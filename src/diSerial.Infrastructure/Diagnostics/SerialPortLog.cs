using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Diagnostics;

// Log serial port events.
public static partial class SerialPortLog
{

    // Log a serial port open request.
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "Opening port {PortName} at {BaudRate} {DataBits}{Parity}{StopBits} flow={FlowControl}")]
    public static partial void Opening(
        ILogger logger, string portName, int baudRate,
        int dataBits, string parity, string stopBits, string flowControl);

    // Log a successful port open.
    [LoggerMessage(EventId = 1002, Level = LogLevel.Information,
        Message = "Port {PortName} opened in {ElapsedMs:F1} ms")]
    public static partial void Opened(ILogger logger, string portName, double elapsedMs);

    // Log a serial port open failure.
    [LoggerMessage(EventId = 1003, Level = LogLevel.Error,
        Message = "Failed to open port {PortName} after {ElapsedMs:F1} ms: {ExceptionType}")]
    public static partial void OpenFailed(
        ILogger logger, Exception exception, string portName, double elapsedMs, string exceptionType);

    // Log a serial port close request.
    [LoggerMessage(EventId = 1004, Level = LogLevel.Information,
        Message = "Closing port {PortName}")]
    public static partial void Closing(ILogger logger, string portName);

    // Log a successful port close.
    [LoggerMessage(EventId = 1005, Level = LogLevel.Information,
        Message = "Port {PortName} closed in {ElapsedMs:F1} ms, readLoopTimedOut={ReadLoopTimedOut}")]
    public static partial void Closed(
        ILogger logger, string portName, double elapsedMs, bool readLoopTimedOut);

    // Log a serial port close failure.
    [LoggerMessage(EventId = 1007, Level = LogLevel.Warning,
        Message = "Closing port {PortName} failed ({ExceptionType}); the device was likely unplugged. Cleanup continues.")]
    public static partial void CloseFailed(
        ILogger logger, Exception exception, string portName, string exceptionType);


    // Log a control line failure.
    [LoggerMessage(EventId = 1008, Level = LogLevel.Warning,
        Message = "Setting control line {Line} on {PortName} failed ({ExceptionType}); the requested level is remembered and will be applied on the next open.")]
    public static partial void ControlLineFailed(
        ILogger logger, Exception exception, string portName, string line, string exceptionType);

    // Log a control line read failure.
    [LoggerMessage(EventId = 1009, Level = LogLevel.Debug,
        Message = "Reading control lines on {PortName} began failing ({ExceptionType}); the indicators now report Unknown. Logged once per transition, not per poll.")]
    public static partial void ControlLineReadFailed(
        ILogger logger, Exception exception, string portName, string exceptionType);

    // Log a missing serial device.
    [LoggerMessage(EventId = 1010, Level = LogLevel.Information,
        Message = "Port {PortName} does not exist; reporting the {ExceptionType} from open as \"port not found\" rather than \"access denied\".")]
    public static partial void AbsentDeviceNodeOnOpen(
        ILogger logger, string portName, string exceptionType);


    // Log received chunk metadata.
    [LoggerMessage(EventId = 1101, Level = LogLevel.Debug,
        Message = "Read {Count} bytes from {PortName}, gap={GapMs:F3} ms at {Timestamp:O}")]
    public static partial void ReadChunk(
        ILogger logger, string portName, int count, double gapMs, DateTimeOffset timestamp);

    // Log received payload bytes.
    [LoggerMessage(EventId = 1102, Level = LogLevel.Trace,
        Message = "Payload from {PortName}: {Hex}")]
    public static partial void ReadPayload(ILogger logger, string portName, string hex);

    // Log a serial read loop failure.
    [LoggerMessage(EventId = 1103, Level = LogLevel.Error,
        Message = "Read loop on {PortName} faulted after {Reads} reads / {Bytes} bytes: {ExceptionType}")]
    public static partial void ReadLoopFaulted(
        ILogger logger, Exception exception, string portName,
        long reads, long bytes, string exceptionType);

    // Log serial read loop shutdown.
    [LoggerMessage(EventId = 1104, Level = LogLevel.Debug,
        Message = "Read loop on {PortName} exited normally after {Reads} reads / {Bytes} bytes")]
    public static partial void ReadLoopExited(
        ILogger logger, string portName, long reads, long bytes);

    // Log a read loop stop timeout.
    [LoggerMessage(EventId = 1105, Level = LogLevel.Debug,
        Message = "Read loop on {PortName} did not exit within the stop timeout; Close will break it")]
    public static partial void ReadLoopStopTimedOut(ILogger logger, Exception exception, string portName);

    // Log a failed read loop stop.
    [LoggerMessage(EventId = 1107, Level = LogLevel.Error,
        Message = "Read loop on {PortName} faulted while closing ({ExceptionType}); "
                  + "the close completed anyway")]
    public static partial void ReadLoopStopFaulted(
        ILogger logger, Exception exception, string portName, string exceptionType);

    // Log a serial line error.
    [LoggerMessage(EventId = 1106, Level = LogLevel.Debug,
        Message = "Serial line error on {PortName}: {ErrorType} (firstOfItsKind={FirstOfItsKind})")]
    public static partial void LineError(
        ILogger logger, string portName, string errorType, bool firstOfItsKind);


    // Log a completed serial write.
    [LoggerMessage(EventId = 1201, Level = LogLevel.Debug,
        Message = "Wrote {Count} bytes to {PortName}")]
    public static partial void Wrote(ILogger logger, string portName, int count);

    // Log a serial write failure.
    [LoggerMessage(EventId = 1202, Level = LogLevel.Error,
        Message = "Write of {Count} bytes to {PortName} failed: {ExceptionType}")]
    public static partial void WriteFailed(
        ILogger logger, Exception exception, string portName, int count, string exceptionType);

    // Format bytes as hex text.
    public static string ToHex(ReadOnlySpan<byte> data, int maxBytes = 64)
    {
        if (data.IsEmpty) return string.Empty;

        var shown = Math.Min(data.Length, maxBytes);
        var builder = new System.Text.StringBuilder(shown * 3 + 16);

        for (var i = 0; i < shown; i++)
        {
            if (i > 0) builder.Append(' ');
            builder.Append(data[i].ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }

        if (data.Length > shown)
        {
            builder.Append(System.Globalization.CultureInfo.InvariantCulture, $" ... ({data.Length} bytes total)");
        }

        return builder.ToString();
    }
}
