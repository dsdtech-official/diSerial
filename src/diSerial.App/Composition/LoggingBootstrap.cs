using System.Globalization;
using DiSerial.App.Services;
using DiSerial.Core.Abstractions;
using DiSerial.Infrastructure.Diagnostics;
using DiSerial.Infrastructure.Time;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Serilog.Formatting.Compact;
using Serilog.Formatting.Display;

namespace DiSerial.App.Composition;

// Configure application logging.
public static class LoggingBootstrap
{
    private const string TextTemplate =
        "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}";

    public static LoggingRuntime Current { get; private set; } = LoggingRuntime.Disabled();

    // Initialize application logging.
    public static LoggingRuntime Initialize(string[]? args = null)
    {
        var overrides = DeveloperSwitchOverrides.Parse(args);

        var allowFormOverride = !AppInfo.IsPackagedBuild;

        var developer = DeveloperOptions.Load(
            logger: null, overrides: overrides, allowFormOverride: allowFormOverride);
        var options = LoggingOptions.From(developer);

        var clock = new MonotonicClock();

        var paths = AppPaths.CreateDefault();

        var sessionLog = new SessionLogBuffer();

        if (options.IsDisabled)
        {
            return Current = new LoggingRuntime(
                NullLoggerFactory.Instance, clock, options, paths,
                logDirectory: null, logger: null, developer: developer, sessionLog: sessionLog);
        }

        var logger = TryCreateLogger(options, clock, paths, sessionLog, out var directory);

        if (logger is null)
        {
            return Current = new LoggingRuntime(
                NullLoggerFactory.Instance, clock, options, paths,
                logDirectory: null, logger: null, developer: developer, sessionLog: sessionLog);
        }

        var factory = new SerilogLoggerFactory(logger, dispose: false);

        _ = DeveloperOptions.Load(
            logger: factory.CreateLogger("Diagnostics.Developer"),
            overrides: overrides,
            allowFormOverride: allowFormOverride);

        Avalonia.Logging.Logger.Sink = new AvaloniaLogBridge(factory);

        var runtime = new LoggingRuntime(
            factory, clock, options, paths, directory, logger, developer, sessionLog);

        WriteStartupBanner(factory, clock, directory!, developer, overrides);
        return Current = runtime;
    }

    // Log unhandled exceptions.
    public static void HookUnhandledExceptions(ILoggerFactory factory)
    {
        var logger = factory.CreateLogger("Diagnostics.Crash");

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                if (MarkLogged(ex))
                {
                    logger.LogCritical(ex, "Unhandled exception, terminating={Terminating}", e.IsTerminating);
                }
            }
            else
            {
                logger.LogCritical("Unhandled non-exception throw, terminating={Terminating}", e.IsTerminating);
            }

            Log.CloseAndFlush();
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            logger.LogError(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };
    }

    // Mark an exception as logged.
    internal static bool MarkLogged(Exception exception)
        => Interlocked.Exchange(ref _lastLogged, exception) != exception;

    private static Exception? _lastLogged;

    // Try to create the file logger.
    private static Logger? TryCreateLogger(
        LoggingOptions options, IMonotonicClock clock, IAppPaths paths,
        SessionLogBuffer sessionLog, out string? directory)
    {
        directory = null;

        try
        {
            Directory.CreateDirectory(paths.LogDirectory);
            directory = paths.LogDirectory;

            var level = ToSerilogLevel(options.MinimumLevel);

            return new LoggerConfiguration()
                .MinimumLevel.Is(level)

                .MinimumLevel.Override("Avalonia", LogEventLevel.Warning)

                .Enrich.With(new MonotonicTimestampEnricher(clock))
                .Enrich.WithProperty("pid", Environment.ProcessId)

                .WriteTo.Async(a => a.File(
                    Path.Combine(paths.LogDirectory, "diserial-.log"),
                    outputTemplate: TextTemplate,
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: LoggingOptions.FileSizeLimitBytes,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: LoggingOptions.RetainedFileCount,
                    shared: false))

                .WriteTo.Async(a => a.File(
                    new CompactJsonFormatter(),
                    Path.Combine(paths.LogDirectory, "diserial-.jsonl"),
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: LoggingOptions.FileSizeLimitBytes,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: LoggingOptions.RetainedFileCount,
                    shared: false))

                .WriteTo.Sink(
                    new SessionLogBufferSink(sessionLog),
                    restrictedToMinimumLevel: LogEventLevel.Information)

                .CreateLogger();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    // Describe the source.
    internal static string DescribeSource(string value, bool fromCommandLine) =>
        fromCommandLine ? value + " (command line)" : value;

    // Describe developer mode.
    internal static string DescribeDebugMode(bool effective, bool? asked) =>
        effective
            ? DescribeSource("True", asked is true)
            : asked is true
                ? "False (--dev refused: packaged build)"
                : "False";

    // Log application startup details.
    private static void WriteStartupBanner(
        ILoggerFactory factory, IMonotonicClock clock, string logDirectory,
        DeveloperOptions developer, DeveloperSwitchOverrides overrides)
    {
        var debugModeFact = DescribeDebugMode(developer.DebugMode, overrides.DebugMode);

        var diagnostics = PlatformDiagnostics.Create();
        var facts = diagnostics.Collect(new PlatformDiagnosticsContext(clock, logDirectory))
            .Append(new KeyValuePair<string, string>("developer.debugMode", debugModeFact))
            .Append(new KeyValuePair<string, string>(
                "developer.replay",
                developer.DebugMode ? developer.Replay ?? "off" : "off (user form)"))
            .Append(new KeyValuePair<string, string>(
                "developer.logLevel",
                DescribeSource(developer.LogLevel ?? "info", overrides.LogLevel is not null)))
            .Append(new KeyValuePair<string, string>(
                "developer.idleGap", developer.IdleGapDescription));

        factory.CreateLogger("Diagnostics.Startup").LogInformation(
            "diSerial starting on {Platform}. Environment: {@Environment}",
            diagnostics.PlatformName,
            facts.ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal));
    }

    // Map to a Serilog log level.
    private static LogEventLevel ToSerilogLevel(LogLevel level) => level switch
    {
        LogLevel.Trace => LogEventLevel.Verbose,
        LogLevel.Debug => LogEventLevel.Debug,
        LogLevel.Information => LogEventLevel.Information,
        LogLevel.Warning => LogEventLevel.Warning,
        LogLevel.Error => LogEventLevel.Error,
        LogLevel.Critical => LogEventLevel.Fatal,
        _ => LogEventLevel.Information
    };

    // Buffer session log events.
    private sealed class SessionLogBufferSink(SessionLogBuffer buffer) : ILogEventSink
    {
        private static readonly MessageTemplateTextFormatter Renderer =
            new("{Message:lj}", CultureInfo.InvariantCulture);

        // Forward a structured log event.
        public void Emit(LogEvent logEvent)
        {
            var text = new StringWriter(CultureInfo.InvariantCulture);
            Renderer.Format(logEvent, text);

            buffer.Append(
                logEvent.Timestamp,
                ToMicrosoftLevel(logEvent.Level),
                CategoryOf(logEvent),
                text.ToString());
        }

        // Select the logging category.
        private static string CategoryOf(LogEvent logEvent) =>
            logEvent.Properties.TryGetValue("SourceContext", out var value)
            && value is ScalarValue { Value: string context }
                ? context
                : string.Empty;

        // Map to a Microsoft log level.
        private static LogLevel ToMicrosoftLevel(LogEventLevel level) => level switch
        {
            LogEventLevel.Verbose => LogLevel.Trace,
            LogEventLevel.Debug => LogLevel.Debug,
            LogEventLevel.Information => LogLevel.Information,
            LogEventLevel.Warning => LogLevel.Warning,
            LogEventLevel.Error => LogLevel.Error,
            LogEventLevel.Fatal => LogLevel.Critical,
            _ => LogLevel.Information,
        };
    }

}

// Own the logging pipeline.
public sealed class LoggingRuntime(
    ILoggerFactory loggerFactory,
    IMonotonicClock clock,
    LoggingOptions options,
    IAppPaths paths,
    string? logDirectory,
    Logger? logger,
    DeveloperOptions? developer = null,
    SessionLogBuffer? sessionLog = null) : IDisposable
{
    public ILoggerFactory LoggerFactory { get; } = loggerFactory;

    public SessionLogBuffer SessionLog { get; } = sessionLog ?? new SessionLogBuffer();

    public DeveloperOptions Developer { get; } = developer ?? DeveloperOptions.Disabled;

    public IMonotonicClock Clock { get; } = clock;

    public LoggingOptions Options { get; } = options;

    public IAppPaths Paths { get; } = paths;

    public string? LogDirectory { get; } = logDirectory;

    // Create a disabled log runtime.
    public static LoggingRuntime Disabled() => new(
        NullLoggerFactory.Instance,
        new MonotonicClock(),
        new LoggingOptions { MinimumLevel = LogLevel.None },
        AppPaths.CreateDefault(),
        logDirectory: null,
        logger: null);

    // Close log sinks and services.
    public void Dispose()
    {
        logger?.Dispose();
        Log.CloseAndFlush();
    }
}

// Stamp events with elapsed time.
internal sealed class MonotonicTimestampEnricher(IMonotonicClock clock) : ILogEventEnricher
{
    private readonly DateTimeOffset _origin = clock.Now;

    // Add monotonic time to a log.
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory factory)
    {
        var now = clock.Now;

        logEvent.AddPropertyIfAbsent(factory.CreateProperty("MonoTime", now));
        logEvent.AddPropertyIfAbsent(factory.CreateProperty(
            "MonoMs", Math.Round((now - _origin).TotalMilliseconds, 3)));
    }
}
