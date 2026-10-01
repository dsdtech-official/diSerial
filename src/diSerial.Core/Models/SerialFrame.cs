namespace DiSerial.Core.Models;

// Represent a captured frame.
public sealed class SerialFrame
{
    public required long Sequence { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required TimeSpan Elapsed { get; init; }

    public TimeSpan? Delta { get; init; }

    public ChannelId Channel { get; init; } = ChannelId.None;

    public required FrameDirection Direction { get; init; }

    public required ReadOnlyMemory<byte> Data { get; init; }

    public FrameFlags Flags { get; init; } = FrameFlags.None;

    public bool IsAutoAnswer { get; init; }

    public LocalizableText? DecodedSummary { get; init; }

    public int Length => Data.Length;
}
