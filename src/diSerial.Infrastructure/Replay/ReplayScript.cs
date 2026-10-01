using DiSerial.Core.Models;

namespace DiSerial.Infrastructure.Replay;

// Describe a simulated failure.
public sealed record ReplayFault(
    SerialErrorKind Kind,
    string Message,
    bool IsFatal = false,
    FrameFlags Flags = FrameFlags.None);

// Describe a replay data step.
public sealed record ReplayStep(TimeSpan DelayBefore, byte[] Data, string? Note = null)
{
    public ReplayFault? Fault { get; init; }
}

// Define a serial replay scenario.
public sealed record ReplayScript
{
    public required string Id { get; init; }

    public required string Description { get; init; }

    public required IReadOnlyList<ReplayStep> Steps { get; init; }

    public bool Loop { get; init; } = true;
}

public enum ReplayDelivery
{
    PerFrame,

    Coalesced,

    Fragmented
}

public readonly record struct ReplayChunk(
    TimeSpan DelayBefore, byte[] Data, ReplayFault? Fault = null);

// Configure replay delivery.
public sealed record ReplayOptions
{
    public ReplayDelivery Delivery { get; init; } = ReplayDelivery.PerFrame;

    public TimeSpan CoalesceWindow { get; init; } = TimeSpan.FromMilliseconds(16);

    public int FragmentSize { get; init; } = 3;

    public TimeSpan FragmentGap { get; init; } = TimeSpan.FromMicroseconds(200);
}
