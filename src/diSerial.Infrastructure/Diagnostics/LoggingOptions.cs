using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Diagnostics;

// Define logging configuration.
public sealed record LoggingOptions
{

    public const long FileSizeLimitBytes = 16L * 1024 * 1024;

    public const int RetainedFileCount = 5;

    public required LogLevel MinimumLevel { get; init; }

    public bool IncludePayload { get; init; }

    public bool IsDisabled => MinimumLevel == LogLevel.None;

    public static LoggingOptions Default => new() { MinimumLevel = LogLevel.Information };

    // Build developer logging options.
    public static LoggingOptions From(DeveloperOptions? developer)
    {
        var options = developer ?? DeveloperOptions.Disabled;

        var level = ParseLevel(options.LogLevel);

        var payload = options.DebugMode
                      && options.LogPayload
                      && level == LogLevel.Trace;

        return new LoggingOptions
        {
            MinimumLevel = level,
            IncludePayload = payload
        };
    }

    // Parse the configured log level.
    internal static LogLevel ParseLevel(string? value) => TryParseLevel(value) ?? LogLevel.Information;

    // Try to parse a log level.
    internal static LogLevel? TryParseLevel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        return value.Trim().ToLowerInvariant() switch
        {
            "off" or "none" or "0" or "false" => LogLevel.None,
            "critical" or "fatal" => LogLevel.Critical,
            "error" => LogLevel.Error,
            "warn" or "warning" => LogLevel.Warning,
            "info" or "information" or "1" or "true" => LogLevel.Information,
            "debug" => LogLevel.Debug,
            "trace" or "verbose" => LogLevel.Trace,
            _ => null
        };
    }

}
