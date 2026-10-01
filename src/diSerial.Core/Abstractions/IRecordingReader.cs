using DiSerial.Core.Models;

namespace DiSerial.Core.Abstractions;

public interface IRecordingReader
{
    // Stream recorded batch frames.
    IAsyncEnumerable<SerialFrame> ReadBatchAsync(long batchId, CancellationToken cancellationToken = default);

    // Read a recorded batch summary.
    Task<RecordingBatchSummary?> GetBatchAsync(long batchId, CancellationToken cancellationToken = default);
}

// Summarize a recorded batch.
public sealed record RecordingBatchSummary(
    long Id,
    string PortLabel,
    string SettingsLabel,
    DateTimeOffset StartedAt,
    string? AliasA = null,
    string? AliasB = null);
