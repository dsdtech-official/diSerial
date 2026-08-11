using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Diagnostics;

/// <summary>
/// Runtime configuration for logging. <b>All of it comes from <see cref="DeveloperOptions"/>,
/// i.e. from <c>diserial.dev.json</c>.</b>
///
/// <code>
/// "logLevel"    off | error | warning | info | debug | trace   default: info
/// "logPayload"  true = payload hex may be logged (also requires logLevel=trace)
/// </code>
///
/// <para><b>Why everything moved into the config file:</b> verbosity used to be decided by both
/// the application form and environment variables, with the variables winning. So
/// <c>DISERIAL_LOG=trace</c> took effect even in user form -- the form had no final say. Read
/// from one file in one place, there is no precedence question left.</para>
///
/// <para><b>There are no environment variables any more:</b> <c>DISERIAL_LOG</c>,
/// <c>DISERIAL_LOG_PAYLOAD</c>, <c>DISERIAL_REPLAY</c>, <c>DISERIAL_REPLAY_WINDOW</c> and
/// <c>DISERIAL_LOG_DIR</c> were all removed on 2026-07-28. Logs go to the <c>logs</c>
/// subdirectory of the configuration directory; the location comes from
/// <see cref="IAppPaths"/> and can no longer be overridden.</para>
///
/// <para>⚠️ <b>Payload contents are not logged by default.</b> Serial payloads are the
/// customer's own bus traffic, so three conditions must hold at once: <c>debugMode</c> +
/// <c>logLevel=trace</c> + <c>logPayload</c>.</para>
///
/// <para>The <c>debugMode</c> gate was added on 2026-07-29: in user form the log is not
/// surfaced to the user at all (01-spec 4.7), so recording customer payloads there has no
/// purpose and only a disclosure risk. <b>Gating is not enabling, though -- in developer form
/// the other two gates still have to be opened explicitly.</b></para>
/// </summary>
public sealed record LoggingOptions
{

    /// <summary>Size cap for a single log file. Rolls over to the next file once reached.</summary>
    public const long FileSizeLimitBytes = 16L * 1024 * 1024;

    /// <summary>How many files are kept, current one included. Counted per sink.</summary>
    public const int RetainedFileCount = 5;

    /// <summary>Minimum level to record. <see cref="LogLevel.None"/> means logging is off.</summary>
    public required LogLevel MinimumLevel { get; init; }

    /// <summary>Whether payload contents may be written to the log as hex.</summary>
    public bool IncludePayload { get; init; }

    /// <summary>Whether logging is switched off entirely.</summary>
    public bool IsDisabled => MinimumLevel == LogLevel.None;

    /// <summary>Defaults: Information level, no payload contents.</summary>
    public static LoggingOptions Default => new() { MinimumLevel = LogLevel.Information };

    /// <summary>Reads the level and the payload switch from the developer options.</summary>
    /// <param name="developer">
    /// When null, everything falls back to the defaults (Information, no payload) -- the same
    /// as "there is no dev.json", which is also what the visual designer and similar hosts get.
    /// </param>
    public static LoggingOptions From(DeveloperOptions? developer)
    {
        var options = developer ?? DeveloperOptions.Disabled;

        var level = ParseLevel(options.LogLevel);

        // Payload contents require all three conditions at once, no exceptions:
        //   1. debugMode      -- in user form the log is not surfaced to the user, so recording
        //                        payloads there carries only disclosure risk and no benefit
        //   2. logLevel=trace -- the volume gate
        //   3. logPayload     -- the explicit switch
        //
        // ⚠️ Condition 1 is the gate added on 2026-07-29, the same shape as in
        // ReplayConfiguration.From: it promotes "user form never records customer payloads"
        // from a discipline to a mechanical guarantee. This is the only place that decides it;
        // callers must not check it a second time.
        var payload = options.DebugMode
                      && options.LogPayload
                      && level == LogLevel.Trace;

        return new LoggingOptions
        {
            MinimumLevel = level,
            IncludePayload = payload
        };
    }

    /// <summary>
    /// Anything unrecognised falls back to the default level: a typo in the configuration must
    /// not cost us the log.
    ///
    /// <para>⚠️ <b>Never fall back to <see cref="LogLevel.None"/></b> -- "nothing was recorded
    /// because the configuration was misspelled" is the worst possible failure mode.</para>
    /// </summary>
    internal static LogLevel ParseLevel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return LogLevel.Information;

        return value.Trim().ToLowerInvariant() switch
        {
            "off" or "none" or "0" or "false" => LogLevel.None,
            "critical" or "fatal" => LogLevel.Critical,
            "error" => LogLevel.Error,
            "warn" or "warning" => LogLevel.Warning,
            "info" or "information" or "1" or "true" => LogLevel.Information,
            "debug" => LogLevel.Debug,
            "trace" or "verbose" => LogLevel.Trace,
            _ => LogLevel.Information
        };
    }

}
