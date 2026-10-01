using Microsoft.Extensions.Logging;

namespace DiSerial.App.Services;

public readonly record struct SessionLogEntry(
    long Sequence, DateTimeOffset Timestamp, LogLevel Level, string Message);

// Store recent log events.
public sealed class SessionLogBuffer
{
    public const int DefaultCapacity = 2000;

    public const LogLevel MinimumLevel = LogLevel.Information;

    public const string FrameworkCategoryPrefix = "Avalonia.";

    private readonly object _gate = new();
    private readonly Queue<SessionLogEntry> _entries = new();
    private readonly int _capacity;
    private long _next;
    private int _dropped;

    // Initialize session log storage.
    public SessionLogBuffer(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    public int Capacity => _capacity;

    public int DroppedCount
    {
        get { lock (_gate) return _dropped; }
    }

    // Append a session log entry.
    public void Append(DateTimeOffset timestamp, LogLevel level, string category, string message)
    {
        if (level < MinimumLevel) return;
        if (category.StartsWith(FrameworkCategoryPrefix, StringComparison.Ordinal)) return;

        lock (_gate)
        {
            if (_entries.Count == _capacity)
            {
                _entries.Dequeue();
                _dropped++;
            }

            _entries.Enqueue(new SessionLogEntry(_next++, timestamp, level, message));
        }
    }

    // Copy the buffered log entries.
    public IReadOnlyList<SessionLogEntry> Snapshot()
    {
        lock (_gate) return [.. _entries];
    }

    // Read logs after a sequence.
    public IReadOnlyList<SessionLogEntry> Since(long afterSequence)
    {
        lock (_gate) return [.. _entries.Where(e => e.Sequence > afterSequence)];
    }

    // Clear the session log buffer.
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _dropped = 0;
        }
    }
}
