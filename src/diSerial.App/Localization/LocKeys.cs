namespace DiSerial.App.Localization;

/// <summary>
/// 资源键常量。
///
/// ViewModel 一律通过本类引用资源键，不写字符串字面量 —— 改键名时有编译期检查。
/// XAML 侧受框架限制只能写字面量键，故 <see cref="LocalizationService"/> 对
/// 缺失的键返回 <c>!Key!</c>，使拼写错误在界面上立刻可见。
/// </summary>
public static class LocKeys
{
    // ---- 通用 ----
    public const string CommonCancel = "Common.Cancel";
    public const string CommonOk = "Common.Ok";

    // ---- 应用 / 主窗口 ----
    public const string AppName = "App.Name";
    public const string AppTitleWithSession = "App.TitleWithSession";
    public const string AppTitleDeveloperMode = "App.TitleDeveloperMode";
    // ⚠️ StatusReady 于 2026-07-31 随 MainWindowViewModel.StatusText 一并删除（P2-1）。
    // 资源键 Status.Ready 本身仍在用 —— MainWindow.axaml:113 的 loc:Translate 直接引它，
    // 而本类只列 ViewModel 需要的键（40/137），没有 ViewModel 消费者就不该留常量。

    /// <summary>语言菜单标题，含当前语言名占位符。各语言自行决定标点写法。</summary>
    public const string MenuLanguage = "Menu.Language";

    // ---- 会话标题与状态 ----
    public const string SessionTerminalTitle = "Session.Terminal.Title";
    public const string SessionMonitorTitle = "Session.Monitor.Title";
    public const string SessionTerminalStatus = "Session.Terminal.Status";
    public const string SessionMonitorStatus = "Session.Monitor.Status";

    public const string StateConnected = "State.Connected";
    public const string StateConnecting = "State.Connecting";
    public const string StateFaulted = "State.Faulted";
    public const string StateDisconnected = "State.Disconnected";
    public const string StateMonitoring = "State.Monitoring";

    // P1-40: appended to the connection state while the display is paused.
    public const string StateDisplayPaused = "State.DisplayPaused";

    // P2-54: appended to the status bar while recording, same slot and same rule as
    // State.DisplayPaused -- appended, never a replacement.
    public const string StateRecording = "State.Recording";
    public const string StateNotStarted = "State.NotStarted";

    // ---- Toolbar labels that toggle at runtime (P1-40) ----
    // Static toolbar labels stay in XAML via {loc:Translate ...}; these two are here
    // because the pause button swaps between them and the swap happens in the ViewModel.
    public const string ToolbarPause = "Toolbar.Pause";
    public const string ToolbarResume = "Toolbar.Resume";

    // P2-54: the record button has to say which way it goes, exactly like Pause/Resume.
    // Until 2026-08-04 it was a constant, so "recording" and "not recording" looked identical.
    // ⚠️ Toolbar.Record had no constant until then either -- the view referenced the key
    // literally, which is exactly what a label that never changes lets you get away with.
    public const string ToolbarRecord = "Toolbar.Record";
    public const string ToolbarStopRecord = "Toolbar.StopRecord";

    // P2-99: the Session menu invokes the same TogglePauseCommand as the toolbar button, so its
    // label has to swap too. It gets its own pair rather than reusing Toolbar.Pause/Resume
    // because the menu says "Pause receiving" where the toolbar says "Pause"; reusing the
    // toolbar's pair would have fixed the lie and shortened the wording in the same stroke.
    // ⚠️ Menu.Session.Pause existed already and was referenced only from XAML -- that is exactly
    // what a label that never changes lets you get away with, same as Toolbar.Record above.
    public const string MenuSessionPause = "Menu.Session.Pause";
    public const string MenuSessionResume = "Menu.Session.Resume";


    // ---- 平台支持状态 ----
    public const string PlatformMacOsNotImplemented = "Platform.MacOsNotImplemented";
    public const string PlatformUnknown = "Platform.Unknown";

    // ---- 清空发送历史（T-03a，2026-08-02）----
    public const string SendHistoryClear = "Send.HistoryClear";
    public const string SendHistoryClearTitle = "Send.HistoryClear.Title";
    public const string SendHistoryClearMessage = "Send.HistoryClear.Message";
    public const string SendHistoryClearConfirm = "Send.HistoryClear.Confirm";
    public const string SendHistoryClearFailed = "Send.HistoryClear.Failed";

    // ---- 记录中关闭会话的确认（P2-61，2026-08-04 用户定）----
    public const string CloseWhileRecordingTitle = "Session.CloseWhileRecording.Title";
    public const string CloseWhileRecordingMessage = "Session.CloseWhileRecording.Message";
    public const string CloseWhileRecordingConfirm = "Session.CloseWhileRecording.Confirm";

    // ---- 总线注入警告（M-09）----
    public const string InjectionTitle = "Injection.Title";
    public const string InjectionMessage = "Injection.Message";
    public const string InjectionConfirm = "Injection.Confirm";

    // ---- 关于 ----
    public const string AboutTitle = "About.Title";
    public const string AboutMessage = "About.Message";

    // ---- 通道描述：端口名 + 用户填的别名 ----
    // ⚠️ 原先这里是 ChannelDefaultAliasA/B（"PLC" / "HMI"），2026-08-01 随 P0-9 删除：
    // 别名不再在新建会话对话框里预填，默认就等于端口名。
    public const string ChannelPortWithAlias = "Channel.PortWithAlias";

    // ⚠️ 一个键取代原先按 A/B 分开的两个（「通道 A 别名」）——
    // 占位文字现在带端口名（「COM6 的别名」），A/B 两个字母整体退场。
    public const string ChannelAliasPlaceholder = "Channel.AliasPlaceholder";

    // ---- 错误 ----
    // ⚠️ Error.PortRequired / Error.PairRequired 于 2026-08-04 随 P2-52 删除 ——
    // 它们是 SessionViewModelFactory 那两处 `?? throw` 的文案，而请求记录改成
    // 每种类型一条派生记录之后，缺端口 / 缺端口对已经**编译不过**，运行期到不了那两句。
    public const string ErrorUnsupportedSessionKind = "Error.UnsupportedSessionKind";

    // ---- 可恢复异常的界面呈现（P0-2 / 01-spec 4.7）----
    // 标题 + 按 SerialErrorKind 分类的原因文本。原始异常消息只进日志，不进界面。
    public const string ErrorNoticeTitle = "ErrorNotice.Title";
    public const string ErrorNoticeDismiss = "ErrorNotice.Dismiss";

    public const string ErrorKindUnknown = "ErrorKind.Unknown";
    public const string ErrorKindPortNotFound = "ErrorKind.PortNotFound";
    public const string ErrorKindAccessDenied = "ErrorKind.AccessDenied";
    public const string ErrorKindDeviceRemoved = "ErrorKind.DeviceRemoved";
    public const string ErrorKindTimeout = "ErrorKind.Timeout";
    public const string ErrorKindNotConnected = "ErrorKind.NotConnected";
    public const string ErrorKindInvalidInput = "ErrorKind.InvalidInput";

    // T-06: the timed-send interval was below the floor. Deliberately not folded into
    // ErrorKind.InvalidInput -- that message talks about HEX digits and would state a
    // wrong reason here.
    public const string ErrorKindIntervalTooSmall = "ErrorKind.IntervalTooSmall";

    // P2-50 ②: "start timed send" was pressed with an empty box. Separate from
    // ErrorKind.InvalidInput for the same reason as above -- there is nothing to parse here.
    public const string ErrorKindNothingToSend = "ErrorKind.NothingToSend";

    // P2-53: path 6 (writing to the recording database failed). It was the only path without
    // its own kind, so it fell back to the generic "see the log" text.
    public const string ErrorKindRecordingFailed = "ErrorKind.RecordingFailed";
    public const string ErrorKindLineError = "ErrorKind.LineError";

    // ---- 新建会话时打不开端口，提示显示在对话框内（01-spec 4.7）----
    //
    // ⚠️ 与上面那组（顶部提示条）是两种呈现，不重叠：
    // 本组服务「会话还没建起来，对话框还开着」，那组服务「已有会话出了问题」。
    // 监听那条要说清是哪一路 —— 光说「打不开」用户不知道该改哪个下拉框。
    public const string DialogOpenFailedTerminal = "Dialog.NewSession.OpenFailed.Terminal";
    public const string DialogOpenFailedMonitor = "Dialog.NewSession.OpenFailed.Monitor";

    // ---- Session type cards (2026-08-03) ----
    //
    // These used to be literal keys inside NewSessionDialog.axaml. The card row is now driven
    // by SessionTypeCatalog, so the wording is resolved in C# and has to come from here --
    // SourceConventionTests refuses user-visible text written into source.
    public const string DialogNewSessionTerminal = "Dialog.NewSession.Terminal";
    public const string DialogNewSessionTerminalDesc = "Dialog.NewSession.TerminalDesc";
    public const string DialogNewSessionMonitor = "Dialog.NewSession.Monitor";
    public const string DialogNewSessionMonitorDesc = "Dialog.NewSession.MonitorDesc";
    public const string DialogNewSessionMonitorRequires = "Dialog.NewSession.MonitorRequires";

    // ---- T-07 control signal panel (2026-08-06, spec 4.15) ----
    //
    // ⛔ Only the three STATE WORDS are here. The row labels and the panel header are resolved
    // in XAML with loc:Translate, so they need no constant — but these three are picked by C#
    // (SignalPanelViewModel maps a ControlLineState to one of them), and SourceConventionTests
    // refuses user-visible text written into source.
    //
    // ⚠️ They exist because spec 4.15 promise 5 forbids conveying the three states by colour
    // alone. Deleting them to "simplify" the panel would break that promise silently — the dots
    // would still render, just without anything a colour-blind reader can use.
    public const string SignalStateHigh = "Signal.StateHigh";
    public const string SignalStateLow = "Signal.StateLow";
    public const string SignalStateUnknown = "Signal.StateUnknown";
}
