using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Diagnostics;

// Hold developer switch values.
public sealed record DeveloperOptions
{
    public const string FileName = "diserial.dev.json";

    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public bool DebugMode { get; init; }

    public string? LogLevel { get; init; }

    public bool LogPayload { get; init; }

    public string? Replay { get; init; }

    public int ReplayWindowMs { get; init; }

    public double? IdleGapMs { get; init; }

    public TimeSpan? IdleGapOverride =>
        IdleGapMs is { } ms and >= 0 ? TimeSpan.FromMilliseconds(ms) : null;

    public string IdleGapDescription => IdleGapMs switch
    {
        null => "auto",
        < 0 => "auto (negative override ignored)",
        0 => "0 (every chunk becomes one frame)",
        { } ms => ms.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "ms"
    };

    public static DeveloperOptions Disabled { get; } = new();

    // Apply developer overrides.
    public DeveloperOptions WithOverrides(DeveloperSwitchOverrides? overrides, bool allowFormOverride)
    {
        if (overrides is null || overrides.IsEmpty) return this;

        var result = this;

        if (overrides.DebugMode is { } debugMode && allowFormOverride)
        {
            result = result with { DebugMode = debugMode };
        }

        if (overrides.LogLevel is { } logLevel)
        {
            result = result with { LogLevel = logLevel };
        }

        return result;
    }

    // Load effective developer flags.

    public static DeveloperOptions Load(
        string? directory = null,
        ILogger? logger = null,
        DeveloperSwitchOverrides? overrides = null,
        bool allowFormOverride = false)
    {
        var switches = overrides ?? DeveloperSwitchOverrides.None;
        var path = Path.Combine(directory ?? AppContext.BaseDirectory, FileName);

        var fromFile = ReadFile(path, logger);
        var effective = (fromFile ?? Disabled).WithOverrides(switches, allowFormOverride);

        Report(path, fromFile is not null, effective, switches, allowFormOverride, logger);

        return effective;
    }

    // Read the developer options file.
    private static DeveloperOptions? ReadFile(string path, ILogger? logger)
    {
        if (!File.Exists(path)) return null;

        try
        {
            var json = File.ReadAllText(path);

            if (string.IsNullOrWhiteSpace(json)) return Disabled;

            var parsed = JsonSerializer.Deserialize(json, DeveloperOptionsContext.Default.DeveloperOptions);
            if (parsed is null) return Disabled;

            if (parsed.SchemaVersion == 0) parsed = parsed with { SchemaVersion = CurrentSchemaVersion };

            if (parsed.SchemaVersion != CurrentSchemaVersion)
            {
                logger?.LogWarning(
                    "{Path} declares schemaVersion {Actual}, this build understands {Expected}. "
                    + "Unrecognised keys are ignored.",
                    path, parsed.SchemaVersion, CurrentSchemaVersion);
            }

            return parsed;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            logger?.LogWarning(e,
                "Failed to read {Path}; developer options are disabled for this run.", path);
            return null;
        }
    }

    // Log an option loading failure.
    private static void Report(
        string path,
        bool fileUsable,
        DeveloperOptions effective,
        DeveloperSwitchOverrides switches,
        bool allowFormOverride,
        ILogger? logger)
    {
        foreach (var warning in switches.Warnings)
        {
            logger?.LogWarning("Command line: {Warning}", warning);
        }

        if (switches.DebugMode is true && !allowFormOverride)
        {
            logger?.LogWarning(
                "{Switch} was given, but this is a packaged build and it is IGNORED. "
                + "Developer form is reachable from a locally built binary only.",
                DeveloperSwitchOverrides.DevSwitch);
        }

        if (!fileUsable && switches.IsEmpty) return;

        var origin = fileUsable ? path : "built-in defaults (no usable " + FileName + ")";

        if (!effective.DebugMode)
        {
            logger?.LogInformation(
                "Developer options from {Origin}, debugMode=false. "
                + "Running in USER FORM: no simulated data. "
                + "logLevel={Level}, idleGap={IdleGap}, replay={Replay} (ignored in user form). "
                + "Command line: {Switches}.",
                origin, effective.LogLevel ?? "info", effective.IdleGapDescription,
                effective.Replay ?? "off", switches.Description);
        }
        else
        {
            logger?.LogWarning(
                "Developer options ACTIVE from {Origin}: DEVELOPER FORM. "
                + "debugMode=true, replay={Replay}, logLevel={Level}, idleGap={IdleGap}. "
                + "Command line: {Switches}. Displayed traffic may be SIMULATED.",
                origin, effective.Replay ?? "off", effective.LogLevel ?? "info",
                effective.IdleGapDescription, switches.Description);
        }
    }
}

// Serialize developer options.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(DeveloperOptions))]
internal sealed partial class DeveloperOptionsContext : JsonSerializerContext;
