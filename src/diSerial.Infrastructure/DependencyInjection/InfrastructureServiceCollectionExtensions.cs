using DiSerial.Core.Abstractions;
using DiSerial.Infrastructure.Decoding;
using DiSerial.Infrastructure.Diagnostics;
using DiSerial.Infrastructure.Export;
using DiSerial.Infrastructure.Formatting;
using DiSerial.Infrastructure.Recording;
using DiSerial.Infrastructure.Replay;
using DiSerial.Infrastructure.Serial;
using DiSerial.Infrastructure.Sessions;
using DiSerial.Infrastructure.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.DependencyInjection;

// Register platform services.
public static class InfrastructureServiceCollectionExtensions
{
    // Register platform services.
    public static IServiceCollection AddDiSerialInfrastructure(
        this IServiceCollection services,
        IMonotonicClock? clock = null,
        ILoggerFactory? loggerFactory = null,
        LoggingOptions? loggingOptions = null,
        DeveloperOptions? developerOptions = null,
        Func<IServiceProvider, TimeSpan?>? userIdleGap = null)
    {
        var developer = developerOptions ?? DeveloperOptions.Disabled;

        if (clock is not null) services.AddSingleton(clock);
        else services.AddSingleton<IMonotonicClock, MonotonicClock>();

        services.AddSingleton(loggerFactory ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton(loggingOptions ?? LoggingOptions.Default);

        services.AddSingleton<IFrameFormatter, FrameFormatter>();

        services.AddSingleton<IFrameSplitterFactory>(sp =>
            new IdleGapFrameSplitterFactory(
                developer.IdleGapOverride,
                sp.GetRequiredService<ILoggerFactory>().CreateLogger("Session"),
                userIdleGap is null ? null : () => userIdleGap(sp)));

        services.AddSingleton<IDecoderRegistry, DecoderRegistry>();

        var replayOptions = ReplayConfiguration.From(developer);
        var replayEnabled = replayOptions is not null;

        if (replayOptions is not null)
        {
            services.AddSingleton<ISerialPortFactory>(sp => new ReplayAwareSerialPortFactory(
                new SerialPortFactory(
                    sp.GetRequiredService<IMonotonicClock>(),
                    sp.GetRequiredService<ILoggerFactory>(),
                    sp.GetRequiredService<LoggingOptions>()),
                sp.GetRequiredService<IMonotonicClock>(),
                replayOptions,
                sp.GetRequiredService<ILoggerFactory>()));

        }
        else
        {
            services.AddSingleton<ISerialPortFactory>(sp => new SerialPortFactory(
                sp.GetRequiredService<IMonotonicClock>(),
                sp.GetRequiredService<ILoggerFactory>(),
                sp.GetRequiredService<LoggingOptions>()));
        }

        services.AddSingleton<IPortEnumerator>(sp =>
        {
            IPortEnumerator enumerator =
                new SystemPortEnumerator(sp.GetRequiredService<ILoggerFactory>());

            if (replayEnabled) enumerator = new ReplayAwarePortEnumerator(enumerator);
            return enumerator;
        });

        services.AddSingleton<ICaptureSessionFactory, CaptureSessionFactory>();

        services.AddSingleton<IDeviceWatcher>(sp => new PollingDeviceWatcher(
            sp.GetRequiredService<IPortEnumerator>(),
            loggerFactory?.CreateLogger("Devices")));

        services.AddSingleton<IExportService, TabularExportService>();

        services.AddSingleton<IRecordingReader>(sp => new SqliteRecordingReader(
            sp.GetRequiredService<IAppPaths>(),
            loggerFactory?.CreateLogger("Recording")));

        services.AddSingleton<IRecordingCatalog>(sp => new SqliteRecordingCatalog(
            sp.GetRequiredService<IAppPaths>(),
            loggerFactory?.CreateLogger("Recording")));

        services.AddSingleton(AppPaths.CreateDefault());

        services.AddSingleton<ISessionRecorderFactory, SessionRecorderFactory>();

        services.AddSingleton<ISendHistoryStore, SqliteSendHistoryStore>();

        services.AddSingleton<IShortcutStore, SqliteShortcutStore>();

        services.AddSingleton<IAutoAnswerStore, SqliteAutoAnswerStore>();

        services.AddSingleton<ISettingsStore, Settings.SqliteSettingsStore>();

        services.AddSingleton<IVolatileSendHistoryStore, VolatileSendHistoryStore>();


        return services;
    }
}
