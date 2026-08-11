using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiSerial.App.Localization;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;

namespace DiSerial.App.ViewModels.Panels;

/// <summary>
/// 数据显示面板（C-05 / C-06 / C-08）。终端会话与监听会话共用，
/// 通过 ShowChannelColumn / ShowDeltaColumn 控制列的显隐。
///
/// ⚠️ 性能注意：当前用 ObservableCollection + ItemsControl 作为脚手架，
/// 便于快速验证布局。高波特率下必须替换为自绘控件
/// （override Render(DrawingContext) + 虚拟化），否则会卡死。
/// 替换时本 ViewModel 的公开接口不变。
///
/// 本类不继承 LocalizedViewModelBase —— 它自身不含可翻译文本，
/// 列头文案在 View 里通过 {loc:T} 取，枚举选项由 IEnumChoiceProvider 提供
/// 并自行响应语言切换。
/// </summary>
public sealed partial class LogPanelViewModel : ViewModelBase
{
    private readonly IFrameFormatter _formatter;

    /// <summary>
    /// How many frames the display keeps. Oldest are dropped past this.
    ///
    /// <para>⛔ <b>5000 -> 500 on 2026-08-11 (user decision), after a field report</b>: a monitor
    /// session left running went sluggish after about half an hour, and the machine with it.
    /// The arithmetic lines up — at the reported ~2.6 frames/s, 5000 frames is ~32 minutes.</para>
    ///
    /// <para>⭐ <b>The cap is not the real defect; it is the only cheap lever.</b> The display is
    /// still a non-virtualising <c>ItemsControl</c> (P1-6), so <b>every</b> retained frame
    /// materialises a control tree — roughly 12 controls per row. 5000 rows is ~60,000 live
    /// controls, all of them taking part in every layout pass. 500 rows is ~6,000.
    /// Fixing this properly means the self-drawn virtualising control in P1-6; until then,
    /// the number of retained rows IS the cost.</para>
    ///
    /// <para>⚠️ <b>This also narrows the toolbar Export (M-08)</b>, which snapshots exactly this
    /// buffer — it now tops out at 500 rows rather than 5000. That is not a side effect that
    /// slipped through; it was put to the user with the number before the change. Full history
    /// has always been Recording's job (C-09), which streams the whole batch from the database
    /// and is not affected by this constant.</para>
    ///
    /// <para>⭐ <b>The principle is already in the spec</b>, written for the pause case:
    /// "缓存要么无界（长时间暂停会把内存吃光），要么有界" — and full capture "是 C-09 记录的
    /// 职责，不该由显示缓冲兼任" (01-spec 4.9). Lowering this constant applies that same
    /// sentence to the buffer's size.</para>
    /// </summary>
    private const int MaxFrames = 500;

    public LogPanelViewModel(IFrameFormatter formatter, IEnumChoiceProvider enumChoices)
    {
        _formatter = formatter;
        DisplayFormatChoices = enumChoices.GetChoices<DisplayFormat>();
        TimestampModeChoices = enumChoices.GetChoices<TimestampMode>();
    }

    public ObservableCollection<FrameViewModel> Frames { get; } = [];

    /// <summary>本地化的枚举选项。ComboBox 用 SelectedValue 绑回下方的原始枚举属性。</summary>
    public IReadOnlyList<LocalizedEnumItem> DisplayFormatChoices { get; }

    public IReadOnlyList<LocalizedEnumItem> TimestampModeChoices { get; }

    [ObservableProperty]
    private DisplayFormat _displayFormat = DisplayFormat.HexAndAscii;

    [ObservableProperty]
    private TimestampMode _timestampMode = TimestampMode.Absolute;

    /// <summary>
    /// 暂停显示（C-08）。规格见 docs/01-spec.md 4.2a。
    ///
    /// <b>语义（2026-07-31 定案，别按字面直觉理解）</b>：
    /// 串口照常收、分帧照常做、<see cref="FrameCount"/> 与 <see cref="ByteCount"/> 照常走，
    /// 但**帧不进 <see cref="Frames"/>，也不缓存在任何地方** —— 暂停期间的帧**永久看不到**。
    ///
    /// ⚠️ <b>此前这里写的是「只是不刷新到界面」，那句话是错的</b>，
    /// 与规格当时的「后台继续缓存不丢数据」一起误导了三天（原 P2-10）。
    /// 已定案：**以「丢弃」为准**，理由是不引入一个长时间暂停会无界增长的缓冲区。
    ///
    /// 由 <c>PauseSemanticsTests</c> 钉住 —— 想改成「缓存 + 恢复回灌」的人会先撞红。
    /// </summary>
    [ObservableProperty]
    private bool _isPaused;

    /// <summary>
    /// 是否跟随末尾滚动（C-08）。<b>由 View 双向绑定</b>：用户手动上滚时
    /// AutoScrollViewer 会把 false 写回这里，滚回底部再置回 true；
    /// 从这里置 true 则显示区立即滚到末尾。
    /// </summary>
    [ObservableProperty]
    private bool _autoScroll = true;

    /// <summary>监听会话显示通道列，终端会话不显示。</summary>
    [ObservableProperty]
    private bool _showChannelColumn;

    /// <summary>显示与上一帧的时间差列。</summary>
    [ObservableProperty]
    private bool _showDeltaColumn;

    /// <summary>
    /// 是否显示本机发出的数据（T-05）。规格见 docs/01-spec.md 4.12。
    ///
    /// ⚠️ <b>它是「过滤」，不是「闸门」—— 这是与 <see cref="IsPaused"/> 最要紧的区别</b>：
    /// TX 帧照常进 <see cref="Frames"/>、照常计入 <see cref="FrameCount"/> 与
    /// <see cref="ByteCount"/>，只是**视图不画它们**。于是重新勾上时
    /// 之前的 TX 行**立刻全部回来**，而暂停期间的帧是**永久看不到**的。
    ///
    /// 过滤发生在视图层（<c>SentVisibilityConverter</c>），本类一行都不用改 ——
    /// 这正是「过滤」该有的代价：**数据侧什么都不做**。
    ///
    /// <b>导出不受它影响</b>（01-spec 4.12 + 6.5 a）：不能让用户拿到一份
    /// 自己不知道缺了什么的文件。
    /// </summary>
    [ObservableProperty]
    private bool _showSentData = true;

    [ObservableProperty]
    private int _frameCount;

    [ObservableProperty]
    private long _byteCount;

    /// <summary>由会话在收到帧时调用。必须已切换到 UI 线程。</summary>
    public void Append(SerialFrame frame, string? channelLabel = null)
    {
        AppendCore(frame, channelLabel);
        Trim();
    }

    /// <summary>
    /// 批量追加。会话层按固定帧率成批推送，而不是逐帧调用 —— 高波特率下
    /// 逐帧刷新会压垮 Dispatcher 队列，且每帧都触发一次集合变更通知。
    /// 成批处理后，裁剪也只需在批末做一次。
    /// </summary>
    public void AppendRange(IReadOnlyList<(SerialFrame Frame, string? Label)> batch)
    {
        if (batch.Count == 0) return;

        foreach (var (frame, label) in batch)
        {
            AppendCore(frame, label);
        }
        Trim();
    }

    /// <summary>
    /// 给某一路已在缓冲里的行重贴标签（P1-41）—— 用户改了通道别名时由会话调用。
    ///
    /// <b>为什么要成批做而不是让每行自己订阅</b>：显示区上限 500 行，
    /// 逐行订阅就是 500 个事件订阅，与 <c>LocalizedViewModelBase</c> 刻意不下放到
    /// <c>FrameViewModel</c> 是同一个理由。这里一次 O(n) 遍历，
    /// 而且只发生在<b>用户显式改名</b>这个低频动作上。
    ///
    /// ⚠️ 不改名的那一路不受影响 —— <see cref="FrameViewModel.Relabel"/> 内部
    /// 对相同标签直接返回，不会发多余的通知。
    /// </summary>
    public void RelabelChannel(ChannelId channel, string? channelLabel)
    {
        foreach (var frame in Frames)
        {
            if (frame.Channel == channel) frame.Relabel(channelLabel);
        }
    }

    private void AppendCore(SerialFrame frame, string? channelLabel)
    {
        // ⚠️ 顺序有意义：计数在前、暂停判断在后。
        // 计数记的是「收到了多少」（与显示无关），显示缓冲记的是「看得见什么」。
        // 于是暂停期间「帧数在涨、显示区不动」—— 那是设计，不是缺陷。
        FrameCount++;
        ByteCount += frame.Length;

        if (IsPaused) return;

        // 标签经构造函数传入，不能用对象初始化器 —— 后者在构造之后才执行，
        // 会让行在首次渲染时缺少标签。
        Frames.Add(new FrameViewModel(frame, _formatter, DisplayFormat, TimestampMode, channelLabel, HexBytesPerLine));
    }

    private void Trim()
    {
        while (Frames.Count > MaxFrames)
        {
            Frames.RemoveAt(0);
        }
    }

    [RelayCommand]
    private void Clear()
    {
        Frames.Clear();
        FrameCount = 0;
        ByteCount = 0;
    }

    [RelayCommand]
    private void TogglePause() => IsPaused = !IsPaused;

    partial void OnDisplayFormatChanged(DisplayFormat value) => RefreshAll();

    partial void OnTimestampModeChanged(TimestampMode value) => RefreshAll();

    /// <summary>
    /// 按当前显示设置重算所有行。
    ///
    /// 除显示格式/时间戳模式变化外，<b>语言切换也必须调用</b> ——
    /// 行内的解码摘要是在 Refresh 时解析并缓存的，不重算就会出现
    /// 「新帧中文、旧帧英文」的混排。
    /// 由 SessionViewModel 在收到语言变更时调用（复用它已有的那一个订阅，
    /// 不为每一行新增订阅 —— 显示区上限 500 行）。
    /// </summary>
    public void RefreshAll()
    {
        foreach (var frame in Frames)
        {
            frame.Refresh(DisplayFormat, TimestampMode, HexBytesPerLine);
        }
    }

    /// <summary>
    /// `HEX + ASCII` 下一行放几个字节（P2-100，2026-08-10 用户定「按可用宽度自动回流」）。
    ///
    /// <para>⛔ <b>缺陷长什么样</b>：换行原先是<u>烤在字符串里</u>的定值 16，
    /// 于是把窗口拉到全屏，数据列<b>确实变宽了</b>（它是 <c>*</c>），
    /// **而里面的文本一点没变** —— 右边空出一大片。</para>
    ///
    /// <para>⭐ <b>为什么是 8 的倍数</b>：hexdump 的可读性来自「同一列永远是同一个字节位」，
    /// 而 8 / 16 / 24 / 32 是这类工具的惯用刻度。⚠️ <b>更要紧的是它<u>压住了重排次数</u>**：
    /// 拖动窗口时按像素算会每帧都变，按 8 取整只在跨过刻度时变一次。</para>
    /// </summary>
    public int HexBytesPerLine { get; private set; } = 16;

    /// <summary>
    /// 显示区把「数据列现在放得下多少个等宽字符」告诉这里（由 View 用真实字体量出来，
    /// ⛔ <b>不在这里写死字体度量</b>）。
    ///
    /// <para>⭐ <b>一行的字符数是 <c>4N + 1</c></b>：每字节 hex 占 <c>"XX "</c> 三个字符，
    /// ASCII 侧栏再占一个，中间一个分隔空格。所以 <c>N = (chars - 1) / 4</c>。</para>
    ///
    /// <para>⛔ <b>算出来没变就<u>不重排</u></b> —— 这是本方法唯一的性能设计：
    /// ⭐ 实测 <b>5000 行</b>整表重排 <b>14.4 ms</b>，一次拖动跨不了几个刻度，
    /// ⚠️ <b>而若按像素重排，同样的 14.4 ms 会落在每一个 resize 事件上</b>。</para>
    ///
    /// <para>⚠️ <b>这里的 5000 是<u>当时量的行数</u>，不是现在的上限</b> ——
    /// <see cref="MaxFrames"/> 2026-08-11 已降到 500，⛔ <b>而这一句不跟着改</b>：
    /// <b>它是一次测量，不是一个配置值。</b>⭐ 把测量条件改成今天的数字，
    /// 等于把一个没做过的实验写成做过的。**按比例推算 500 行约 1.4 ms，
    /// 而那是推算，没量过。**</para>
    /// </summary>
    public void SetDataColumnCharacters(int characters)
    {
        var fit = (characters - 1) / 4;

        // 夹紧再取整到 8 的倍数。⛔ 下限 8 不是审美：窗口很窄时 fit 会算成 0 甚至负数，
        // 而 0 会让格式化器那个 for 永远不前进（它自己也夹了一道，这里是第一道）。
        var quantised = Math.Max(8, fit / 8 * 8);

        if (quantised == HexBytesPerLine) return;

        HexBytesPerLine = quantised;
        OnPropertyChanged(nameof(HexBytesPerLine));
        RefreshAll();
    }
}
