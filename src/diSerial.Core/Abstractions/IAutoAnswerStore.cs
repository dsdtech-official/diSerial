using DiSerial.Core.Models;

namespace DiSerial.Core.Abstractions;

public enum PayloadFormat
{
    Ascii,
    Hex
}

// Define an automatic reply rule.
public sealed record AutoAnswerRule(
    long Id,
    string Name,
    PayloadFormat TriggerFormat,
    string TriggerText,
    PayloadFormat ResponseFormat,
    string ResponseText,
    LineEnding Terminator,
    long Ordinal);

// Define automatic reply limits.
public static class AutoAnswerLimits
{
    public const int MaxCount = 16;

    public const int MaxPatternBytes = 256;

    public const int MaxNameLength = 15;
}

public enum AutoAnswerSaveResult
{
    Saved,

    Full,

    InvalidName,

    InvalidTrigger,

    InvalidResponse,

    NotFound,
}

public interface IAutoAnswerStore
{
    // Load saved reply rules.
    IReadOnlyList<AutoAnswerRule> Load();

    // Save an automatic reply rule.
    AutoAnswerSaveResult Add(
        string name,
        PayloadFormat triggerFormat, string triggerText,
        PayloadFormat responseFormat, string responseText,
        LineEnding terminator);

    // Update a saved reply rule.
    AutoAnswerSaveResult Update(
        long id,
        string name,
        PayloadFormat triggerFormat, string triggerText,
        PayloadFormat responseFormat, string responseText,
        LineEnding terminator);

    // Delete a saved reply rule.
    void Delete(long id);

    // Move a reply rule to the front.
    void MoveToFront(long id);

    // Move a reply rule up one place.
    void MoveForwardOne(long id);

    // Move a reply rule down one slot.
    void MoveBackOne(long id);
}
