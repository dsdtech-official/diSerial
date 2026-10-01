using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;

namespace DiSerial.App.Services;

public enum SettingsPersistence
{
    SettingsDb,

    Never,

    RecordingsDb
}

public enum SettingsScope
{
    Global,

    Terminal,

    Monitor,

    Both
}

// Describe a settings parameter.
public sealed record SettingsParameter
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required SettingsScope Scope { get; init; }

    public required SettingsPersistence Persistence { get; init; }

    public required string Notes { get; init; }

    public Func<AppSettingsModel, string>? Read { get; init; }

    public Func<AppSettingsModel, string, AppSettingsModel?>? Write { get; init; }

    public string? UncoveredLeaf { get; init; }

    public string Label { get; init; } = string.Empty;

    public string ValueType { get; init; } = string.Empty;
}

// Define application settings.
public static class SettingsCatalog
{
    public const int MaxAssignedId = 41;


    private static readonly Dictionary<string, string> ValueTypes = new(StringComparer.Ordinal)
    {
        ["Baud rate"] = "int",
        ["Data bits"] = "int",
        ["Parity"] = "enum",
        ["Stop bits"] = "enum",
        ["Flow control"] = "enum",
        ["Display format"] = "enum",
        ["Timestamp mode"] = "enum",
        ["Show channel column"] = "bool",
        ["Show delta column"] = "bool",
        ["Show sent data"] = "bool"
    };

    // Get the settings scope suffix.
    private static string ScopeSuffix(SettingsScope scope) => scope switch
    {
        SettingsScope.Terminal => " (terminal)",
        SettingsScope.Monitor => " (monitor)",
        SettingsScope.Global => string.Empty,
        _ => " (both)"
    };

    private static readonly SettingsParameter[] Entries =
    [
        new()
        {
            Id = "P01",
            Name = "UI language",
            Scope = SettingsScope.Global,
            Persistence = SettingsPersistence.SettingsDb,
            Label = "UI language",
            ValueType = "string",
            Notes = "Language menu. An unset value follows the system UI language.",
            Read = m => m.Language ?? string.Empty,
            Write = (m, raw) => m with { Language = raw.Length == 0 ? null : raw }
        },

        new()
        {
            Id = "P33",
            Name = "Last export directory",
            Scope = SettingsScope.Both,
            Persistence = SettingsPersistence.SettingsDb,
            Label = "Last export directory",
            ValueType = "string",
            Notes = "Export dialog. Keep the last successful export directory.",
            Read = m => m.LastExportDirectory ?? string.Empty,
            Write = (m, raw) => m with { LastExportDirectory = raw.Length == 0 ? null : raw }
        },

        new()
        {
            Id = "P34",
            Name = "Auto-reconnect",
            Scope = SettingsScope.Both,
            Persistence = SettingsPersistence.SettingsDb,
            Label = "Auto-reconnect after an unexpected disconnect",
            ValueType = "bool",
            Notes = "Options dialog. Reconnect only after an unexpected disconnect.",
            Read = m => m.Options.AutoReconnect.ToString(),
            Write = (m, raw) => bool.TryParse(raw, out var b)
                ? m with { Options = m.Options with { AutoReconnect = b } }
                : null
        },

        new()
        {
            Id = "P35",
            Name = "Reconnect attempts",
            Scope = SettingsScope.Both,
            Persistence = SettingsPersistence.SettingsDb,
            Label = "Reconnect attempts before giving up",
            ValueType = "int",
            Notes = "Options dialog. Limit reconnect attempts to 1..100.",
            Read = m => m.Options.ReconnectAttempts.ToString(),
            Write = (m, raw) => int.TryParse(raw, out var n)
                ? m with { Options = m.Options with { ReconnectAttempts = AppOptions.ClampAttempts(n) } }
                : null
        },

        new()
        {
            Id = "P36",
            Name = "Reconnect delay",
            Scope = SettingsScope.Both,
            Persistence = SettingsPersistence.SettingsDb,
            Label = "Milliseconds between reconnect attempts",
            ValueType = "int",
            Notes = "Options dialog. Reconnect delay is limited to 200..60000 ms.",
            Read = m => m.Options.ReconnectDelayMs.ToString(),
            Write = (m, raw) => int.TryParse(raw, out var n)
                ? m with { Options = m.Options with { ReconnectDelayMs = AppOptions.ClampDelayMs(n) } }
                : null
        },

        new()
        {
            Id = "P37",
            Name = "Max displayed frames",
            Scope = SettingsScope.Both,
            Persistence = SettingsPersistence.SettingsDb,
            Label = "Frames kept in the display area",
            ValueType = "int",
            Notes = "Options dialog. Retain 100..5000 visible frames.",
            Read = m => m.Options.MaxDisplayedFrames.ToString(),
            Write = (m, raw) => int.TryParse(raw, out var n)
                ? m with { Options = m.Options with { MaxDisplayedFrames = AppOptions.ClampFrames(n) } }
                : null
        },

        new()
        {
            Id = "P38",
            Name = "Last export format",
            Scope = SettingsScope.Both,
            Persistence = SettingsPersistence.SettingsDb,
            Label = "Format of the last successful export",
            ValueType = "enum",
            Notes = "Export dialog. Keep the last successfully used export format.",
            Read = m => m.Options.LastExportFormat.ToString(),
            Write = (m, raw) => Enum.TryParse<ExportFormat>(raw, out var f) && Enum.IsDefined(f)
                ? m with { Options = m.Options with { LastExportFormat = f } }
                : null
        },

        new()
        {
            Id = "P39",
            Name = "Panel tab height",
            Scope = SettingsScope.Terminal,
            Persistence = SettingsPersistence.SettingsDb,
            Label = "Height of the terminal session's panel tabs",
            ValueType = "int",
            Notes = "Remember panel height, limited to 120..600 pixels.",
            Read = m => m.Options.PanelHeight.ToString(),
            Write = (m, raw) => int.TryParse(raw, out var n)
                ? m with { Options = m.Options with { PanelHeight = AppOptions.ClampPanelHeight(n) } }
                : null
        },

        new()
        {
            Id = "P40",
            Name = "Automatic framing threshold",
            Scope = SettingsScope.Both,
            Persistence = SettingsPersistence.SettingsDb,
            Label = "Derive the framing threshold from the port settings",
            ValueType = "bool",
            Notes = "Reception options. Automatic idle-gap calculation is off by default.",
            Read = m => m.Options.IdleGapAuto.ToString(),
            Write = (m, raw) => bool.TryParse(raw, out var b)
                ? m with { Options = m.Options with { IdleGapAuto = b } }
                : null
        },

        new()
        {
            Id = "P41",
            Name = "Framing threshold",
            Scope = SettingsScope.Both,
            Persistence = SettingsPersistence.SettingsDb,
            Label = "Milliseconds of silence that end a frame",
            ValueType = "int",
            Notes = "Reception options. Apply a fixed idle gap on the next connection.",
            Read = m => m.Options.IdleGapMs.ToString(),
            Write = (m, raw) => int.TryParse(raw, out var n)
                ? m with { Options = m.Options with { IdleGapMs = AppOptions.ClampIdleGapMs(n) } }
                : null
        },

        Serial("P02", "Baud rate", SettingsScope.Terminal),
        Serial("P03", "Data bits", SettingsScope.Terminal),
        Serial("P04", "Parity", SettingsScope.Terminal),
        Serial("P05", "Stop bits", SettingsScope.Terminal),
        Serial("P06", "Flow control", SettingsScope.Terminal),

        Display("P07", "Display format", SettingsScope.Terminal),
        Display("P08", "Timestamp mode", SettingsScope.Terminal),
        Display("P09", "Show channel column", SettingsScope.Terminal),
        Display("P10", "Show delta column", SettingsScope.Terminal),
        Display("P11", "Show sent data", SettingsScope.Terminal),

        Serial("P12", "Baud rate", SettingsScope.Monitor),
        Serial("P13", "Data bits", SettingsScope.Monitor),
        Serial("P14", "Parity", SettingsScope.Monitor),
        Serial("P15", "Stop bits", SettingsScope.Monitor),
        Serial("P16", "Flow control", SettingsScope.Monitor),

        Display("P17", "Display format", SettingsScope.Monitor),
        Display("P18", "Timestamp mode", SettingsScope.Monitor),
        Display("P19", "Show channel column", SettingsScope.Monitor),
        Display("P20", "Show delta column", SettingsScope.Monitor),

        new()
        {
            Id = "P21",
            Name = "Show sent data",
            Scope = SettingsScope.Monitor,
            Persistence = SettingsPersistence.Never,
            UncoveredLeaf = "Monitor.Display.ShowSent",
            Notes = "Monitor sessions always display sent frames."
        },

        NotPersisted("P22", "Send format (ASCII / HEX)", SettingsScope.Both,
            "Send area radio buttons. Back to ASCII every launch: it changes the bytes sent."),
        NotPersisted("P23", "Line ending", SettingsScope.Both,
            "Send area dropdown. Back to None every launch: it changes the bytes sent."),
        NotPersisted("P25", "DTR output", SettingsScope.Terminal,
            "Serial signals panel. Unticked every launch: it drives a real line."),
        NotPersisted("P26", "RTS output", SettingsScope.Terminal,
            "Serial signals panel. Unticked every launch: it drives a real line. "
          + "Disabled while hardware flow control owns RTS."),
        NotPersisted("P27", "Send enabled", SettingsScope.Monitor,
            "Monitor sessions start with sending disabled. It is the bus injection gate; "
          + "Sending requires explicit confirmation."),

        NotPersisted("P24", "Timed send interval", SettingsScope.Terminal,
            "Timed send panel. Back to 1000 ms every launch."),
        NotPersisted("P28", "Port selection", SettingsScope.Both,
            "New session dialog. ⛔ Never persisted: USB serial port names are not stable, so "
          + "a remembered COM3 can silently become someone else's device."),
        NotPersisted("P29", "Channel alias", SettingsScope.Monitor,
            "Monitor session channel headers. Defaults to the port name."),
        NotPersisted("P30", "Scroll follow", SettingsScope.Both,
            "Transient view state, not a preference. Persisting it would start the app "
          + "not following, which reads as broken."),
        NotPersisted("P31", "Paused", SettingsScope.Both,
            "Transient view state, same reason as P30."),

        new()
        {
            Id = "P32",
            Name = "Send history",
            Scope = SettingsScope.Both,
            Persistence = SettingsPersistence.RecordingsDb,
            Notes = "Send history is stored in recordings.db."
        }
    ];

    public static IReadOnlyList<SettingsParameter> All { get; } =
        [.. Entries.OrderBy(e => e.Id, StringComparer.Ordinal)];

    public static IReadOnlyList<SettingsParameter> Persisted { get; } =
        [.. All.Where(e => e.Persistence == SettingsPersistence.SettingsDb)];


    // Get scoped session preferences.
    private static SessionPreferences Section(AppSettingsModel m, SettingsScope scope) =>
        scope == SettingsScope.Terminal ? m.Terminal : m.Monitor;

    // Replace scoped preferences.
    private static AppSettingsModel WithSection(
        AppSettingsModel m, SettingsScope scope, SessionPreferences section) =>
        scope == SettingsScope.Terminal ? m with { Terminal = section } : m with { Monitor = section };

    // Define a serial setting entry.
    private static SettingsParameter Serial(string id, string name, SettingsScope scope) => new()
    {
        Id = id,
        Name = name,
        Scope = scope,
        Persistence = SettingsPersistence.SettingsDb,
        Label = name + ScopeSuffix(scope),
        ValueType = ValueTypes[name],
        Notes = "New session dialog. Save parameters when confirmed.",
        Read = m => ReadSerial(Section(m, scope).Serial, name),
        Write = (m, raw) =>
        {
            var section = Section(m, scope);
            var updated = WriteSerial(section.Serial, name, raw);
            return updated is null ? null : WithSection(m, scope, section with { Serial = updated });
        }
    };

    // Define a display setting entry.
    private static SettingsParameter Display(string id, string name, SettingsScope scope) => new()
    {
        Id = id,
        Name = name,
        Scope = scope,
        Persistence = SettingsPersistence.SettingsDb,
        Label = name + ScopeSuffix(scope),
        ValueType = ValueTypes[name],
        Notes = "Display settings. Save preferences by session type.",
        Read = m => ReadDisplay(Section(m, scope).Display, name),
        Write = (m, raw) =>
        {
            var section = Section(m, scope);
            var updated = WriteDisplay(section.Display, name, raw);
            return updated is null ? null : WithSection(m, scope, section with { Display = updated });
        }
    };

    // Define a transient setting.
    private static SettingsParameter NotPersisted(
        string id, string name, SettingsScope scope, string notes) => new()
    {
        Id = id,
        Name = name,
        Scope = scope,
        Persistence = SettingsPersistence.Never,
        Notes = notes
    };

    // Read the default setting value.
    public static string DefaultValueOf(SettingsParameter parameter) =>
        parameter.Read!(new AppSettingsModel());


    // Read a serial setting value.
    private static string ReadSerial(SerialPreferences s, string name) => name switch
    {
        "Baud rate" => s.BaudRate.ToString(),
        "Data bits" => s.DataBits.ToString(),
        "Parity" => s.Parity.ToString(),
        "Stop bits" => s.StopBits.ToString(),
        "Flow control" => s.FlowControl.ToString(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown serial parameter.")
    };

    // Apply a serial setting value.
    private static SerialPreferences? WriteSerial(SerialPreferences s, string name, string raw) => name switch
    {
        "Baud rate" => int.TryParse(raw, out var b) && b > 0 ? s with { BaudRate = b } : null,
        "Data bits" => int.TryParse(raw, out var d) && d is >= 5 and <= 8 ? s with { DataBits = d } : null,
        "Parity" => Enum.TryParse<SerialParity>(raw, out var p) && Enum.IsDefined(p) ? s with { Parity = p } : null,
        "Stop bits" => Enum.TryParse<SerialStopBits>(raw, out var t) && Enum.IsDefined(t) ? s with { StopBits = t } : null,
        "Flow control" => Enum.TryParse<SerialFlowControl>(raw, out var f) && Enum.IsDefined(f) ? s with { FlowControl = f } : null,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown serial parameter.")
    };

    // Read a display setting value.
    private static string ReadDisplay(DisplayPreferences d, string name) => name switch
    {
        "Display format" => d.Format.ToString(),
        "Timestamp mode" => d.Timestamp.ToString(),
        "Show channel column" => d.ShowChannel.ToString(),
        "Show delta column" => d.ShowDelta.ToString(),
        "Show sent data" => d.ShowSent.ToString(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown display parameter.")
    };

    // Apply a display setting value.
    private static DisplayPreferences? WriteDisplay(DisplayPreferences d, string name, string raw) => name switch
    {
        "Display format" => Enum.TryParse<DisplayFormat>(raw, out var f) && Enum.IsDefined(f) ? d with { Format = f } : null,
        "Timestamp mode" => Enum.TryParse<TimestampMode>(raw, out var t) && Enum.IsDefined(t) ? d with { Timestamp = t } : null,
        "Show channel column" => bool.TryParse(raw, out var c) ? d with { ShowChannel = c } : null,
        "Show delta column" => bool.TryParse(raw, out var e) ? d with { ShowDelta = e } : null,
        "Show sent data" => bool.TryParse(raw, out var s) ? d with { ShowSent = s } : null,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown display parameter.")
    };
}
