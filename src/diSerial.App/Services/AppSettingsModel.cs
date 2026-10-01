using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;

namespace DiSerial.App.Services;

// Hold application preferences.
public sealed record AppSettingsModel
{
    public string? Language { get; set; }

    public string? LastExportDirectory { get; set; }

    public AppOptions Options { get; set; } = new();

    public SessionPreferences Terminal { get; set; } = SessionPreferences.TerminalDefaults;

    public SessionPreferences Monitor { get; set; } = SessionPreferences.MonitorDefaults;

}

// Hold session preferences.
public sealed record SessionPreferences
{
    public SerialPreferences Serial { get; set; } = new();

    public DisplayPreferences Display { get; set; } = new();

    public static SessionPreferences TerminalDefaults => new()
    {
        Display = new DisplayPreferences
        {
            Timestamp = TimestampMode.Relative,
            ShowChannel = true,
            ShowDelta = false
        }
    };

    public static SessionPreferences MonitorDefaults => new()
    {
        Display = new DisplayPreferences
        {
            Timestamp = TimestampMode.Absolute,
            ShowChannel = true,
            ShowDelta = true
        }
    };
}

// Hold serial port preferences.
public sealed record SerialPreferences
{
    private static readonly SerialPortSettings CoreDefaults = new();

    public int BaudRate { get; set; } = CoreDefaults.BaudRate;

    public int DataBits { get; set; } = CoreDefaults.DataBits;

    public SerialParity Parity { get; set; } = CoreDefaults.Parity;

    public SerialStopBits StopBits { get; set; } = CoreDefaults.StopBits;

    public SerialFlowControl FlowControl { get; set; } = CoreDefaults.FlowControl;

    // Build serial port settings.
    public SerialPortSettings ToSettings() => new()
    {
        BaudRate = BaudRate,
        DataBits = DataBits,
        Parity = Parity,
        StopBits = StopBits,
        FlowControl = FlowControl
    };

    // Copy serial port preferences.
    public static SerialPreferences From(SerialPortSettings settings) => new()
    {
        BaudRate = settings.BaudRate,
        DataBits = settings.DataBits,
        Parity = settings.Parity,
        StopBits = settings.StopBits,
        FlowControl = settings.FlowControl
    };
}

// Hold frame display preferences.
public sealed record DisplayPreferences
{
    public DisplayFormat Format { get; set; } = DisplayFormat.HexAndAscii;

    public TimestampMode Timestamp { get; set; } = TimestampMode.Absolute;

    public bool ShowChannel { get; set; }

    public bool ShowDelta { get; set; }

    public bool ShowSent { get; set; } = true;
}



// Hold application-wide options.
public sealed record AppOptions
{
    public bool AutoReconnect { get; set; }

    public int ReconnectAttempts { get; set; } = DefaultReconnectAttempts;

    public int ReconnectDelayMs { get; set; } = DefaultReconnectDelayMs;

    public int MaxDisplayedFrames { get; set; } = DefaultMaxDisplayedFrames;

    public int PanelHeight { get; set; } = DefaultPanelHeight;

    public ExportFormat LastExportFormat { get; set; } = ExportFormat.Tsv;

    public bool IdleGapAuto { get; set; } = false;

    public int IdleGapMs { get; set; } = DefaultIdleGapMs;

    public const int MinReconnectAttempts = 1;
    public const int MaxReconnectAttempts = 100;
    public const int DefaultReconnectAttempts = 3;

    public const int MinReconnectDelayMs = 200;
    public const int MaxReconnectDelayMs = 60_000;
    public const int DefaultReconnectDelayMs = 2_000;

    public const int MinPanelHeight = 120;
    public const int MaxPanelHeight = 600;
    public const int DefaultPanelHeight = 201;

    public const int MinDisplayedFrames = 100;
    public const int MaxAllowedDisplayedFrames = 5_000;
    public const int DefaultMaxDisplayedFrames = 500;

    public const int MinIdleGapMs = 1;
    public const int MaxIdleGapMs = 1_000;

    public const int DefaultIdleGapMs = 10;

    // Clamp reconnect attempt counts.
    public static int ClampAttempts(int value) =>
        Math.Clamp(value, MinReconnectAttempts, MaxReconnectAttempts);

    // Clamp the send panel height.
    public static int ClampPanelHeight(int value) =>
        Math.Clamp(value, MinPanelHeight, MaxPanelHeight);

    // Clamp the reconnect delay.
    public static int ClampDelayMs(int value) =>
        Math.Clamp(value, MinReconnectDelayMs, MaxReconnectDelayMs);

    // Clamp the displayed frame limit.
    public static int ClampFrames(int value) =>
        Math.Clamp(value, MinDisplayedFrames, MaxAllowedDisplayedFrames);

    // Clamp the idle grouping delay.
    public static int ClampIdleGapMs(int value) =>
        Math.Clamp(value, MinIdleGapMs, MaxIdleGapMs);

    public TimeSpan? IdleGapOverride =>
        IdleGapAuto ? null : TimeSpan.FromMilliseconds(ClampIdleGapMs(IdleGapMs));
}
