using DiSerial.Core.Models;
using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Diagnostics;

// Log port discovery events.
public static partial class DeviceWatcherLog
{
    // Log a changed port list.
    [LoggerMessage(EventId = 1301, Level = LogLevel.Debug,
        Message = "Ports changed: +{AddedCount} -{RemovedCount}, total {Total}; added=[{Added}] removed=[{Removed}] current=[{Current}]")]
    public static partial void PortsChanged(
        ILogger logger, int addedCount, int removedCount, int total,
        string added, string removed, string current);

    // Log an unchanged port list.
    [LoggerMessage(EventId = 1302, Level = LogLevel.Trace,
        Message = "Polled {Total} ports, no change")]
    public static partial void PolledNoChange(ILogger logger, int total);

    // Log device watcher startup.
    [LoggerMessage(EventId = 1303, Level = LogLevel.Debug,
        Message = "Port watch started, baseline {Total} ports: [{Baseline}]")]
    public static partial void Started(ILogger logger, int total, string baseline);

    // Log device watcher shutdown.
    [LoggerMessage(EventId = 1304, Level = LogLevel.Debug,
        Message = "Port watch stopped")]
    public static partial void Stopped(ILogger logger);

    // Log a device watcher failure.
    [LoggerMessage(EventId = 1305, Level = LogLevel.Error,
        Message = "Port watch loop faulted and stopped; hot-plug detection is no longer running ({ExceptionType})")]
    public static partial void Faulted(ILogger logger, Exception exception, string exceptionType);

    // Log a port handler failure.
    [LoggerMessage(EventId = 1306, Level = LogLevel.Error,
        Message = "A PortsChanged subscriber threw ({ExceptionType}); that notification was lost "
            + "but hot-plug detection is still running")]
    public static partial void SubscriberFailed(
        ILogger logger, Exception exception, string exceptionType);

    // Log a port baseline failure.
    [LoggerMessage(EventId = 1307, Level = LogLevel.Error,
        Message = "Baseline port enumeration failed ({ExceptionType}); the watch is starting "
            + "without one, and the first poll will adopt whatever it finds")]
    public static partial void BaselineFailed(
        ILogger logger, Exception exception, string exceptionType);

    // Log a port baseline recovery.
    [LoggerMessage(EventId = 1308, Level = LogLevel.Debug,
        Message = "Baseline recovered on the first poll: {Total} ports [{Baseline}]; "
            + "no change is reported for this round")]
    public static partial void BaselineRecovered(ILogger logger, int total, string baseline);

    // Format the discovered port list.
    public static string Describe(IReadOnlyList<SerialPortInfo> ports) =>
        ports.Count == 0 ? "-" : string.Join(", ", ports.Select(p => p.PortName));
}
