using Avalonia.Logging;
using Microsoft.Extensions.Logging;
using AvaloniaLevel = Avalonia.Logging.LogEventLevel;
using MelLevel = Microsoft.Extensions.Logging.LogLevel;

namespace DiSerial.App.Composition;

// Route Avalonia log events.
internal sealed class AvaloniaLogBridge(ILoggerFactory factory) : ILogSink
{
    private readonly Dictionary<string, ILogger> _loggers = [];
    private readonly Lock _gate = new();

    // Check log level eligibility.
    public bool IsEnabled(AvaloniaLevel level, string area)
        => LoggerFor(area).IsEnabled(ToMel(level));

    // Forward an Avalonia log event.
    public void Log(AvaloniaLevel level, string area, object? source, string messageTemplate)
        => Write(level, area, source, messageTemplate, []);

    // Forward an Avalonia log event.
    public void Log<T0>(
        AvaloniaLevel level, string area, object? source, string messageTemplate, T0 pv0)
        => Write(level, area, source, messageTemplate, [pv0]);

    // Forward an Avalonia log event.
    public void Log<T0, T1>(
        AvaloniaLevel level, string area, object? source, string messageTemplate, T0 pv0, T1 pv1)
        => Write(level, area, source, messageTemplate, [pv0, pv1]);

    // Forward an Avalonia log event.
    public void Log<T0, T1, T2>(
        AvaloniaLevel level, string area, object? source, string messageTemplate,
        T0 pv0, T1 pv1, T2 pv2)
        => Write(level, area, source, messageTemplate, [pv0, pv1, pv2]);

    // Forward an Avalonia log event.
    public void Log(
        AvaloniaLevel level, string area, object? source, string messageTemplate,
        params object?[] propertyValues)
        => Write(level, area, source, messageTemplate, propertyValues);

    // Forward a formatted log event.
    private void Write(
        AvaloniaLevel level, string area, object? source, string template, object?[] values)
    {
        var logger = LoggerFor(area);
        var mel = ToMel(level);

        if (!logger.IsEnabled(mel)) return;

        try
        {
            logger.Log(mel, template + " [source={AvaloniaSource}]",
                [.. values, Describe(source)]);
        }
        catch (Exception e)
        {
            logger.Log(mel, "Avalonia log formatting failed for template {Template}: {Reason}",
                template, e.Message);
        }
    }

    // Get a logger for an area.
    private ILogger LoggerFor(string area)
    {
        lock (_gate)
        {
            if (_loggers.TryGetValue(area, out var existing)) return existing;

            var created = factory.CreateLogger("Avalonia." + area);
            _loggers[area] = created;
            return created;
        }
    }

    // Describe the log source type.
    private static string Describe(object? source)
        => source?.GetType().Name ?? "none";

    // Map the logging level.
    private static MelLevel ToMel(AvaloniaLevel level) => level switch
    {
        AvaloniaLevel.Verbose => MelLevel.Trace,
        AvaloniaLevel.Debug => MelLevel.Trace,
        AvaloniaLevel.Information => MelLevel.Debug,
        AvaloniaLevel.Warning => MelLevel.Warning,
        AvaloniaLevel.Error => MelLevel.Error,
        AvaloniaLevel.Fatal => MelLevel.Critical,
        _ => MelLevel.Debug
    };
}
