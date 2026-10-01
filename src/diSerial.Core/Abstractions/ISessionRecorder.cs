using DiSerial.Core.Models;

namespace DiSerial.Core.Abstractions;

public interface ISessionRecorder : IAsyncDisposable
{
    bool IsRecording { get; }

    long? CurrentBatchId { get; }

    long FramesWritten { get; }

    long BytesWritten { get; }

    event EventHandler<RecordingFailedEventArgs>? Failed;

    // Start a recording batch.
    Task<long> StartAsync(RecordingBatchInfo info, CancellationToken cancellationToken = default);

    // Queue a frame for recording.
    Task WriteAsync(SerialFrame frame, CancellationToken cancellationToken = default);

    // Finish the recording batch.
    Task StopAsync(CancellationToken cancellationToken = default);
}

// Describe a recording snapshot.
public sealed record RecordingBatchInfo(
    SessionKind SessionKind,
    string PortA,
    string? PortB,
    string? AliasA,
    string? AliasB,
    SerialPortSettings Settings);

// Carry a recording failure.
public sealed class RecordingFailedEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception;
}

public interface ISessionRecorderFactory
{
    // Create a session recorder.
    ISessionRecorder Create();
}
