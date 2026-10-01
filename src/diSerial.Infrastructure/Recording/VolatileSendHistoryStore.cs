using DiSerial.Core.Abstractions;

namespace DiSerial.Infrastructure.Recording;

// Keep send history in memory.
public sealed class VolatileSendHistoryStore : IVolatileSendHistoryStore
{
    private const int Capacity = 100;

    private readonly Lock _gate = new();

    private readonly List<SendHistoryEntry> _entries = [];

    // Load in-memory send history.
    public IReadOnlyList<SendHistoryEntry> Load(int limit)
    {
        if (limit <= 0) return [];

        lock (_gate)
        {
            return _entries.Take(limit).ToList();
        }
    }

    // Save a send history entry.
    public void Record(string text, bool isHexMode, string? lineEnding)
    {
        lock (_gate)
        {
            var index = _entries.FindIndex(e => e.IsHexMode == isHexMode && e.Text == text);
            var useCount = 1;

            if (index >= 0)
            {
                useCount = _entries[index].UseCount + 1;
                _entries.RemoveAt(index);
            }

            _entries.Insert(0, new SendHistoryEntry(
                text, isHexMode, lineEnding, useCount, DateTimeOffset.Now));

            while (_entries.Count > Capacity) _entries.RemoveAt(_entries.Count - 1);
        }
    }

    // Delete a send history entry.
    public void Delete(string text, bool isHexMode)
    {
        lock (_gate)
        {
            _entries.RemoveAll(e => e.IsHexMode == isHexMode && e.Text == text);
        }
    }

    // Count in-memory send entries.
    public int Count()
    {
        lock (_gate)
        {
            return _entries.Count;
        }
    }

    // Clear in-memory send history.
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }
}
