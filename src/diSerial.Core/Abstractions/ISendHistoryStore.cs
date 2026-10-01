namespace DiSerial.Core.Abstractions;

// Describe a saved send entry.
public sealed record SendHistoryEntry(
    string Text,
    bool IsHexMode,
    string? LineEnding,
    int UseCount,
    DateTimeOffset LastUsedAt);

public interface ISendHistoryStore
{
    // Load saved send history.
    IReadOnlyList<SendHistoryEntry> Load(int limit);

    // Save a send history entry.
    void Record(string text, bool isHexMode, string? lineEnding);

    // Delete a send history entry.
    void Delete(string text, bool isHexMode);

    // Count saved send entries.
    int Count();

    // Clear saved send history.
    void Clear();
}

public interface IVolatileSendHistoryStore : ISendHistoryStore;
