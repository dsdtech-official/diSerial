using DiSerial.Core.Models;

namespace DiSerial.Core.Abstractions;

public interface ICaptureSession : IAsyncDisposable
{
    SessionKind Kind { get; }

    ConnectionState State { get; }

    DateTimeOffset? StartedAt { get; }

    event EventHandler<FrameCapturedEventArgs>? FrameCaptured;

    event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    event EventHandler<SerialErrorEventArgs>? LineErrorDetected;

    // Start capturing serial traffic.
    Task StartAsync(CancellationToken cancellationToken = default);

    // Stop capturing serial traffic.
    Task StopAsync(CancellationToken cancellationToken = default);

    // Send the requested payload.
    Task SendAsync(ChannelId channel, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
}

public interface IControlLineSession
{
    // Read serial input line states.
    SerialControlLines ReadControlLines();

    bool IsRtsOwnedByFlowControl { get; }

    // Set a serial output line.
    Task SetOutputLineAsync(
        SerialOutputLine line, bool asserted, CancellationToken cancellationToken = default);
}

public interface IAutoAnswerSession
{
    // Send an automatic reply payload.
    Task SendAutoAnswerAsync(
        ChannelId channel, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
}

public interface ICaptureSessionFactory
{
    // Create a terminal session.
    ICaptureSession CreateTerminal(string portName, SerialPortSettings settings);

    // Create a monitoring session.
    ICaptureSession CreateMonitor(SerialChannelPair pair, SerialPortSettings settings);
}

// Carry a captured serial frame.
public sealed class FrameCapturedEventArgs(SerialFrame frame) : EventArgs
{
    public SerialFrame Frame { get; } = frame;
}

// Carry a connection state change.
public sealed class ConnectionStateChangedEventArgs(
    ConnectionState state,
    string? message = null,
    SerialErrorKind errorKind = SerialErrorKind.Unknown) : EventArgs
{
    public ConnectionState State { get; } = state;

    public string? Message { get; } = message;

    public SerialErrorKind ErrorKind { get; } = errorKind;
}
