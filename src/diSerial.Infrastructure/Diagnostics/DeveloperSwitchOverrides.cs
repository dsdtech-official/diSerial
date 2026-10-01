using System.Collections.ObjectModel;

namespace DiSerial.Infrastructure.Diagnostics;

// Hold command-line overrides.
public sealed record DeveloperSwitchOverrides
{
    public const string DevSwitch = "--dev";

    public const string LogLevelSwitch = "--log-level";

    public bool? DebugMode { get; init; }

    public string? LogLevel { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public static DeveloperSwitchOverrides None { get; } = new();

    public bool IsEmpty => DebugMode is null && LogLevel is null;

    public string Description
    {
        get
        {
            if (IsEmpty) return "none";

            var parts = new List<string>(2);
            if (DebugMode is true) parts.Add(DevSwitch);
            if (LogLevel is { } level) parts.Add($"{LogLevelSwitch}={level}");
            return string.Join(' ', parts);
        }
    }

    // Parse developer command flags.
    public static DeveloperSwitchOverrides Parse(IEnumerable<string>? args)
    {
        if (args is null) return None;

        bool? debugMode = null;
        string? logLevel = null;
        List<string>? warnings = null;

        foreach (var raw in args)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var argument = raw.Trim();

            var separator = argument.IndexOf('=');
            var name = (separator < 0 ? argument : argument[..separator]).ToLowerInvariant();
            var value = separator < 0 ? null : argument[(separator + 1)..];

            if (name == DevSwitch && value is null)
            {
                debugMode = true;
                continue;
            }

            if (name == LogLevelSwitch && !string.IsNullOrWhiteSpace(value))
            {
                if (LoggingOptions.TryParseLevel(value) is null)
                {
                    (warnings ??= []).Add(
                        $"{argument}: '{value.Trim()}' is not a log level "
                        + "(off, error, warning, info, debug, trace). Ignored.");
                    continue;
                }

                logLevel = value.Trim();
                continue;
            }

            if (IsOwnNamespace(name))
            {
                (warnings ??= []).Add($"{argument}: not a recognised diSerial switch. Ignored.");
            }
        }

        return new DeveloperSwitchOverrides
        {
            DebugMode = debugMode,
            LogLevel = logLevel,
            Warnings = warnings is null
                ? Array.Empty<string>()
                : new ReadOnlyCollection<string>(warnings)
        };
    }

    // Identify app command switches.
    private static bool IsOwnNamespace(string name) =>
        name.StartsWith("--dev", StringComparison.Ordinal)
        || name.StartsWith("--log", StringComparison.Ordinal);
}
