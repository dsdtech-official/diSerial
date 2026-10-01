namespace DiSerial.Core.Models;

// Classify serial port failures.
public static class SerialErrorClassifier
{
    // Classify serial port exceptions.
    public static SerialErrorKind Classify(Exception? exception) => exception switch
    {
        null => SerialErrorKind.Unknown,

        UnauthorizedAccessException => SerialErrorKind.AccessDenied,

        ArgumentException => SerialErrorKind.PortNotFound,

        TimeoutException => SerialErrorKind.Timeout,

        ObjectDisposedException => SerialErrorKind.DeviceRemoved,

        FileNotFoundException => SerialErrorKind.PortNotFound,

        IOException => SerialErrorKind.DeviceRemoved,
        InvalidOperationException => SerialErrorKind.DeviceRemoved,

        _ => SerialErrorKind.Unknown
    };

    // Classify a stopped read loop.
    public static SerialErrorKind? ClassifyReadLoopStop(Exception exception, bool cancellationRequested)
    {
        if (cancellationRequested) return null;

        return exception is OperationCanceledException
            ? SerialErrorKind.DeviceRemoved
            : Classify(exception);
    }
}
