using DiSerial.Core.Models;

namespace DiSerial.Core.Abstractions;

public interface ISerialPort : IAsyncDisposable
{
    string PortName { get; }

    SerialPortSettings Settings { get; }

    bool IsOpen { get; }

    event EventHandler<SerialChunkReceivedEventArgs>? DataReceived;

    event EventHandler<SerialErrorEventArgs>? ErrorReceived;

    // Open the serial port.
    Task OpenAsync(CancellationToken cancellationToken = default);

    // Close the serial port.
    Task CloseAsync(CancellationToken cancellationToken = default);

    // Write the supplied payload.
    Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    // Read serial input line states.
    SerialControlLines ReadControlLines();

    // Set a serial output line.
    Task SetOutputLineAsync(
        SerialOutputLine line, bool asserted, CancellationToken cancellationToken = default);
}

// Describe a received byte chunk.
public sealed class SerialChunkReceivedEventArgs(
    ReadOnlyMemory<byte> data, DateTimeOffset timestamp) : EventArgs
{
    public ReadOnlyMemory<byte> Data { get; } = data;

    public DateTimeOffset Timestamp { get; } = timestamp;
}

// Describe a failed port opening.
public sealed class SerialPortOpenException(
    ChannelId channel, string portName, Exception innerException)
    : Exception($"Failed to open {portName} (channel {channel}).", innerException)
{
    public ChannelId Channel { get; } = channel;

    public string PortName { get; } = portName;
}

// Carry a serial line error.
public sealed class SerialErrorEventArgs(
    FrameFlags error,
    string message,
    bool isFatal = false,
    SerialErrorKind kind = SerialErrorKind.Unknown)
    : EventArgs
{
    public FrameFlags Error { get; } = error;

    public string Message { get; } = message;

    public SerialErrorKind Kind { get; } = kind;

    public bool IsFatal { get; } = isFatal;
}
