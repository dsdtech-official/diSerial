using DiSerial.App.Localization;
using DiSerial.App.ViewModels.Dialogs;
using DiSerial.App.ViewModels.Sessions;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;

namespace DiSerial.App.Services;

/// <summary>
/// 会话 ViewModel 工厂 —— 扩展点。
///
/// 把「会话类型 → 具体 ViewModel」的映射集中到一处。新增会话类型时
/// 只需在此追加一个分支，MainWindowViewModel 与对话框均无需改动。
/// </summary>
public interface ISessionViewModelFactory
{
    SessionViewModel Create(NewSessionResult request);
}

/// <inheritdoc />
public sealed class SessionViewModelFactory(
    ICaptureSessionFactory captureSessionFactory,
    ILocalizationService localization,
    SessionContext sessionContext,
    IVolatileSendHistoryStore volatileSendHistory,
    ISessionRecorderFactory recorderFactory) : ISessionViewModelFactory
{
    /// <summary>
    /// ⭐ <b>Dispatch is on the request's <i>type</i>, not on an enum it carries</b> (P2-52,
    /// 2026-08-04). The payload each branch needs is <c>required</c> on that branch's record, so
    /// there is nothing left to unwrap and nothing that can be missing at runtime -- the two
    /// <c>?? throw</c> checks that used to guard this went away with the nullable fields.
    /// </summary>
    public SessionViewModel Create(NewSessionResult request) => request switch
    {
        TerminalSessionRequest terminal => CreateTerminal(terminal),
        MonitorSessionRequest monitor => CreateMonitor(monitor),

        // Still reachable, and still worth a sentence the user can read: a new record deriving
        // from NewSessionResult without a branch here lands on it.
        _ => throw new NotSupportedException(
            localization.Format(LocKeys.ErrorUnsupportedSessionKind, request.GetType().Name))
    };

    private SessionViewModel CreateTerminal(TerminalSessionRequest request)
    {
        var port = request.Port;

        var capture = captureSessionFactory.CreateTerminal(port.PortName, request.Settings);
        return new TerminalSessionViewModel(
            port, request.Settings, capture, recorderFactory.Create(), sessionContext);
    }

    private SessionViewModel CreateMonitor(MonitorSessionRequest request)
    {
        var pair = request.Pair;

        var capture = captureSessionFactory.CreateMonitor(pair, request.Settings);
        // ⚠️ 别名不再由对话框带过来（2026-08-01，P0-9）——
        // ChannelViewModel 构造时就把它设成端口名，用户进界面后自己改。
        //
        // ⭐ 发送历史换成**不落盘**的那一个（2026-08-03 用户定）——
        // 监听会话发出去的是对客户产线的注入，M-09 约束 4 规定「启用状态绝不持久化」，
        // 而把载荷留在盘上与那一条互相矛盾。理由全文在 IVolatileSendHistoryStore 上。
        //
        // ⛔ **这是全项目唯一决定「哪种会话用哪个 store」的地方**，
        // 别在会话或面板里再判一次 —— SendPanelViewModel 只认接口，它不该知道有两种。
        // `SessionSendHistoryIsolationTests` 守着这一行。
        return new MonitorSessionViewModel(
            pair, request.Settings, capture, recorderFactory.Create(),
            sessionContext with { SendHistory = volatileSendHistory });
    }
}
