using DiSerial.App.Localization;
using DiSerial.App.ViewModels.Dialogs;
using DiSerial.App.ViewModels.Sessions;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;

namespace DiSerial.App.Services;

public interface ISessionViewModelFactory
{
    // Create the requested session.
    SessionViewModel Create(NewSessionResult request);
}

// Create session view models.
public sealed class SessionViewModelFactory(
    ICaptureSessionFactory captureSessionFactory,
    ILocalizationService localization,
    SessionContext sessionContext,
    IVolatileSendHistoryStore volatileSendHistory,
    ISessionRecorderFactory recorderFactory) : ISessionViewModelFactory
{
    // Create the requested session.
    public SessionViewModel Create(NewSessionResult request) => request switch
    {
        TerminalSessionRequest terminal => CreateTerminal(terminal),
        MonitorSessionRequest monitor => CreateMonitor(monitor),

        _ => throw new NotSupportedException(
            localization.Format(LocKeys.ErrorUnsupportedSessionKind, request.GetType().Name))
    };

    // Create a terminal session.
    private SessionViewModel CreateTerminal(TerminalSessionRequest request)
    {
        var port = request.Port;

        var capture = captureSessionFactory.CreateTerminal(port.PortName, request.Settings);
        return new TerminalSessionViewModel(
            port, request.Settings, capture, recorderFactory.Create(), sessionContext);
    }

    // Create a monitoring session.
    private SessionViewModel CreateMonitor(MonitorSessionRequest request)
    {
        var pair = request.Pair;

        var capture = captureSessionFactory.CreateMonitor(pair, request.Settings);
        return new MonitorSessionViewModel(
            pair, request.Settings, capture, recorderFactory.Create(),
            sessionContext with { SendHistory = volatileSendHistory });
    }
}
