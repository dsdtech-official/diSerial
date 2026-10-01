using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Diagnostics;

// Log synthetic replay events.
public static partial class ReplayLog
{
    // Log replay loop startup.
    [LoggerMessage(EventId = 1501, Level = LogLevel.Debug,
        Message = "Replay loop on {PortName} started: script={ScriptId}, {Chunks} chunks, loop={Loop}")]
    public static partial void LoopStarted(
        ILogger logger, string portName, string scriptId, int chunks, bool loop);

    // Log replay loop shutdown.
    [LoggerMessage(EventId = 1502, Level = LogLevel.Debug,
        Message = "Replay loop on {PortName} exited ({Reason}) after {Chunks} chunks")]
    public static partial void LoopExited(
        ILogger logger, string portName, string reason, long chunks);

    // Log a replay loop failure.
    [LoggerMessage(EventId = 1503, Level = LogLevel.Error,
        Message = "Replay loop on {PortName} faulted after {Chunks} chunks ({ExceptionType}); "
                  + "replay has stopped and no further data will arrive")]
    public static partial void LoopFaulted(
        ILogger logger, Exception exception, string portName, long chunks, string exceptionType);
}
