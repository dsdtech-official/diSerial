using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using DiSerial.Infrastructure.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiSerial.Infrastructure.Formatting;

public enum FrameCloseReason
{
    IdleGap,

    IdleTimer,

    MaxLength,

    Forced,
}

// Split frames at idle gaps.
public sealed class IdleGapFrameSplitter : IFrameSplitter
{
    public const int MaxFrameLength = 4096;

    private readonly List<byte> _buffer = [];

    private DateTimeOffset _pendingStartedAt;

    private DateTimeOffset _pendingEndedAt;

    public ILogger Logger { get; init; } = NullLogger.Instance;

    public TimeSpan IdleGap { get; init; }

    public bool HasPending => _buffer.Count > 0;

    // Buffer incoming frame bytes.
    public IReadOnlyList<FramedData> Append(ReadOnlyMemory<byte> data, DateTimeOffset timestamp)
    {
        if (data.IsEmpty) return [];

        List<FramedData>? completed = null;

        if (_buffer.Count > 0 && timestamp - _pendingEndedAt >= IdleGap)
        {
            (completed ??= []).Add(TakeBuffered(FrameCloseReason.IdleGap));
        }

        if (_buffer.Count == 0) _pendingStartedAt = timestamp;
        _buffer.AddRange(data.Span);
        _pendingEndedAt = timestamp;

        if (_buffer.Count >= MaxFrameLength)
        {
            (completed ??= []).Add(TakeBuffered(FrameCloseReason.MaxLength));
        }

        return completed ?? (IReadOnlyList<FramedData>)[];
    }

    // Emit a frame after an idle gap.
    public FramedData? FlushIfIdle(DateTimeOffset now)
    {
        if (_buffer.Count == 0) return null;
        if (now - _pendingEndedAt < IdleGap) return null;

        return TakeBuffered(FrameCloseReason.IdleTimer);
    }

    // Emit the buffered frame.
    public FramedData? Flush()
    {
        if (_buffer.Count == 0) return null;

        return TakeBuffered(FrameCloseReason.Forced);
    }

    // Clear buffered frame state.
    public void Reset()
    {
        _buffer.Clear();
        _pendingStartedAt = default;
        _pendingEndedAt = default;
    }

    // Emit and clear buffered bytes.
    private FramedData TakeBuffered(FrameCloseReason reason)
    {
        var frame = new FramedData(_buffer.ToArray(), _pendingStartedAt, _pendingEndedAt);
        _buffer.Clear();

        SessionLog.FrameCompleted(
            Logger, reason, frame.Length, frame.StartedAt, frame.EndedAt,
            frame.Duration.TotalMilliseconds);

        return frame;
    }
}

// Create idle-gap frame splitters.
public sealed class IdleGapFrameSplitterFactory(
    TimeSpan? idleGapOverride = null,
    ILogger? logger = null,
    Func<TimeSpan?>? userIdleGap = null) : IFrameSplitterFactory
{
    // Create an idle frame splitter.
    public IFrameSplitter Create(SerialPortSettings settings) =>
        new IdleGapFrameSplitter
        {
            IdleGap = idleGapOverride ?? userIdleGap?.Invoke() ?? settings.DefaultIdleGap,

            Logger = logger ?? NullLogger.Instance,
        };
}
