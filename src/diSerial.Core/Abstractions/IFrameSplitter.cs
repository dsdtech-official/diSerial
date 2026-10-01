using DiSerial.Core.Models;

namespace DiSerial.Core.Abstractions;

public readonly record struct FramedData(
    ReadOnlyMemory<byte> Data,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt)
{
    public TimeSpan Duration => EndedAt - StartedAt;

    public int Length => Data.Length;
}

public interface IFrameSplitter
{
    TimeSpan IdleGap { get; init; }

    bool HasPending { get; }

    // Buffer incoming frame bytes.
    IReadOnlyList<FramedData> Append(ReadOnlyMemory<byte> data, DateTimeOffset timestamp);

    // Emit a frame after an idle gap.
    FramedData? FlushIfIdle(DateTimeOffset now);

    // Emit the buffered frame.
    FramedData? Flush();

    // Clear buffered frame state.
    void Reset();
}

public interface IFrameSplitterFactory
{
    // Create an idle frame splitter.
    IFrameSplitter Create(SerialPortSettings settings);
}
