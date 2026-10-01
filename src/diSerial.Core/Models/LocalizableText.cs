namespace DiSerial.Core.Models;

// Describe translatable text.
public sealed record LocalizableText
{
    // Initialize translatable text.
    private LocalizableText() { }

    public string Key { get; private init; } = string.Empty;

    public IReadOnlyList<object?> Args { get; private init; } = [];

    public string? Fallback { get; private init; }

    public bool HasKey => !string.IsNullOrEmpty(Key);

    // Create text from a resource key.
    public static LocalizableText FromKey(string key, params object?[] args) =>
        new() { Key = key, Args = args };

    // Create text from a resource key.
    public static LocalizableText FromKey(string key, string fallback, params object?[] args) =>
        new() { Key = key, Args = args, Fallback = fallback };

    // Create text from a literal.
    public static LocalizableText FromLiteral(string text) =>
        new() { Fallback = text };

    // Return fallback text or its key.
    public override string ToString() =>
        HasKey ? $"LocalizableText(key: {Key})" : $"LocalizableText(literal: {Fallback})";
}
