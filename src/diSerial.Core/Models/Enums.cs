namespace DiSerial.Core.Models;

public enum SessionKind
{
    Terminal,

    Monitor
}

public enum ChannelId
{
    None = 0,
    A = 1,
    B = 2
}

public enum FrameDirection
{
    Rx,

    Tx
}

public enum DisplayFormat
{
    Ascii,
    Hex,
    HexAndAscii
}

public enum TimestampMode
{
    None,

    Absolute,

    Relative
}

public enum MonitorViewMode
{
    Merged,

    Split,

    ChannelAOnly,

    ChannelBOnly
}

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Faulted
}

public enum SerialParity
{
    None,
    Odd,
    Even,
    Mark,
    Space
}

public enum SerialStopBits
{
    One,
    OnePointFive,
    Two
}

public enum SerialFlowControl
{
    None,
    RequestToSend,
    XOnXOff
}

public enum SerialOutputLine
{
    Dtr,

    Rts
}

public enum ControlLineState
{
    Unknown,

    Low,

    High
}

public readonly record struct SerialControlLines(
    ControlLineState Cts,
    ControlLineState Dsr,
    ControlLineState Dcd)
{
    public static SerialControlLines Unknown => new(
        ControlLineState.Unknown, ControlLineState.Unknown, ControlLineState.Unknown);
}

public enum PlatformSupportStatus
{
    Supported,

    MacOsNotImplemented,

    UnknownPlatform
}

public enum SerialErrorKind
{
    Unknown,

    PortNotFound,

    AccessDenied,

    DeviceRemoved,

    Timeout,

    NotConnected,

    InvalidInput,

    IntervalTooSmall,

    NothingToSend,

    RecordingFailed,

    LineError
}

[Flags]
public enum FrameFlags
{
    None = 0,
    ParityError = 1 << 0,
    FramingError = 1 << 1,
    BufferOverrun = 1 << 2,

    ProtocolError = 1 << 3
}

public enum LineEnding
{
    None,
    Cr,
    Lf,
    CrLf
}
