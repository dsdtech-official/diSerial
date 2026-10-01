namespace DiSerial.Core.Abstractions;

// Describe a saved send shortcut.
public sealed record Shortcut(long Id, string Name, byte[] Payload, long Ordinal);

// Define saved command limits.
public static class ShortcutLimits
{
    public const int MaxCount = 12;

    public const int MaxNameLength = 15;
}

public enum ShortcutAddResult
{
    Added,

    Full,

    DuplicateName,

    InvalidName,

    EmptyPayload,

    StorageFailed,
}

public interface IShortcutStore
{
    // Load saved send shortcuts.
    IReadOnlyList<Shortcut> Load();

    // Save a send shortcut.
    ShortcutAddResult Add(string name, byte[] payload);

    // Rename a saved send shortcut.
    bool Rename(long id, string name);

    // Delete a saved send shortcut.
    void Delete(long id);

    // Move a shortcut to the front.
    void MoveToFront(long id);

    // Move a shortcut up one place.
    void MoveForwardOne(long id);
}
