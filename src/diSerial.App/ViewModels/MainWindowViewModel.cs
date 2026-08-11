using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiSerial.App.Localization;
using DiSerial.App.Services;
using DiSerial.App.ViewModels.Dialogs;
using DiSerial.App.ViewModels.Sessions;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;

namespace DiSerial.App.ViewModels;

/// <summary>
/// 主窗口 ViewModel：承载菜单、语言选择、会话集合与 diDatatracker 插入提示。
///
/// ⚠️ V1.0 的 UI 只显示一个会话（不做标签页与会话侧边栏），
/// 但此处已使用 ObservableCollection&lt;SessionViewModel&gt; 承载。
/// V1.1 加标签页时只需把 ContentControl 换成 TabControl 并绑定
/// Sessions / ActiveSession，<b>不涉及任何架构级重构</b>。
/// </summary>
public sealed partial class MainWindowViewModel : LocalizedViewModelBase, IAsyncDisposable
{
    private readonly IDialogService _dialogService;
    private readonly ISessionViewModelFactory _sessionFactory;
    private readonly IDeviceWatcher _deviceWatcher;
    private readonly ISerialPortFactory _serialPortFactory;
    private readonly IAppSettings _settings;
    private readonly Func<NewSessionDialogViewModel> _newSessionDialogFactory;

    public MainWindowViewModel(
        IDialogService dialogService,
        ISessionViewModelFactory sessionFactory,
        IDeviceWatcher deviceWatcher,
        ISerialPortFactory serialPortFactory,
        ILocalizationService localization,
        IAppSettings settings,
        ISessionTypeCatalog sessionTypes,
        Func<NewSessionDialogViewModel> newSessionDialogFactory,
        DeveloperModeState developerMode)
        : base(localization)
    {
        IsDeveloperMode = developerMode.IsEnabled;
        _dialogService = dialogService;
        _sessionFactory = sessionFactory;
        _deviceWatcher = deviceWatcher;
        _serialPortFactory = serialPortFactory;
        _settings = settings;
        _newSessionDialogFactory = newSessionDialogFactory;

        SessionTypes = sessionTypes.CreateItems();

        // The first entry is the default, so a single serial port stays the shortest path --
        // the same rule the dialog's own step 1 follows.
        SelectedSessionType = SessionTypes[0];

        LanguageMenuItems = localization.AvailableLanguages
            .Select(o => new LanguageMenuItemViewModel(o))
            .ToArray();

        SelectedLanguage = localization.AvailableLanguages
            .FirstOrDefault(l => l.Culture.Name == localization.CurrentCulture.Name)
            ?? localization.AvailableLanguages[0];

        SyncLanguageSelection();
    }

    public ObservableCollection<SessionViewModel> Sessions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveSession))]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(PauseMenuText))]
    [NotifyCanExecuteChangedFor(nameof(CloseSessionCommand))]
    [NotifyCanExecuteChangedFor(nameof(NewSessionCommand))]
    private SessionViewModel? _activeSession;

    public bool HasActiveSession => ActiveSession is not null;

    /// <summary>
    /// One session at a time (C-01a / P2-61). The two commands are deliberately driven by the
    /// <b>same</b> property in opposite directions: exactly one of "new" and "close" is ever
    /// available, so there is no state where both look possible or neither does.
    /// </summary>
    private bool CanCreateSession => !HasActiveSession;

    /// <summary>
    /// The session types offered on the empty state (2026-08-03, user decision).
    ///
    /// <para>⭐ <b>Why the choice is here and not only in the dialog.</b> The empty state used to
    /// be a paragraph of prose plus one button, and that paragraph explained the two session
    /// types in words -- the same thing the dialog's first step says with cards. Offering the
    /// cards here lets the dialog open straight on step 2, and removes a duplicated explanation
    /// that <c>Empty.Description</c>'s own comment had to warn against contradicting.</para>
    ///
    /// <para>⚠ <b>This is a pre-selection, not a replacement for step 1.</b> Step 1 still exists
    /// and is still reachable: the File menu opens the dialog with no session on screen at all,
    /// and "Back" on step 2 goes to it.</para>
    ///
    /// <para>⛔ <b>This list must never name a concrete type</b>, here or in the .axaml -- it
    /// comes from <see cref="ISessionTypeCatalog"/> exactly like the dialog's does.
    /// <c>NewSessionDialogDecouplingTests</c> scans this file for that.</para>
    /// </summary>
    public IReadOnlyList<SessionTypeItem> SessionTypes { get; }

    /// <summary>The card chosen on the empty state; handed to the dialog as its starting type.</summary>
    [ObservableProperty]
    private SessionTypeItem? _selectedSessionType;

    /// <summary>
    /// 当前会话的错误提示（P0-2 / 01-spec 4.7）。
    ///
    /// ⚠️ <b>刻意在这里转发一层，而不是让 XAML 直接绑 <c>ActiveSession.ErrorNotice</c></b>：
    /// 空状态下 <c>ActiveSession</c> 为 null，穿透绑定会各产生一条
    /// <c>Avalonia.Binding</c> 警告 —— 那正是 P1-20 记着的那 7 条噪音。
    /// 这个日志通道的价值在于「有 Warning 就意味着有问题」，不该再往里加。
    /// </summary>
    public string? SessionError => ActiveSession?.ErrorNotice;

    public bool HasSessionError => ActiveSession?.HasErrorNotice ?? false;

    [RelayCommand(CanExecute = nameof(HasSessionError))]
    private void DismissSessionError() => ActiveSession?.DismissErrorCommand.Execute(null);

    /// <summary>切换会话时把订阅搬过去，避免旧会话继续驱动界面。</summary>
    partial void OnActiveSessionChanging(SessionViewModel? oldValue, SessionViewModel? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= OnActiveSessionPropertyChanged;
        if (newValue is not null) newValue.PropertyChanged += OnActiveSessionPropertyChanged;
    }

    partial void OnActiveSessionChanged(SessionViewModel? value) => RaiseSessionErrorChanged();

    private void OnActiveSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // null / 空字符串是「所有属性均已变更」（语言切换时基类会发这个），必须一并接住 ——
        // 否则切语言后提示条的文字不会跟着翻译。
        var everything = e.PropertyName is null or "";

        if (everything || e.PropertyName is nameof(SessionViewModel.ErrorNotice)
                                         or nameof(SessionViewModel.HasErrorNotice))
        {
            RaiseSessionErrorChanged();
        }

        // ⭐ 会话标题变了 —— 窗口标题是**读穿**它的（见 Title 的 getter），
        // 而 [NotifyPropertyChangedFor(nameof(Title))] 只在 ActiveSession
        // **这个引用本身**换掉时才触发，接不住「同一个会话改了自己的标题」。
        //
        // ⚠️ 促成它的事实（P1-42，2026-08-01）：改通道别名之后帧行、状态栏、
        // 侧栏全变了，而标题栏仍是旧名字 —— 直到切一次会话才正。
        // 改名在 [01-spec 4.13] 之后是常规流程，这条从边角情形变成了天天遇得到。
        if (everything || e.PropertyName == nameof(SessionViewModel.Title))
        {
            OnPropertyChanged(nameof(Title));
        }

        // ⭐ 同一个理由用在暂停上（P2-99）：菜单项的标签也是**读穿**当前会话的，
        // 而「暂停 / 恢复」的切换发生在会话自己身上，不换 ActiveSession 引用。
        if (everything || e.PropertyName == nameof(SessionViewModel.PauseMenuText))
        {
            OnPropertyChanged(nameof(PauseMenuText));
        }
    }

    private void RaiseSessionErrorChanged()
    {
        OnPropertyChanged(nameof(SessionError));
        OnPropertyChanged(nameof(HasSessionError));
        DismissSessionErrorCommand.NotifyCanExecuteChanged();
    }

    /// <summary>可选语言列表（C-25）。名称以各语言自身书写，不参与翻译。</summary>
    public IReadOnlyList<LanguageMenuItemViewModel> LanguageMenuItems { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LanguageMenuHeader))]
    private LanguageOption _selectedLanguage;

    /// <summary>
    /// Language menu title, e.g. <c>Language (English)</c> / <c>语言：简体中文</c>.
    ///
    /// <para>The current language is shown on the menu bar itself rather than only as a check
    /// mark once the menu is open: if a user switches to a language they cannot read, this name
    /// is their main way back.</para>
    ///
    /// <para>Punctuation is each language's own choice, which is why the resource is a format
    /// string rather than "Language" plus a fixed bracket.</para>
    ///
    /// <para>⚠️ The Chinese one used a colon because its mnemonic already occupied a pair of
    /// brackets (<c>语言(_L)：{0}</c>) and nesting would have produced <c>语言(L)(简体中文)</c>.
    /// <b>That reason expired on 2026-08-02</b>, when the user had the mnemonic brackets removed
    /// from every Chinese menu title so both languages read the same. The colon simply stayed --
    /// switching it to brackets now would be a second visible change nobody asked for.</para>
    /// </summary>
    public string LanguageMenuHeader => LF(LocKeys.MenuLanguage, SelectedLanguage.NativeName);

    /// <summary>
    /// 平台限制提示。
    /// 文本由 App 层根据 Core 返回的状态码映射，Core 本身不产出本地化字符串。
    /// </summary>
    public string? PlatformWarning =>
        PlatformStatusPresenter.Describe(_serialPortFactory.SupportStatus, Localization);

    public bool HasPlatformWarning => PlatformWarning is not null;

    // ⚠️ 原先这里有一组「双通道设备接入提示」成员（原 M-02）——
    // DetectedPair / HasDetectionNotice / DetectionHint 与两个命令。
    // 2026-07-29 整组移除，理由见 IDeviceWatcher 的注释。

    /// <summary>
    /// 窗口标题。开发模式下追加醒目后缀。
    ///
    /// ⚠️ <b>标识刻意放在标题栏，不是状态栏或某个角落。</b>
    /// 它防的不是「用户误用」（用户根本接触不到开发模式），
    /// 而是<b>合成数据被当成真实总线数据</b>：本项目已确认有效的协作方式是
    /// 「启动程序 → 截图 → 分析截图」，已修的缺陷里有三个是这么发现的。
    /// 标题栏后缀保证出现在每一张截图里，状态栏则可能被裁掉。
    /// </summary>
    public string Title
    {
        get
        {
            var title = ActiveSession is null
                ? L(LocKeys.AppName)
                : LF(LocKeys.AppTitleWithSession, ActiveSession.Title);

            return IsDeveloperMode ? LF(LocKeys.AppTitleDeveloperMode, title) : title;
        }
    }

    /// <summary>
    /// 「会话 → 暂停接收」那一项的标签（P2-99）。⭐ **它在这里而不是在会话上，只为了一件事：
    /// 没有会话时也要有字。** 菜单的 <c>DataContext</c> 是 <c>ActiveSession</c>，
    /// ⛔ 直接绑会话的属性在无会话时会渲染成**空标签** —— 而那几项此时是禁用但仍然显示的。
    ///
    /// <para>⚠️ 无会话时给的是「暂停」那一侧的文案：此时既没有在收也没有暂停，
    /// 而菜单项是禁用的，**说什么都不会被点到**；给未暂停态才不会让人以为「有东西正暂停着」。</para>
    /// </summary>
    public string PauseMenuText =>
        ActiveSession?.PauseMenuText ?? L(LocKeys.MenuSessionPause);

    /// <summary>是否有任何开发者开关处于开启状态。</summary>
    public bool IsDeveloperMode { get; }

    // ⚠️ 这里曾有一个 StatusText（P2-16 a，2026-07-31 删）。它零消费者 ——
    // 状态栏 MainWindow.axaml:110 绑的一直是 ActiveSession.StatusText，
    // 空状态那句则由同文件 113 行的 loc:Translate Status.Ready 直接给。
    // 随它一并删掉的是 LocKeys.StatusReady：本类按需列键（40/137），
    // 而那个常量的唯一存在理由就是这个属性。资源键 Status.Ready 仍在用（XAML 侧）。

    /// <summary>
    /// 启动端口监视。它的产物（<c>PortsChanged</c>）由**新建会话对话框**消费 ——
    /// 对话框开着时插拔串口，端口下拉即时跟着变。
    ///
    /// ⚠️ 原先这里还会在 <c>debugMode</c> 下伪造一次双通道接入事件，
    /// 以便无硬件时验证「插入提示 → 一键建会话」那条 UI 路径。
    /// 插入提示整个移除后（2026-07-29），触发器与 <c>IDeviceSimulator</c> 一并删掉。
    ///
    /// ⚠️ <b>合成端口 SIM-A / SIM-B 也已于 2026-07-30 删除</b>（随监听桩退休）。
    /// 无硬件时要建监听会话，把 <c>dev.json</c> 的 <c>replay</c> 设为 <c>on</c>，
    /// 用两个 <c>REPLAY-*</c> 端口 —— 它们走真实采集链路，比桩更可信。
    /// </summary>
    public async Task InitializeAsync() => await _deviceWatcher.StartAsync();

    /// <summary>切换界面语言并记住选择（C-25）。</summary>
    [RelayCommand]
    private void SetLanguage(LanguageOption? option)
    {
        if (option is null) return;

        // 顺序要紧：必须先更新 SelectedLanguage，再切换语言。
        // SetLanguage 会同步触发 CultureChanged，基类随即刷新全部计算属性；
        // 若此时 SelectedLanguage 仍是旧值，LanguageMenuHeader 会拿到
        // 「新语言的格式串 + 旧语言的名字」，显示成「语言(L)：English」。
        SelectedLanguage = option;
        SyncLanguageSelection();
        Localization.SetLanguage(option.Culture);
        _settings.Language = option.Culture.Name;
    }

    /// <summary>
    /// Language switch: the base class re-evaluates this ViewModel's own computed properties,
    /// but the session-type cards are separate objects it knows nothing about.
    ///
    /// <para>⚠ <b>Unlike the dialog's list, this one is long-lived</b> -- built once in the
    /// constructor and kept for the life of the window -- so it is the one that would otherwise
    /// sit in the previous language after a switch. The dialog rebuilds its list on every open
    /// and needs nothing here.</para>
    /// </summary>
    protected override void OnCultureChanged()
    {
        base.OnCultureChanged();

        foreach (var type in SessionTypes) type.RefreshText();
    }

    /// <summary>更新菜单项的勾选状态，使当前语言可见。</summary>
    private void SyncLanguageSelection()
    {
        foreach (var item in LanguageMenuItems)
        {
            item.IsSelected = item.Option.Culture.Name == SelectedLanguage.Culture.Name;
        }
    }

    /// <summary>
    /// Opens the new-session dialog. <paramref name="startAt"/> non-null means "the type is
    /// already chosen, go straight to configuring it"; null means "start by asking".
    ///
    /// <para>⛔ <b>Unavailable while a session is open</b> (P2-61, 2026-08-04 user decision):
    /// C-01a says one session at a time, and until this guard existed the code only honoured
    /// the <i>visible</i> half of that — a second session was created and the first one stayed
    /// live, holding its port and still capturing, with nothing on screen saying so.</para>
    ///
    /// <para>⭐ <b>Closing first is not a dead end</b>, and that was the thing to check before
    /// disabling this: closing returns to the empty state, which carries the session-type cards
    /// (2026-08-03), so "close → pick a type → new session" is a complete path. Had the empty
    /// state still been the old paragraph-and-one-button, this guard would have sealed off the
    /// only way to switch session types.</para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCreateSession))]
    private async Task NewSessionAsync(SessionTypeItem? startAt)
    {
        // ⛔ Not redundant with CanExecute. `IRelayCommand.Execute` does not consult
        // `CanExecute`, so the greyed-out menu item is a UI-layer courtesy, not an invariant.
        // C-01a claims "one session at a time" about the application, not about the File menu,
        // and a claim that only one call site honours is the shape this project keeps getting
        // caught by. Any future entry point gets the rule for free.
        if (HasActiveSession) return;

        // using：对话框 ViewModel 继承 LocalizedViewModelBase，构造时订阅了
        // CultureChanged。不释放的话，每开一次对话框就多一个永不退订的订阅者 ——
        // 正是那个基类的注释在极力避免的事（P0-5 的第二处）。
        using var dialog = _newSessionDialogFactory();

        // ⭐ 谁按的，决定从第几步开始（2026-08-03 用户定）：
        //
        //   空状态的按钮 —— 类型**就在屏幕上刚选过**，传进去，直接开第二步。
        //   「文件」菜单   —— 屏幕上**没有**类型选择器（它随会话打开而隐藏），
        //                    传 null，从第一步开始。
        //
        // ⛔ 别把它简化成「总是传 SelectedSessionType」：那样会话开着时菜单也跳到
        //    第二步，而空状态不在屏上 —— 用户**再也换不了会话类型**（开着终端就建不了监听）。
        //    「上一步」删掉之后这条路就彻底断了，两件事必须一起看。
        //
        // ⚠️ 传的是**这一份**列表里的对象，而对话框有它自己的一份，
        //    所以由对话框按类型认领对应项，不能直接拿去用。
        dialog.PreselectType(startAt);

        await dialog.LoadAsync();
        await OpenSessionFromDialogAsync(dialog);
    }

    /// <summary>
    /// Closes the current session, asking first if a recording is running.
    ///
    /// <para>⭐ <b>Only recording gets a prompt</b> (P2-61, 2026-08-04 user decision). The frames
    /// on screen are already understood to be transient — Clear wipes them, and nothing ever
    /// promised they would last. A recording is the opposite: it is on disk and it produces an
    /// export file, so ending it is a real outcome the user is entitled to be told about.
    /// Prompting for everything would put a dialog in front of the ordinary case and train
    /// people to click through it, which is how the prompt that matters gets ignored too.</para>
    ///
    /// <para>⚠️ <b>Declining leaves everything exactly as it was.</b> The session is not
    /// half-closed and the recording keeps running — there is no state between the two.</para>
    ///
    /// <para>⛔ <b>Closing does not export, and the prompt has to say so.</b> The export runs in
    /// <c>ToggleRecordAsync</c>'s stop branch only; <c>DisposeAsync</c> just disposes the
    /// recorder, so the batch is left in the database with no file and no screen in V1.0 that
    /// can reach it. The first draft of this prompt claimed an export file gets written — that
    /// is the P2-58 shape (wording promising more than the code delivers), authored on the same
    /// day that shape was written up. <b>If closing is ever made to export, this string changes
    /// with it.</b></para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasActiveSession))]
    private async Task CloseSessionAsync()
    {
        if (ActiveSession is not { } session) return;

        if (session.IsRecording)
        {
            var confirmed = await _dialogService.ShowConfirmationAsync(
                L(LocKeys.CloseWhileRecordingTitle),
                L(LocKeys.CloseWhileRecordingMessage),
                L(LocKeys.CloseWhileRecordingConfirm),
                L(LocKeys.CommonCancel));

            if (!confirmed) return;
        }

        Sessions.Remove(session);
        await session.DisposeAsync();

        // ⚠️ Not LastOrDefault() any more (P2-61): with one session at a time this is always
        // null, and the old fallback was how an invisible leftover session used to surface --
        // close the visible one and a session the user believed was gone came back.
        ActiveSession = null;
    }

    /// <summary>
    /// ⚠️ <c>LF</c>, not <c>L</c>: the version is a placeholder now (P1-16, 2026-08-02).
    /// It used to be spelled out inside the translated sentence, i.e. one fact stored twice and
    /// updatable in either language alone. <c>AppInfo.DisplayVersion</c> reads it off the
    /// assembly, whose single source is <c>Directory.Build.props</c>.
    /// </summary>
    [RelayCommand]
    private async Task ShowAboutAsync() =>
        await _dialogService.ShowMessageAsync(
            L(LocKeys.AboutTitle),
            LF(LocKeys.AboutMessage, AppInfo.DisplayVersion));

    private async Task OpenSessionFromDialogAsync(NewSessionDialogViewModel dialog)
    {
        var result = await _dialogService.ShowNewSessionDialogAsync(dialog);
        if (result is null) return;

        // ⚠️ 会话是在对话框里建好并**已经连上**的 —— 端口就是在那里打开的。
        //
        // 这样安排是因为「打不开端口」的提示要出现在对话框内、对话框保持打开
        // （01-spec 4.7）。若在这里才连接，那时会话界面已经出现了，
        // 就只能退回顶部提示条那种呈现。
        //
        // 所以这里**不再调 ConnectCommand** —— 那条路径现在只服务「已有会话的重连」。
        // Result 非 null 就意味着打开已经成功，CreatedSession 一定非空。
        if (dialog.CreatedSession is not { } session) return;

        Sessions.Add(session);
        ActiveSession = session;
    }

    /// <summary>
    /// ⭐ <b>Idempotent, and it has to be</b> (P2-30).
    ///
    /// <para>The shutdown path disposes this object <b>twice by construction</b>:
    /// <c>App.OnShutdownRequested</c> calls it explicitly so sessions are torn down in a known
    /// order, and then disposes the container — which owns this same instance as a singleton and
    /// disposes it again. ⛔ <b>Without the guard the second pass re-enters the whole teardown</b>,
    /// and an exception there is not merely untidy: <c>ServiceProvider</c> stops disposing the
    /// remaining singletons, and <c>StoredAppSettings</c> — the one that flushes user settings to
    /// disk — is among them. That is the failure mode <c>CompositionRootTests</c> was written
    /// for, arriving by a route it did not cover.</para>
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        // 错误提示条的转发订阅同样要退掉，否则会话释放后仍被主窗口持有。
        if (ActiveSession is { } active) active.PropertyChanged -= OnActiveSessionPropertyChanged;

        Dispose();

        foreach (var session in Sessions.ToArray())
        {
            await session.DisposeAsync();
        }
        Sessions.Clear();

        // ⭐ Stopped, not disposed -- P2-30's other half.
        //
        // This view model STARTS the watcher (InitializeAsync) but does not OWN it: it is an
        // AddSingleton registration, so the container constructs it and the container disposes
        // it. Calling DisposeAsync here made that three disposals for one object (this method
        // ran twice, plus the container's own).
        //
        // ⚠️ The rule worth keeping is the general one: START/STOP is a usage pair, and
        // CONSTRUCT/DISPOSE is an ownership pair. Whoever started it should stop it; only the
        // owner disposes it.
        await _deviceWatcher.StopAsync();
    }

    /// <summary>0 until <see cref="DisposeAsync"/> has been entered. See there for why.</summary>
    private int _disposed;
}
