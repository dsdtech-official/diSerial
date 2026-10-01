using DiSerial.Core.Models;

namespace DiSerial.App.ViewModels.Dialogs;

// Describe a new session request.
public abstract record NewSessionResult
{
    public required SerialPortSettings Settings { get; init; }


}

// Describe a terminal request.
public sealed record TerminalSessionRequest : NewSessionResult
{
    public required SerialPortInfo Port { get; init; }
}

// Describe a monitoring request.
public sealed record MonitorSessionRequest : NewSessionResult
{
    public required SerialChannelPair Pair { get; init; }
}
