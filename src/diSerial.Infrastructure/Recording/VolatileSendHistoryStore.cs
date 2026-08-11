using DiSerial.Core.Abstractions;

namespace DiSerial.Infrastructure.Recording;

/// <summary>
/// In-memory <see cref="IVolatileSendHistoryStore"/> — what monitor sessions get.
///
/// <para><b>Why this file exists at all</b> is on the interface, not here: it is a safety
/// decision about not writing bus injections to disk, not a storage optimisation.</para>
///
/// <para>⭐ <b>It implements the full contract, it is not a no-op.</b> Ordering,
/// (text, mode) identity, use counts and the cap all behave exactly as the durable store
/// does — the only difference a user can observe is that restarting diSerial empties it. A
/// version that silently dropped writes would make "history works in a monitor session" a
/// false promise, and would pass any test that only checks nothing was persisted.</para>
///
/// <para>⚠️ <b>It lives in Infrastructure even though it touches no I/O</b>, next to the SQLite
/// implementation: the two are alternatives for one contract, and splitting them across layers
/// would make "which one does a session get" harder to see than it already is.</para>
/// </summary>
public sealed class VolatileSendHistoryStore : IVolatileSendHistoryStore
{
    /// <summary>
    /// Same cap as the durable table, so the two implementations cannot drift into behaving
    /// differently for a reason no one intended.
    /// </summary>
    private const int Capacity = 100;

    /// <summary>
    /// ⚠️ Guards <see cref="_entries"/>. Sends normally arrive on the UI thread, but the store
    /// is a singleton shared by every monitor session in the run, and nothing in the contract
    /// promises a single caller. A lock around a list this small costs nothing measurable and
    /// removes the question entirely.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>Newest first — the order <see cref="Load"/> promises.</summary>
    private readonly List<SendHistoryEntry> _entries = [];

    public IReadOnlyList<SendHistoryEntry> Load(int limit)
    {
        if (limit <= 0) return [];

        lock (_gate)
        {
            return _entries.Take(limit).ToList();
        }
    }

    public void Record(string text, bool isHexMode, string? lineEnding)
    {
        lock (_gate)
        {
            // Identity is (text, mode): the same characters mean different bytes in the two
            // modes, so collapsing them would let the newer one rewrite what the older meant.
            var index = _entries.FindIndex(e => e.IsHexMode == isHexMode && e.Text == text);
            var useCount = 1;

            if (index >= 0)
            {
                useCount = _entries[index].UseCount + 1;
                _entries.RemoveAt(index);
            }

            _entries.Insert(0, new SendHistoryEntry(
                text, isHexMode, lineEnding, useCount, DateTimeOffset.Now));

            // Least recently used falls off the end, matching the durable store's eviction.
            while (_entries.Count > Capacity) _entries.RemoveAt(_entries.Count - 1);
        }
    }

    public void Delete(string text, bool isHexMode)
    {
        lock (_gate)
        {
            _entries.RemoveAll(e => e.IsHexMode == isHexMode && e.Text == text);
        }
    }

    public int Count()
    {
        lock (_gate)
        {
            return _entries.Count;
        }
    }

    /// <summary>
    /// ⚠️ <b>Cannot fail, and that is not an exemption from the contract.</b> The interface
    /// says <c>Clear</c> must not swallow storage failures, because reporting success while
    /// rows survive on disk would be the tool lying. Here there is no disk and no failure mode
    /// to swallow — the guarantee holds trivially rather than being waived.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }
}
