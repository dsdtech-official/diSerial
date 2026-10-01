using DiSerial.Core.Models;
using DiSerial.Infrastructure.Formatting;
using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Diagnostics;

// Log capture session events.
public static partial class SessionLog
{
    // Log a backwards clock delta.
    [LoggerMessage(EventId = 1401, Level = LogLevel.Debug,
        Message = "Merged timeline out of order: frame {Sequence} on channel {Channel} "
            + "has DeltaMs={DeltaMs:F3} (negative; arrival order differs from timestamp order)")]
    public static partial void NegativeDelta(
        ILogger logger, long sequence, ChannelId channel, double deltaMs);

    // Log a session state transition.
    [LoggerMessage(EventId = 1402, Level = LogLevel.Information,
        Message = "Session state: {From} -> {To} ({Kind} session on {Target})")]
    public static partial void StateTransition(
        ILogger logger, ConnectionState from, ConnectionState to, SessionKind kind, string target);

    // Log a completed serial frame.
    [LoggerMessage(EventId = 1403, Level = LogLevel.Debug,
        Message = "Frame closed by {Reason}: {Length} bytes, "
            + "startedAt={StartedAt:O} endedAt={EndedAt:O} spanMs={SpanMs:F3}")]
    public static partial void FrameCompleted(
        ILogger logger,
        FrameCloseReason reason,
        int length,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        double spanMs);
}
