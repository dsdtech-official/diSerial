using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;

namespace DiSerial.App.ViewModels.Sessions;

// Describe a connection failure.
public sealed record SessionOpenFailure(ChannelId Channel, SerialErrorKind Kind, string? Detail)
{
    // Classify a connection failure.
    public static SessionOpenFailure From(Exception exception) => exception is SerialPortOpenException open
        ? new SessionOpenFailure(
            open.Channel,
            SerialErrorClassifier.Classify(open.InnerException ?? open),
            (open.InnerException ?? open).Message)

        : new SessionOpenFailure(
            ChannelId.None,
            SerialErrorClassifier.Classify(exception),
            exception.Message);
}
