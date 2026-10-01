using DiSerial.Core.Models;

namespace DiSerial.Core.Abstractions;

public interface IRecordingCatalog
{
    // List matching recorded batches.
    Task<IReadOnlyList<RecordingBatchListItem>> ListBatchesAsync(
        RecordingBatchFilter filter, CancellationToken cancellationToken = default);

    // Delete the requested batches.
    Task<int> DeleteBatchesAsync(
        IReadOnlyCollection<long> batchIds, CancellationToken cancellationToken = default);

    // Read the database size.
    Task<long> GetDatabaseSizeAsync(CancellationToken cancellationToken = default);

    // Compact the recording database.
    Task VacuumAsync(CancellationToken cancellationToken = default);
}

// Report a busy recording store.
public sealed class RecordingDatabaseBusyException(Exception inner)
    : Exception("The recording database is in use by something else right now.", inner);

// Describe a recorded batch.
public sealed record RecordingBatchListItem(
    long Id,
    SessionKind SessionKind,
    string PortA,
    string? PortB,
    string? AliasA,
    string? AliasB,
    SerialPortSettings Settings,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    long FrameCount);

// Define recording query limits.
public sealed record RecordingBatchFilter
{
    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }
}
