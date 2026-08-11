using System.Collections.Concurrent;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiSerial.App.Localization;
using DiSerial.App.Services;
using DiSerial.App.ViewModels.Dialogs;
using DiSerial.App.ViewModels.Panels;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using Microsoft.Extensions.Logging;

namespace DiSerial.App.ViewModels.Sessions;

/// <summary>
/// 会话 ViewModel 基类 —— 本项目最重要的扩展点。
///
/// 架构约定（原则 1，见 docs/01-spec.md 第二节 与 docs/02-architecture.md 第一节）：
/// 用「会话类型」而非「模式开关」区分终端与监听。新增会话类型
/// （V1.1 的 TCP/UDP、后续 SSH）只需派生本类并在 ViewLocator 命名约定下
/// 提供对应 View，主窗口与菜单无需改动。
///
/// 继承 LocalizedViewModelBase：Title / StatusText 等计算属性在语言切换时
/// 需要重新求值，由基类统一发出「所有属性均已变更」通知完成刷新。
///
/// 职责边界：本类只管展示状态与用户交互；所有 I/O 在 ICaptureSession 内。
/// </summary>
public abstract partial class SessionViewModel : LocalizedViewModelBase, IAsyncDisposable
{
    /// <summary>UI 刷新频率。30fps 已远超人眼对滚动文本的分辨能力，再高只是空耗。</summary>
    private static readonly TimeSpan UiRefreshInterval = TimeSpan.FromMilliseconds(33);

    private readonly ICaptureSession _capture;
    private readonly ISessionRecorder _recorder;

    /// <summary>
    /// 会话侧的诊断日志。
    ///
    /// ⚠️ 补这个 logger 之前，App 层的几处 <c>catch</c> <b>一条日志都不记</b> ——
    /// 之所以还有记录，全靠 Infrastructure 顺手记了 <c>OpenFailed</c> / <c>WriteFailed</c>。
    /// 而「输入格式错」「未连接就发送」两条路径根本不经过 Infrastructure，此前是彻底无声。
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>采集线程与 UI 线程之间的缓冲。采集侧只入队，绝不接触 UI。</summary>
    private readonly ConcurrentQueue<(SerialFrame Frame, string? Alias)> _pending = new();

    /// <summary>
    /// Batches frame appends onto the UI thread.
    ///
    /// <para>⚠️ This goes through <see cref="IPeriodicTimer"/> rather than holding a
    /// <c>DispatcherTimer</c> directly (P2-31). The App test project runs without an Avalonia
    /// runtime, so a raw dispatcher timer never ticks there -- and a pump that never ticks
    /// silently turns "frame arrived, nothing was displayed" into a passing test. That is the
    /// <c>DrainPending</c> false green recorded in docs/03-conventions.md 0.6 (4); it is the
    /// same seam the timed send uses.</para>
    /// </summary>
    private readonly IPeriodicTimer _uiPump;

    protected SessionViewModel(
        ICaptureSession capture,
        ISessionRecorder recorder,
        SessionContext context)
        : base(context.Localization)
    {
        _capture = capture;
        _recorder = recorder;
        Context = context;

        // 类别用 "Session" 与既有的 "Serial.*" / "Diagnostics.*" 保持同一风格，
        // 让区域成为可过滤维度。
        _logger = context.LoggerFactory.CreateLogger("Session");

        LogPanel = new LogPanelViewModel(context.Formatter, context.EnumChoices);
        SendPanel = new SendPanelViewModel(
            context.EnumChoices, context.Timers, context.SendHistory, _logger);

        // T-07 (spec 4.15). Capability detection, not a session-kind check: see SignalPanel.
        SignalPanel = capture is IControlLineSession controlLines
            ? new SignalPanelViewModel(controlLines, context.Timers, context.Localization)
            : null;
        Subscribe<SendRequestedEventArgs>(OnSendRequested,
            h => SendPanel.SendRequested += h, h => SendPanel.SendRequested -= h);
        Subscribe(OnInputRejected,
            h => SendPanel.InputRejected += h, h => SendPanel.InputRejected -= h);
        Subscribe(OnPayloadMissing,
            h => SendPanel.PayloadMissing += h, h => SendPanel.PayloadMissing -= h);
        // ⛔ IntervalRejected had NO subscriber until 2026-08-04 (P2-60). The panel raised it,
        // TimedSendTests asserted it was raised, ErrorKind.IntervalTooSmall had wording in both
        // languages -- and none of it ever reached a screen. "The event is raised" is not the
        // same claim as "the user is told".
        Subscribe(OnIntervalRejected,
            h => SendPanel.IntervalRejected += h, h => SendPanel.IntervalRejected -= h);
        Subscribe(OnClearHistoryRequested,
            h => SendPanel.ClearHistoryRequested += h, h => SendPanel.ClearHistoryRequested -= h);

        Subscribe(OnPreferenceChanged,
            h => LogPanel.PropertyChanged += h, h => LogPanel.PropertyChanged -= h);
        Subscribe(OnPreferenceChanged,
            h => SendPanel.PropertyChanged += h, h => SendPanel.PropertyChanged -= h);

        Subscribe<FrameCapturedEventArgs>(OnFrameCaptured,
            h => _capture.FrameCaptured += h, h => _capture.FrameCaptured -= h);
        Subscribe<ConnectionStateChangedEventArgs>(OnCaptureStateChanged,
            h => _capture.StateChanged += h, h => _capture.StateChanged -= h);
        Subscribe<RecordingFailedEventArgs>(OnRecordingFailed,
            h => _recorder.Failed += h, h => _recorder.Failed -= h);
        Subscribe<SerialErrorEventArgs>(OnLineErrorDetected,
            h => _capture.LineErrorDetected += h, h => _capture.LineErrorDetected -= h);

        // 定时批量刷新，而不是逐帧 Dispatcher.Post。
        // 115200 波特率下每秒可达数百帧，逐帧派发会压垮 Dispatcher 队列。
        _uiPump = context.Timers.Create(
            UiRefreshInterval, DrainPending, TimerPriority.Background);
        _uiPump.Start();
    }

    // ===================== event subscriptions (P2-44) =====================

    private readonly List<Action> _unsubscribe = [];

    /// <summary>
    /// Subscribe to an event and register the matching unsubscribe <b>in the same act</b>.
    /// <see cref="DisposeAsync"/> runs the whole list; nothing has to remember anything.
    ///
    /// <para><b>Why the shape is this and not a "-=" line further down the file</b> (P2-44,
    /// user decision 2026-08-02): "every += needs a matching -=" was pure discipline, and
    /// discipline had already failed once — <c>MonitorSessionViewModel.EnableSendRequested</c>
    /// was subscribed and never released, and <b>deleting an unsubscribe left all 481 tests
    /// green</b>. That is exactly the kind of convention 03-conventions section 1 says has to
    /// stop depending on memory.</para>
    ///
    /// <para>⭐ <b>What it buys:</b> forgetting now means <i>not subscribing</i> — you cannot
    /// call this without supplying the removal. A missed subscription shows up immediately as
    /// a feature that does nothing; a missed unsubscribe shows up as nothing at all, ever.
    /// The failure mode moved from invisible to obvious.</para>
    ///
    /// <para>⚠️ <b>What it does not buy, stated plainly:</b> a raw <c>X += Handler</c> written
    /// somewhere else still leaks, exactly as before. This makes the right way the easy way;
    /// it does not make the wrong way impossible. <c>SessionSubscriptionTeardownTests</c> is
    /// what actually goes red — it asks whether the events still reach a disposed session,
    /// which catches a raw <c>+=</c> too.</para>
    ///
    /// <para>Derived classes use it from their own constructors — those run after this one,
    /// so the list is already there, and their subscriptions are released with everyone
    /// else's rather than through a <c>DisposeCore</c> override that can be forgotten.</para>
    /// </summary>
    protected void Subscribe<TArgs>(
        EventHandler<TArgs> handler,
        Action<EventHandler<TArgs>> subscribe,
        Action<EventHandler<TArgs>> unsubscribe)
    {
        subscribe(handler);
        _unsubscribe.Add(() => unsubscribe(handler));
    }

    /// <inheritdoc cref="Subscribe{TArgs}"/>
    protected void Subscribe(
        EventHandler handler, Action<EventHandler> subscribe, Action<EventHandler> unsubscribe)
    {
        subscribe(handler);
        _unsubscribe.Add(() => unsubscribe(handler));
    }

    /// <inheritdoc cref="Subscribe{TArgs}"/>
    protected void Subscribe(
        PropertyChangedEventHandler handler,
        Action<PropertyChangedEventHandler> subscribe,
        Action<PropertyChangedEventHandler> unsubscribe)
    {
        subscribe(handler);
        _unsubscribe.Add(() => unsubscribe(handler));
    }

    protected SessionContext Context { get; }

    public abstract SessionKind Kind { get; }

    // ---- 显示 / 发送偏好的持久化 ----

    /// <summary>
    /// 应用偏好期间抑制回写。
    ///
    /// 不加这个的话，<see cref="ApplyStoredPreferences"/> 里的每一次赋值都会
    /// 触发 PropertyChanged → 立即把刚读出来的值再写回去。功能上无害，
    /// 但每建一个会话就白写一轮盘，且掩盖了「谁改的」这个信息。
    /// </summary>
    private bool _applyingPreferences;

    /// <summary>
    /// 把上次记住的显示与发送偏好应用到面板上。
    ///
    /// <b>按会话类型各存一套</b>：监听会话要看通道列与增量列，终端不需要；
    /// 终端习惯相对时间戳，监听要绝对时间戳。共用一套会让切换会话类型时列布局乱掉。
    ///
    /// 派生类在自己的构造函数末尾调用 —— 必须在派生类设完自身固有项之后，
    /// 否则会被那些硬编码覆盖。
    /// </summary>
    protected void ApplyStoredPreferences()
    {
        var prefs = Kind == SessionKind.Monitor
            ? Context.Settings.Monitor
            : Context.Settings.Terminal;

        _applyingPreferences = true;
        try
        {
            LogPanel.DisplayFormat = prefs.Display.Format;
            LogPanel.TimestampMode = prefs.Display.Timestamp;
            LogPanel.ShowChannelColumn = prefs.Display.ShowChannel;
            LogPanel.ShowDeltaColumn = prefs.Display.ShowDelta;
            LogPanel.ShowSentData = prefs.Display.ShowSent;

            // ⚠️ 发送区的 HexMode / LineEnding 刻意不在这里 —— 它们 2026-07-31 起不再持久化。
            // 每次新建会话都回到 ASCII + 无结束符，理由见 AppSettingsModel 里那段注释。
        }
        finally
        {
            _applyingPreferences = false;
        }
    }

    /// <summary>
    /// 面板上任何一项偏好变化即回写设置。
    ///
    /// ⚠️ 刻意只挑这几个属性：<c>AutoScroll</c> 与 <c>IsPaused</c> 是**瞬时状态**，
    /// 不是偏好。把它们存下来会出现「启动后不滚动，用户以为坏了」——
    /// 每次启动都应当是跟随末尾。
    ///
    /// ⚠️ <b>发送区的 <c>IsHexMode</c> / <c>LineEnding</c> 2026-07-31 起也不在这里了</b>
    /// （01-spec 4.5 已从「记」移到「不记」）。理由与上面那两条不同 ——
    /// 它们不是瞬时状态，而是**会改变线上真实字节**的开关，见 <c>AppSettingsModel</c> 里那段注释。
    ///
    /// ⚠️ <b>下面那句 <c>relevant</c> 判定是 <c>PreferenceWriteBindingTests</c> 的取处</b> ——
    /// 它按那句话的开头做锚点、取到分号为止，再从中抓 <c>nameof(...)</c>。改写法要连它一起改。
    ///
    /// ⚠️ <b>别在本注释里原样写出那句锚点</b>：<c>IndexOf</c> 会先命中注释，
    /// 取到的区间里一个 <c>nameof</c> 都没有，扫描器当场变瞎。
    /// 2026-07-31 真踩了一次，被 <c>PreferenceListScannerIsNotBlind</c> 逮住。
    /// </summary>
    private void OnPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        // P1-40: pausing is not a persisted preference, so it is handled before the filter below
        // and returns early. Both surfaces that reveal the paused state are computed properties,
        // so they only refresh if we say so.
        if (e.PropertyName == nameof(LogPanelViewModel.IsPaused))
        {
            OnPropertyChanged(nameof(PauseButtonText));
            OnPropertyChanged(nameof(PauseMenuText));
            OnPropertyChanged(nameof(StatusText));
            return;
        }

        if (_applyingPreferences) return;

        var relevant = e.PropertyName is
            nameof(LogPanelViewModel.DisplayFormat) or
            nameof(LogPanelViewModel.TimestampMode) or
            nameof(LogPanelViewModel.ShowChannelColumn) or
            nameof(LogPanelViewModel.ShowDeltaColumn) or
            nameof(LogPanelViewModel.ShowSentData);

        if (!relevant) return;

        var display = new DisplayPreferences
        {
            Format = LogPanel.DisplayFormat,
            Timestamp = LogPanel.TimestampMode,
            ShowChannel = LogPanel.ShowChannelColumn,
            ShowDelta = LogPanel.ShowDeltaColumn,
            ShowSent = LogPanel.ShowSentData
        };

        // 整节替换 —— 一次赋值即触发持久化（去抖后原子写）。
        if (Kind == SessionKind.Monitor)
        {
            Context.Settings.Monitor = Context.Settings.Monitor with { Display = display };
        }
        else
        {
            Context.Settings.Terminal = Context.Settings.Terminal with { Display = display };
        }
    }

    /// <summary>标签页与窗口标题上显示的名称。</summary>
    public abstract string Title { get; }

    /// <summary>状态栏文本，由各会话类型自行组织。</summary>
    public abstract string StatusText { get; }

    public LogPanelViewModel LogPanel { get; }

    public SendPanelViewModel SendPanel { get; }

    /// <summary>
    /// The serial control signal panel (T-07, spec 4.15), or <b>null</b> when this session type
    /// has no single port to report on.
    ///
    /// <para>⭐ <b>Null is how "monitor sessions do not get this panel" (spec 4.15, promise 2) is
    /// enforced</b> — <c>MonitorCaptureSession</c> does not implement
    /// <see cref="IControlLineSession"/>, so the capability test below simply finds nothing. The
    /// view binds its visibility to this being non-null rather than to a session-kind flag: a
    /// flag is a rule someone has to remember to write, and this is the absence of the thing
    /// itself.</para>
    /// </summary>
    public SignalPanelViewModel? SignalPanel { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    private ConnectionState _state = ConnectionState.Disconnected;

    public bool IsConnected => State == ConnectionState.Connected;

    /// <summary>
    /// ⚠️ <b>The two notifications are the whole of P2-54.</b> Until 2026-08-04 this property had
    /// none, because nothing on screen depended on it: the toolbar label was a constant and the
    /// status bar never mentioned recording. Whether the tool was recording was, literally,
    /// unobservable from the UI.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordButtonText))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _isRecording;

    /// <summary>
    /// ⭐ <b>What the capture thread reads instead of <see cref="IsRecording"/></b> (P2-32).
    ///
    /// <para><see cref="OnFrameCaptured"/> runs on the capture thread, while
    /// <see cref="IsRecording"/> is written by the UI thread. A plain field read has no memory
    /// barrier, so the capture thread could keep seeing a stale value for an unbounded time.
    /// ⛔ <b>Both directions lose something real</b>: stale <c>false</c> after the user presses
    /// Record drops frames from the recording, and stale <c>true</c> after Stop hands frames to
    /// a recorder that silently discards them (the behaviour P2-29's mutation round found).
    /// For a tool whose job is not to lose data, "probably visible soon" is not good enough.</para>
    ///
    /// <para>⚠️ <b>Written in exactly one place</b> — the generated
    /// <c>OnIsRecordingChanged</c> hook below — so it cannot drift from the property. A mirror
    /// with two writers would be a worse defect than the one it fixes.</para>
    /// </summary>
    private volatile bool _recordingVisibleToCapture;

    partial void OnIsRecordingChanged(bool value) => _recordingVisibleToCapture = value;

    /// <summary>
    /// 面向开发者的原始异常消息。<b>刻意不绑定到界面</b> ——
    /// 它是英文框架文案，界面上要显示的原因由 <see cref="ErrorNotice"/> 给出。
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// 当前待呈现的错误分类；<c>null</c> 表示没有未处理的错误。
    ///
    /// ⚠️ <b>存的是分类而不是成品文本</b>：文本由 <see cref="ErrorNotice"/> 实时算出，
    /// 于是切换界面语言时提示条会跟着翻译。存字符串就做不到 ——
    /// 那正是 03-conventions 坑 3「计算属性不会自动刷新」的反面用法。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ErrorNotice))]
    [NotifyPropertyChangedFor(nameof(HasErrorNotice))]
    [NotifyCanExecuteChangedFor(nameof(DismissErrorCommand))]
    private SerialErrorKind? _errorKind;

    /// <summary>顶部非模态提示条要显示的原因（01-spec 4.7）。</summary>
    public string? ErrorNotice => ErrorKind is { } kind
        ? SessionErrorPresenter.Describe(kind, Localization)
        : null;

    public bool HasErrorNotice => ErrorKind is not null;

    /// <summary>
    /// 关闭提示条。规格要求提示条<b>常驻</b>直到用户关闭或被下一条替换，
    /// 因此这里是唯一的「消失」入口，没有自动超时。
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasErrorNotice))]
    private void DismissError() => ErrorKind = null;

    /// <summary>
    /// 记下一条待呈现的错误。新的**替换**旧的 —— 规格定的是「同时只存在一条」。
    /// </summary>
    /// <param name="kind">用于界面呈现的分类。</param>
    /// <param name="diagnostic">原始异常消息，只作诊断用，不进界面。</param>
    private void ReportError(SerialErrorKind kind, string? diagnostic = null)
    {
        var replaced = ErrorKind;

        ErrorMessage = diagnostic;
        ErrorKind = kind;

        // ⚠️ 记的是「界面上显示了什么」，不是「出错了」（03-conventions 8.4.5）。
        //
        // 这条日志要能让不在现场的人回答三个问题：哪条路径触发的（Kind）、
        // 用户在哪个会话上撞到的（Session）、以及**上一条是不是被顶掉了**（Replaced）——
        // 最后这项是规格「同时只存在一条」的直接后果，没有它就分不清
        // 「只报了一条」和「报了三条但前两条被覆盖」。
        //
        // Detail 是框架异常原文，只在日志里出现；界面上显示的是 Kind 映射出的本地化文案。
        // 路径 4 / 5 没有底层异常，Detail 为空是正常的 —— 它们此前连日志都没有。
        _logger.LogWarning(
            "Error notice shown: kind={Kind}, session={Session}, replaced={Replaced}, detail={Detail}",
            kind, Kind, replaced?.ToString() ?? "none", diagnostic ?? "-");
    }

    /// <summary>
    /// 尝试建立连接，**把失败返回而不是显示成提示条**。返回 <c>null</c> 即成功。
    ///
    /// <b>新建会话对话框用它</b>：打不开端口时对话框要**保持打开**、
    /// 在对话框内提示「通道 B（COM7）无法打开」，用户改个端口直接重试
    /// （规格见 docs/01-spec.md 4.7）。走 <see cref="ConnectCommand"/> 做不到 ——
    /// 那条路径把异常转成顶部提示条，而那时会话界面已经出现了。
    ///
    /// ⚠️ <b>刻意不设 <see cref="ErrorKind"/></b>：提示条与对话框内提示是两种呈现，
    /// 同时出现就成了「同一件事说两遍」。
    /// </summary>
    public async Task<SessionOpenFailure?> TryConnectAsync()
    {
        try
        {
            ErrorMessage = null;
            ErrorKind = null;
            await _capture.StartAsync();
            return null;
        }
        catch (Exception ex)
        {
            State = ConnectionState.Faulted;

            var failure = SessionOpenFailure.From(ex);

            // 原文只进日志，与 ReportError 的口径一致 —— 界面上显示的是分类。
            //
            // ⚠️ 记 failure.Detail 而不是 ex.Message：监听失败时 ex 是我们自己包装的
            // SerialPortOpenException，它的消息只有「Failed to open COM1 (channel B).」，
            // **说不出为什么**。failure.Detail 取的是 inner 的原文（「Access to the path
            // 'COM1' is denied.」）。Channel 与 Kind 一并记上，于是这一行自己就能回答
            // 「哪一路、什么原因、界面上显示了什么」——03-conventions 8.4.5 的要求。
            _logger.LogWarning(
                "Session open failed before the dialog closed: session={Session}, "
                + "channel={Channel}, kind={Kind}, detail={Detail}",
                Kind, failure.Channel, failure.Kind, failure.Detail ?? "-");

            return failure;
        }
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        try
        {
            // 重连之前先清掉上一次的提示条 —— 否则用户会对着一条已经不成立的原因发呆。
            ErrorMessage = null;
            ErrorKind = null;
            await _capture.StartAsync();
        }
        catch (Exception ex)
        {
            // 路径 1：打不开端口。原文进 ErrorMessage（诊断），分类进 ErrorKind（界面）。
            //
            // ⚠️ 这条路径现在只服务**已有会话的重连**（工具栏/菜单的「连接」）——
            // 新建会话时的打开失败走 TryConnectAsync，提示显示在对话框内。
            ReportError(SerialErrorClassifier.Classify(ex), ex.Message);
            State = ConnectionState.Faulted;
        }
    }

    private bool CanConnect() => State is ConnectionState.Disconnected or ConnectionState.Faulted;

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private async Task DisconnectAsync() => await _capture.StopAsync();

    /// <summary>
    /// Clear means "start looking again from now" (P2-20, option Y): every visible counter
    /// resets together, not just the ones the log panel happens to own.
    ///
    /// Before this, Clear reached <see cref="LogPanelViewModel.FrameCount"/> but not the monitor
    /// session's per-channel byte counters, so the status bar showed 8 frames next to 14626 bytes
    /// — the same line carrying two different epochs. The defect was the inconsistency, not the
    /// choice of epoch.
    ///
    /// NOTE: clearing never touches capture or recording (spec 4.10). It resets the view.
    /// </summary>
    [RelayCommand]
    private void Clear()
    {
        LogPanel.ClearCommand.Execute(null);
        OnCleared();

        // StatusText is computed; without this it keeps showing the old counts until the next
        // frame batch arrives — and on a silent bus that is forever.
        OnPropertyChanged(nameof(StatusText));
    }

    /// <summary>
    /// Derived classes reset their own visible counters here (P2-20).
    /// Anything shown in the status bar or the side panel belongs in this override.
    /// </summary>
    protected virtual void OnCleared() { }

    [RelayCommand]
    private void TogglePause() => LogPanel.TogglePauseCommand.Execute(null);

    /// <summary>
    /// 开始 / 停止记录（C-09）。规格见 docs/01-spec.md 4.10。
    ///
    /// ⚠️ <b>开始时不弹任何对话框</b> —— 记录直接入库，保存位置到停止后导出时才问。
    /// 出问题那一刻用户想要的是立刻按下记录，而不是先跟文件对话框纠缠。
    /// </summary>
    [RelayCommand]
    private async Task ToggleRecordAsync()
    {
        if (IsRecording)
        {
            var batchId = _recorder.CurrentBatchId;
            var frames = _recorder.FramesWritten;

            await _recorder.StopAsync();
            IsRecording = false;

            // 空批次不必打扰用户 —— 点了记录又立刻点停止是常见的误操作。
            if (batchId is { } id && frames > 0) await ExportBatchAsync(id);
            return;
        }

        try
        {
            await _recorder.StartAsync(DescribeRecordingBatch());
            IsRecording = true;
        }
        catch (Exception ex)
        {
            // 开始记录就失败（目录不可写、磁盘满）——
            // 与写入中途失败走同一条路径，见 OnRecordingFailed。
            //
            // P2-53: 这里原先报 Unknown，于是界面只说「详细原因请查看日志文件」，
            // 而规格第 6 条路径要求说清**为什么写不了**。RecordingFailed 是它自己的那一类。
            ReportError(SerialErrorKind.RecordingFailed, ex.Message);
            _logger.LogError(ex, "Starting the recording failed.");
            IsRecording = false;
        }
    }

    /// <summary>派生类提供批次元信息（端口、别名、串口参数）。</summary>
    protected abstract RecordingBatchInfo DescribeRecordingBatch();

    /// <summary>
    /// 记录写库失败（01-spec 4.7 的第 6 条路径）。
    ///
    /// ⚠️ <b>必须把 <see cref="IsRecording"/> 改回去</b> —— 否则按钮上仍写着「停止记录」，
    /// 而实际早就没在记。用户会放心去复现问题，回来发现记录是空的。
    /// **界面不能暗示一件没有发生的事。**
    /// </summary>
    /// <summary>
    /// A hardware line error — parity, framing, overrun (P1-52, 01-spec 4.7).
    ///
    /// <para>⭐ <b>This is the whole user-visible half of P1-52.</b> Before it, those three
    /// <c>FrameFlags</c> values had <b>no producer anywhere in the project</b>: the contract
    /// mentioned them, the display layer had colours ready for them, and the data never came.
    /// A wrong baud rate showed up as garbage on screen with nothing naming the cause.</para>
    ///
    /// <para>⚠️ <b>The session keeps running.</b> Nothing is stopped and no state changes —
    /// what is in doubt is the data, and the user is the one who decides whether the port
    /// settings are wrong. Reporting it as a fault would close a session over what may be a
    /// single glitch.</para>
    ///
    /// <para>⚠️ <b>Dispatched to the UI thread</b>, like <see cref="OnRecordingFailed"/>: this
    /// arrives on a driver callback thread, and <c>ReportError</c> writes properties the view
    /// is bound to.</para>
    /// </summary>
    private void OnLineErrorDetected(object? sender, SerialErrorEventArgs e)
        => Dispatcher.UIThread.Post(() => ReportError(e.Kind, e.Message));

    /// <summary>
    /// The recording writer loop died (01-spec 4.7, path 6).
    ///
    /// <para>⛔ <b>Two halves, and the second one used to be missing entirely</b> (P2-89).
    /// Turning <see cref="IsRecording"/> back is what the user sees; calling
    /// <c>StopAsync</c> is what makes the <b>next</b> press of Record work. Without it the
    /// recorder stayed at <c>IsRecording == true</c> with a dead writer loop, so the retry hit
    /// its early return, the button read "Stop recording", and <b>every frame went into a queue
    /// nobody was reading</b> — no log line, no banner, nothing written. That is the
    /// [第 1 层] "the tool is lying" defect at the exact moment the user most wants a record.</para>
    ///
    /// <para>⚠️ <b>The cleanup deliberately does not run on the UI thread.</b> It is I/O, and
    /// the UI half must not wait on it.</para>
    ///
    /// <para>⛔ <b>And it must not be awaited on this call stack</b>: this handler is invoked
    /// from inside the writer loop itself, and <c>StopAsync</c> waits for that very loop to
    /// finish. Starting it with <c>_ =</c> lets it park on its first await, so the loop returns
    /// and the drain completes immediately instead of burning the 2 s timeout.</para>
    /// </summary>
    private void OnRecordingFailed(object? sender, RecordingFailedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsRecording = false;
            ReportError(SerialErrorKind.RecordingFailed, e.Exception.Message);   // P2-53
        });

        _ = ReleaseRecorderAfterFailureAsync();
    }

    /// <summary>
    /// Hands the failed batch back to the recorder so the next <c>StartAsync</c> is a real one.
    ///
    /// <para>⚠️ <b>Fire-and-forget, so it swallows and logs its own failures</b> — an escaping
    /// exception here would land on an unobserved faulted <c>Task</c>, which is precisely the
    /// shape 00-STATUS P2-86 is about. There is no caller left to report to: the user has
    /// already been told the recording failed.</para>
    /// </summary>
    private async Task ReleaseRecorderAfterFailureAsync()
    {
        try
        {
            await _recorder.StopAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Releasing the recorder after a write failure failed.");
        }
    }

    /// <summary>
    /// 「导出」按钮（M-08）—— 导的是**显示缓冲**里的内容。
    ///
    /// ⚠️ <b>与「停止记录后导出」是两件事</b>：那一条导的是**整个批次**（全量、从库里读）。
    /// 显示缓冲上限 500 帧（2026-08-11 由 5000 降下来），清空过、暂停过的都不在里面。
    /// </summary>
    [RelayCommand]
    private Task ExportAsync()
    {
        // 先取快照 —— 导出对话框开着的时候采集仍在跑，
        // 边导边变会让「导出的是哪一屏」说不清。
        //
        // ⭐ **按 Sequence 排序，不用显示缓冲的原有顺序**（P1-50，2026-08-04）：
        // ICaptureSession.FrameCaptured 只保证**序号在锁内单调分配**，不保证事件按序到达
        // （监听会话四个并发生产者，锁内组帧、锁外发布）。于是显示缓冲里相邻两行的
        // Sequence 可能是倒的，而**文件比屏幕活得久** —— 屏上一次瞬时错序看过就算了，
        // 导出去的文件是要被贴进工单、喂给解析器的。
        //
        // ⚠️ **刻意只在这里排，不去改采集侧**：有序发布要么把发布收进锁（订阅方的耗时
        // 就进了临界区，那正是当初刻意避开的），要么加一条单一派发队列。
        // **代价在热路径上，而这里是一次点击、几千帧的冷路径** —— 同一个问题，
        // 在便宜的那一端解决。⛔ 显示区本身仍按到达顺序画，那一半是**已声明的局限**
        // （01-spec 4.9.3a），不是这里能修的。
        var snapshot = LogPanel.Frames
            .Select(f => f.Frame)
            .OrderBy(f => f.Sequence)
            .ToArray();

        // 别名取**当前**值（用户随时可改）。
        // ⚠️ 2026-08-01 起批次导出那条也取当前值，两条路径口径一致 —— 理由见 ExportBatchAsync。
        return RunExportAsync(
            DescribeExportBaseName("export"),
            ChannelsForExport(),
            _ => ToAsync(snapshot));
    }

    private static async IAsyncEnumerable<SerialFrame> ToAsync(IEnumerable<SerialFrame> frames)
    {
        foreach (var f in frames) yield return f;
        await Task.CompletedTask;
    }

    /// <summary>停止记录后导出刚结束的那个批次 —— 全量，从库里流式读。</summary>
    private async Task ExportBatchAsync(long batchId)
    {
        var summary = await Context.RecordingReader.GetBatchAsync(batchId);
        var baseName = summary is null
            ? DescribeExportBaseName("record")
            : $"diserial-{summary.PortLabel}-{summary.SettingsLabel}-{summary.StartedAt.LocalDateTime:yyyyMMdd-HHmmss}";

        // ⭐ 别名取**会话当前的**，与工具栏那条导出路径同一口径（2026-08-01 反转，P0-9 的连带）。
        //
        // ⚠️ 这里 2026-07-31 到 2026-08-01 之间是相反的：取 summary.AliasA/AliasB，
        //    即「记录开始那一刻叫什么」，理由是「导出要还原当时的称呼」。
        //    **那条理由随 P0-9 的修法失效了** —— 别名不再是「这一路当时的身份」，
        //    而是**用户给这个端口贴的标签**：默认等于端口名，看清流量之后改成 PLC / HMI。
        //    COM6 一直就是 PLC，改名只是补上标签，所以追溯生效才是对的。
        //
        // ⚠️ 不反转的话会出现：先点记录、看几秒改名、再停止 →
        //    批次文件写 COM6 而工具栏导出写 PLC，**同一批帧两个答案**。
        //    而「先记录后改名」在新设计下是常见流程，不是边角情形。
        //
        // ⚠️ 若将来做「打开历史批次再导出」（V1.x），那时会话已不在、当前别名取不到，
        //    只能回退到 summary 里的快照 —— 届时应当在**停止记录时把当前别名回写进批次**，
        //    而不是让两条路径的语义再次分叉。护栏：ChannelIdentityTests。
        await RunExportAsync(
            baseName, ChannelsForExport(),
            ct => Context.RecordingReader.ReadBatchAsync(batchId, ct));
    }

    /// <summary>
    /// 导出要带的两路通道信息：**端口名 + 别名各一份**。
    ///
    /// 两者都取<b>会话当前值</b>，两条导出路径共用这一个来源 ——
    /// 于是「同一批帧只有一个答案」这条不依赖调用方记得传对参数。
    /// 终端会话四个值全是 null，导出时通道列本就关着。
    /// </summary>
    private ExportChannels ChannelsForExport() => new(
        ResolveChannelPort(ChannelId.A), ResolveChannelAlias(ChannelId.A),
        ResolveChannelPort(ChannelId.B), ResolveChannelAlias(ChannelId.B));

    /// <summary>
    /// Shows the export options dialog and, once the user confirms, writes the file.
    /// Shared by both export paths.
    ///
    /// <para>⚠️ The four display-related fields are seeded from the session's <b>current</b>
    /// display settings — users mostly want to export "what they are looking at", so starting
    /// from there costs fewer clicks than starting from fixed defaults.</para>
    ///
    /// <para>⚠️ <b>The <c>using</c> below is load-bearing, not tidiness</b> (P1-48).
    /// <see cref="ExportDialogViewModel"/> derives from <see cref="LocalizedViewModelBase"/>,
    /// which subscribes to <c>CultureChanged</c> in its constructor — and the localization
    /// service is a singleton, so it holds the VM forever. Without the <c>using</c>, every
    /// press of "Export" and every stop-recording leaks one VM.</para>
    ///
    /// <para>⚠️ It has to be <c>using</c> rather than a <c>Dispose()</c> call after the write:
    /// the cancel path returns early, and cancelling is the common case.</para>
    ///
    /// <para>Same defect as <c>MainWindowViewModel.NewSessionAsync</c> — that one was fixed as
    /// "P0-5's second site" while this one was missed. Guard: <c>ExportDialogLifetimeTests</c>.</para>
    /// </summary>
    private async Task RunExportAsync(
        string suggestedBaseName,
        ExportChannels channels,
        Func<CancellationToken, IAsyncEnumerable<SerialFrame>> source)
    {
        using var vm = new ExportDialogViewModel(
            Context.Localization,
            Context.EnumChoices,
            Context.DialogService,
            new ExportDialogSeed(
                suggestedBaseName,
                LogPanel.DisplayFormat,
                LogPanel.TimestampMode,
                LogPanel.ShowChannelColumn,
                LogPanel.ShowDeltaColumn,
                channels,
                ResolveExportDirectory()));

        var request = await Context.DialogService.ShowExportDialogAsync(vm);
        if (request is null) return;                 // 用户取消

        try
        {
            var frames = new List<SerialFrame>();
            await foreach (var f in source(CancellationToken.None)) frames.Add(f);

            await Context.ExportService.ExportAsync(frames, request.FilePath, request.Options);
            _logger.LogInformation(
                "Exported {Count} frames to {Path}", frames.Count, request.FilePath);

            // P33: remember where it landed, so the next export opens there instead of Documents.
            // ⛔ This line is AFTER the write on purpose (user decision 2026-08-10): cancelling
            // never reaches here, and a failed write throws past it. What gets remembered is
            // where a file actually appeared -- not where one was aimed.
            var directory = Path.GetDirectoryName(request.FilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Context.Settings.LastExportDirectory = directory;
            }
        }
        catch (Exception ex)
        {
            // 写不出去要说出来 —— 与记录写库失败同一条纪律：
            // 界面不能让用户以为文件已经存好了。
            _logger.LogError(ex, "Export to {Path} failed.", request.FilePath);
            ReportError(SerialErrorKind.Unknown, ex.Message);
        }
    }

    /// <summary>
    /// 导出对话框该开在哪个目录（P33，2026-08-10 用户提）。
    /// 返回 <c>null</c> 表示「没有可用的记忆」，对话框据此落到「文档」。
    ///
    /// <para>⛔ <b>存在性检查在这里，不在对话框里</b>：那是一次真实的磁盘查询，
    /// 而记住的目录随时可能没了 —— <b>U 盘拔掉、文件夹删掉、换一台机器同一份设置</b>。
    /// ⚠️ 用户定的是「<b>回落到文档，并记一条日志</b>」：
    /// 界面上不拦路（照常导得出去），⭐ <b>而「它为什么又忘了」这个问题在日志里答得出来</b> ——
    /// 静默回落的话，用户只会觉得这个功能坏了，且查不出原因。</para>
    ///
    /// <para>⚠️ <b>目录消失与目录不可写是两回事</b>：本方法只答前者。
    /// 后者由导出本身的失败路径处理（<c>ReportError</c> + 一条 <c>Error</c> 日志），
    /// ⛔ 而它<u>不会</u>污染这条记忆 —— 写回那一行排在导出成功之后。</para>
    /// </summary>
    private string? ResolveExportDirectory()
    {
        var remembered = Context.Settings.LastExportDirectory;
        if (string.IsNullOrWhiteSpace(remembered)) return null;

        if (Directory.Exists(remembered)) return remembered;

        _logger.LogInformation(
            "Last export directory {Path} is gone; the export dialog falls back to Documents.",
            remembered);
        return null;
    }

    /// <summary>
    /// 默认文件名的基名（不含扩展名）。
    ///
    /// ⚠️ <b>派生类<u>必须</u>覆写以带上端口信息</b>（P0-7 b，2026-07-31 定）。
    /// 本实现只是「派生类还没接上」时的兜底，不该被任何会话真正用到。
    ///
    /// <b>为什么是虚方法而不是在基类里直接取</b>：只有派生类知道端口 ——
    /// 终端有一个 <c>Port.PortName</c>，监听有两个通道。在基类里拿就得向下转型。
    ///
    /// ⚠️ <b>这里曾经是个死接缝</b>：注释写着「派生类可覆写以带上端口信息」，
    /// 而**两个派生类都没覆写**。后果是两条导出路径的文件名规则不一致 ——
    /// 批次导出走 <c>summary.PortLabel</c> 得到 <c>diserial-COM6-COM7-115200-8N1-…</c>，
    /// 而「导出」按钮走本方法得到 <c>diserial-export-…</c>，**不带任何端口信息**。
    /// 2026-07-31 真机实测时两个文件名摆在一起才看出来。
    /// </summary>
    protected virtual string DescribeExportBaseName(string kind) =>
        $"diserial-{kind}-{DateTime.Now:yyyyMMdd-HHmmss}";

    /// <summary>
    /// 语言切换时，除刷新自身计算属性外，还要重算显示区已有的行。
    ///
    /// 行内的解码摘要在 FrameViewModel 里是解析后缓存的，
    /// 不重算就会出现「新帧新语言、旧帧旧语言」的混排。
    /// 这里复用基类已有的那一个订阅，<b>不为每一行单独订阅</b> ——
    /// 显示区上限 500 行，逐行订阅会造成事件订阅爆炸与内存泄漏。
    /// </summary>
    protected override void OnCultureChanged()
    {
        LogPanel.RefreshAll();
        base.OnCultureChanged();
    }

    /// <summary>把连接状态映射为当前语言的文本。派生类可覆写以使用自己的措辞。</summary>
    /// <summary>
    /// The connection state on its own. Override to change the wording per session type
    /// (the monitor session says "Monitoring" / "Not started" instead of "Connected").
    /// </summary>
    protected virtual string DescribeConnectionState() => State switch
    {
        ConnectionState.Connected => L(LocKeys.StateConnected),
        ConnectionState.Connecting => L(LocKeys.StateConnecting),
        ConnectionState.Faulted => L(LocKeys.StateFaulted),
        _ => L(LocKeys.StateDisconnected)
    };

    /// <summary>
    /// What the status bar shows: the connection state, plus a "display paused" suffix
    /// while the display is paused (P1-40).
    ///
    /// Pausing is otherwise invisible: the only observable effect is "the log stopped moving",
    /// which looks exactly like a dead device, a pulled cable, or a hung app. In a tool whose
    /// whole value is showing what happened on the bus, a state that shows nothing has to be
    /// able to say why it is showing nothing.
    ///
    /// NOTE: the suffix is APPENDED, never a replacement. Capture, counters and recording all
    /// keep running while paused (spec 4.2a) — replacing "Monitoring" with "Paused" would state
    /// the opposite of what is actually happening.
    ///
    /// NOTE: this is deliberately non-virtual and sits between the status bar and
    /// <see cref="DescribeConnectionState"/>, so a future session type cannot forget the suffix
    /// by overriding the wrong member.
    /// </summary>
    protected string DescribeState()
    {
        var state = DescribeConnectionState();

        // P2-54, and the same reasoning as the paused suffix below it: recording is otherwise
        // invisible. The user presses "Record" and nothing on screen changes, so "am I actually
        // recording" is unanswerable -- and Q-9's 6a already reproduced recording stopping
        // SILENTLY, which is the case where a whole field trip's data is lost without a hint.
        //
        // NOTE: appended, not a replacement, exactly like the paused suffix -- the session really
        // is still connected and still capturing while it records.
        if (IsRecording) state = LF(LocKeys.StateRecording, state);

        if (LogPanel.IsPaused) state = LF(LocKeys.StateDisplayPaused, state);

        return state;
    }

    /// <summary>
    /// Toolbar label for the pause button: swaps to "Resume" while paused (P1-40).
    ///
    /// Without this the button reads "Pause" when it is already paused, leaving the way back
    /// undiscoverable — the button is the only control that can undo it.
    /// </summary>
    public string PauseButtonText =>
        L(LogPanel.IsPaused ? LocKeys.ToolbarResume : LocKeys.ToolbarPause);

    /// <summary>
    /// Session-menu label for the same command. Separate from <see cref="PauseButtonText"/> only
    /// because the menu says "Pause receiving" where the toolbar says "Pause" — menus carry the
    /// fuller wording, and binding the menu straight to the toolbar label would have quietly
    /// shortened it.
    ///
    /// <para>⛔ <b>The menu item used to be a constant</b> (P2-99): it read "Pause receiving" even
    /// while already paused, so it named an action that was not the one it would perform — and it
    /// invokes the very same <c>TogglePauseCommand</c> as the toolbar button, which had been fixed
    /// for exactly this reason back in P1-40. One surface was corrected and the other was left
    /// behind.</para>
    /// </summary>
    public string PauseMenuText =>
        L(LogPanel.IsPaused ? LocKeys.MenuSessionResume : LocKeys.MenuSessionPause);

    /// <summary>
    /// Toolbar label for the record button: swaps to "Stop recording" while recording (P2-54).
    ///
    /// <para>⛔ <b>It used to be a constant</b>, so recording and not-recording looked identical
    /// and the button could not tell the user which way it went. Worse, the comment on
    /// <see cref="OnRecordingFailed"/> already warned that failing to reset
    /// <see cref="IsRecording"/> would leave the button reading "Stop recording" — <b>describing
    /// a screen that did not exist</b>. This makes that warning true.</para>
    /// </summary>
    public string RecordButtonText =>
        L(IsRecording ? LocKeys.ToolbarStopRecord : LocKeys.ToolbarRecord);

    /// <summary>
    /// 派生类可覆写以提供通道**别名**（监听会话用）—— 只是用户起的那个名字，
    /// 不含端口名。进导出文件的 <c>Alias</c> 列。
    /// </summary>
    protected virtual string? ResolveChannelAlias(ChannelId channel) => null;

    /// <summary>
    /// 派生类可覆写以提供通道对应的**端口名**（监听会话用）。
    /// 进导出文件的 <c>Port</c> 列 —— 2026-08-01 起它取代了原先的 <c>Channel</c>（A/B）列，
    /// 与界面口径一致，见 01-spec 4.13。
    /// </summary>
    protected virtual string? ResolveChannelPort(ChannelId channel) => null;

    /// <summary>
    /// 派生类可覆写以提供**帧行标签**（监听会话用）：<c>COM6</c> 或 <c>COM6 · PLC</c>。
    ///
    /// ⚠️ <b>与上面两个分开，是因为三处的口径不同</b>：
    /// 帧行要紧凑（<c>COM6 · PLC</c>）、状态栏要能与计数器区分（<c>COM6「PLC」</c>）、
    /// 导出要机器可读（端口与别名各占一列）。**组装规则都在 <c>ChannelViewModel</c>，
    /// 本类只负责取。**
    /// </summary>
    protected virtual string? ResolveChannelLabel(ChannelId channel) => null;

    /// <summary>派生类处理发送请求。终端直接发，监听需先确认注入风险。</summary>
    protected virtual async Task SendAsync(ChannelId channel, ReadOnlyMemory<byte> data)
        => await _capture.SendAsync(channel, data);

    /// <summary>
    /// 采集线程回调。只做入队，不碰 UI —— 真正的刷新由 <see cref="_uiPump"/> 批量完成。
    /// 通道别名在此解析，因为它依赖会话状态，而排空时机不确定。
    ///
    /// <para>⚠️ <b>This method runs on the capture thread and reads two things the UI thread
    /// writes</b> — P2-32 filed it because a codebase that comments every threading decision had
    /// said nothing at all here, so a reader would reasonably assume it touched no shared
    /// state. The two are handled differently on purpose:</para>
    ///
    /// <list type="bullet">
    ///   <item><b>Recording state</b> — read through
    ///         <see cref="_recordingVisibleToCapture"/>, which is <c>volatile</c>. Staleness
    ///         here loses frames in one direction and writes to a stopped recorder in the
    ///         other, so it is closed rather than tolerated.</item>
    ///   <item><b>Channel alias</b> — read straight from the UI-owned
    ///         <c>ChannelViewModel.Alias</c>, and the staleness window is <b>accepted</b>. It is
    ///         a reference read, so nothing tears; the worst case is a frame or two carrying the
    ///         previous alias immediately after a rename. ⭐ <b>And that is already covered from
    ///         the other side</b>: P1-41 relabels the rows already in the buffer when a channel
    ///         is renamed, so the visible result converges either way.</item>
    /// </list>
    /// </summary>
    private void OnFrameCaptured(object? sender, FrameCapturedEventArgs e)
    {
        _pending.Enqueue((e.Frame, ResolveChannelLabel(e.Frame.Channel)));

        // ⚠️ 记录在**这里**喂，不在 DrainPending 里 —— 那是 UI 线程，
        // 界面一卡记录就跟着停。而且暂停只影响显示（01-spec 4.2a），
        // 记录必须照常，把它接在采集侧才不会被暂停牵连。
        //
        // WriteAsync 只入队、立即返回，不会拖慢采集线程。
        if (_recordingVisibleToCapture) _ = _recorder.WriteAsync(e.Frame);
    }

    /// <summary>在 UI 线程上排空队列并一次性刷新。</summary>
    private void DrainPending()
    {
        if (_pending.IsEmpty) return;

        var batch = new List<(SerialFrame Frame, string? Alias)>();
        while (_pending.TryDequeue(out var item))
        {
            batch.Add(item);
            OnFrameAppended(item.Frame);
        }

        LogPanel.AppendRange(batch);
        OnPropertyChanged(nameof(StatusText));
    }

    /// <summary>派生类可在此更新自身统计（如各通道字节数）。</summary>
    protected virtual void OnFrameAppended(SerialFrame frame) { }

    /// <summary>
    /// Stops the timed send the moment the session stops being connected -- the first of the
    /// three mandatory stops (01-spec 4.14, promise 7).
    ///
    /// <para><b>Why it hangs off the property rather than off the disconnect command.</b> Every
    /// path that leaves <see cref="ConnectionState.Connected"/> assigns this property: the
    /// explicit Disconnect, a cable being pulled (Faulted arrives on the capture event), and a
    /// failed reconnect. Hooking the single assignment point makes it impossible for a new
    /// path to forget -- and forgetting matters here: 01-spec 4.7 fixes the error banner at one
    /// at a time, newest replacing oldest, so a timer writing to a dead port would overwrite
    /// its own banner several times a second and the user would never get to read a stable
    /// reason for the failure.</para>
    ///
    /// <para>⚠️ Not to be confused with <see cref="OnCaptureStateChanged"/> below: this is the
    /// generated hook for the <c>State</c> property, that one is the capture-session event
    /// handler. The handler was renamed away from <c>OnStateChanged</c> precisely so the two
    /// do not read as overloads of one idea.</para>
    /// </summary>
    partial void OnStateChanged(ConnectionState value)
    {
        if (value != ConnectionState.Connected) StopTimedSendOnDisconnect();
    }

    /// <summary>
    /// Separate method so the guard test can name the reason it exists.
    /// <see cref="SendPanelViewModel.StopTimedSend"/> is idempotent, so repeated
    /// non-connected states are harmless.
    /// </summary>
    private void StopTimedSendOnDisconnect() => SendPanel.StopTimedSend();

    private void OnCaptureStateChanged(object? sender, ConnectionStateChangedEventArgs e)
        => Dispatcher.UIThread.Post(() =>
        {
            State = e.State;

            // 路径 3：连接中设备被拔出。原因由下层随事件带上来 ——
            // ⚠️ 此前这里只取 State，e.Message 被整条丢掉，界面上只剩「错误」二字。
            //
            // 只在真的带了分类时才覆盖：ConnectAsync 失败时下层也会置 Faulted，
            // 但那一路的分类由 ConnectAsync 自己的 catch 给出（更准），别被 Unknown 盖掉。
            if (e.State == ConnectionState.Faulted && e.ErrorKind != SerialErrorKind.Unknown)
            {
                ReportError(e.ErrorKind, e.Message);
            }
        });

    private async void OnSendRequested(object? sender, SendRequestedEventArgs e)
    {
        // 路径 5：未连接就发送。
        // ⚠️ 这个判断必须在这里做，不能只靠下层 —— TerminalCaptureSession.SendAsync
        // 在未连接时是 `return`，既不抛异常也不记日志，用户侧就是「按了没反应」。
        if (State != ConnectionState.Connected)
        {
            ReportError(SerialErrorKind.NotConnected);
            return;
        }

        try
        {
            await SendAsync(e.Channel, e.Data);

            // 01-spec 4.14 promise ②: the box is cleared after a SUCCESSFUL send only.
            // ⚠️ This line is the promise. Moving it up next to the State check above would
            // cover path 5 and quietly leave path 2 broken -- a failed write is caught below,
            // i.e. AFTER this point, which is exactly why the clear has to live here (P2-58).
            SendPanel.ConfirmSent(e.SourceText);
        }
        catch (Exception ex)
        {
            // Second of the three mandatory stops (01-spec 4.14, promise 7): one failed write
            // means every following tick would fail too, each one replacing the error banner
            // that the user is still trying to read.
            SendPanel.StopTimedSend();

            // 路径 2：发送失败。
            ReportError(SerialErrorClassifier.Classify(ex), ex.Message);
        }
    }

    /// <summary>路径 4：发送区输入无法解析为字节。</summary>
    private void OnInputRejected(object? sender, EventArgs e)
        => ReportError(SerialErrorKind.InvalidInput);

    private void OnPayloadMissing(object? sender, EventArgs e)
        => ReportError(SerialErrorKind.NothingToSend);

    private void OnIntervalRejected(object? sender, EventArgs e)
        => ReportError(SerialErrorKind.IntervalTooSmall);

    /// <summary>
    /// Confirms and performs "clear the whole send history".
    ///
    /// <para>⚠️ <b>The prompt quotes the stored count, not the visible one.</b> The dropdown
    /// shows a dozen rows while the table holds up to a hundred, so the number on screen would
    /// understate the damage roughly eightfold — and since there is no history management
    /// screen (and none planned), <b>this prompt is the only place the user ever learns how
    /// much is actually kept</b>.</para>
    ///
    /// <para>⚠️ <b>No undo, deliberately.</b> This exists so payloads sent to a customer's bus
    /// can be made to actually go away; keeping a recoverable copy would defeat it. All the
    /// protection therefore sits in front of the action.</para>
    ///
    /// <para>Failures are reported rather than swallowed — <c>ISendHistoryStore.Clear</c> is
    /// the one method there that throws, because claiming success over a table that is still
    /// full is the worst thing this feature could do.</para>
    /// </summary>
    private async void OnClearHistoryRequested(object? sender, EventArgs e)
    {
        var count = SendPanel.StoredHistoryCount;
        if (count == 0) return;

        var confirmed = await Context.DialogService.ShowConfirmationAsync(
            L(LocKeys.SendHistoryClearTitle),
            LF(LocKeys.SendHistoryClearMessage, count),
            L(LocKeys.SendHistoryClearConfirm),
            L(LocKeys.CommonCancel));

        if (!confirmed) return;

        try
        {
            SendPanel.ConfirmClearHistory();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Clearing the send history failed");
            await Context.DialogService.ShowMessageAsync(
                L(LocKeys.SendHistoryClearTitle), L(LocKeys.SendHistoryClearFailed));
        }
    }

    public async ValueTask DisposeAsync()
    {
        _uiPump.Stop();
        _uiPump.Dispose();

        // T-07: stop polling the control lines before the capture goes away. Its timer reads
        // through the session, so a tick landing after DisposeAsync below would be reading a
        // disposed port -- survivable (ReadControlLines never throws) but pointless work on a
        // closing window.
        //
        // ⛔ This line also carries a second job that is NOT visible from here (P2-86,
        // 2026-08-08). SignalPanelViewModel.Push writes through the session with
        // `_ = SetOutputLineAsync(...)` and discards the task. SetOutputLineAsync throws
        // ObjectDisposedException once the port is gone, and a throw on a discarded task is
        // dropped by .NET -- no wire change, no log entry, nothing. What stops that today is
        // purely the order of these two statements: disposing the panel first sets its
        // _disposed flag, so Push returns before it can reach a dead port.
        //
        // ⚠️ Moving SignalPanel?.Dispose() below `await _capture.DisposeAsync()` opens that
        // hole, and nothing here would fail: no test, no warning, no log. The reasoning that
        // depends on this ordering is written out on SignalPanelViewModel.Push -- change one,
        // read the other.
        SignalPanel?.Dispose();

        // Timed send must not outlive the session: one of its three mandatory stops
        // (01-spec 4.14, promise 7). The other two are handled where they happen --
        // disconnect in OnStateChanged, send failure in OnSendRequested.
        SendPanel.StopTimedSend();

        // ⭐ P2-29: the capture is disposed BEFORE the unsubscribe loop, and the order is the
        // whole fix. Disposing it runs StopAsync -> EmitFrames(FlushRemaining()) -- the frame
        // still sitting in the splitter when the user hit close. Unsubscribing first meant
        // that final frame was raised to nobody: it never reached OnFrameCaptured, so it never
        // reached the recorder either, and the batch on disk was silently short by one frame.
        // The comment on FlushRemaining says it exists "to avoid silently losing data", which
        // is exactly what the ordering undid.
        //
        // ⚠️ It only has to beat the unsubscribe, not the UI pump: OnFrameCaptured just
        // enqueues into _pending and feeds the recorder -- it never touches the pump. Frames
        // arriving now are written to the database and simply never drawn, which is correct
        // for a window that is closing.
        await _capture.DisposeAsync();

        // Every subscription registered its own removal at the point it was made (see
        // Subscribe), derived classes included. Deliberately after StopTimedSend above:
        // stopping raises PropertyChanged, and the preference hook still has to see it.
        foreach (var unsubscribe in _unsubscribe) unsubscribe();
        _unsubscribe.Clear();

        // 退订语言变更事件，避免会话关闭后仍被本地化服务持有。
        Dispose();

        // Last, so the frames flushed above are still in its queue when it drains
        // (StopAsync completes the writer and commits the remainder rather than cancelling).
        await _recorder.DisposeAsync();
    }
}
