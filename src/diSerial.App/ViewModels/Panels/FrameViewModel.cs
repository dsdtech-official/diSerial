using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;

namespace DiSerial.App.ViewModels.Panels;

/// <summary>
/// 显示区中的一行。持有原始 SerialFrame，按当前显示设置渲染出各列文本。
///
/// 不使用 [ObservableProperty]：本类实例数量极大（高波特率下每秒数百个），
/// 刻意保持轻量。显示设置变化时由 LogPanelViewModel 统一调用 Refresh()。
/// </summary>
public sealed class FrameViewModel : ViewModelBase
{
    private readonly IFrameFormatter _formatter;

    public FrameViewModel(SerialFrame frame, IFrameFormatter formatter,
        DisplayFormat displayFormat, TimestampMode timestampMode, string? channelLabel = null,
        int hexBytesPerLine = 16)
    {
        Frame = frame;
        _formatter = formatter;

        // ⚠️ 标签必须在 Refresh 之前赋值。
        // 早期写法是构造后用对象初始化器设它 —— 而 C# 的对象初始化器
        // 在构造函数<b>之后</b>执行，于是 Refresh 里算 ChannelText 时标签还是 null，
        // 每一行出生时都缺标签，只有事后被再次 Refresh 才补上。
        // 所以这里只能走构造函数参数，不要改回属性初始化器。
        ChannelLabel = channelLabel;

        Refresh(displayFormat, timestampMode, hexBytesPerLine);
    }

    public SerialFrame Frame { get; }

    public long Sequence => Frame.Sequence;

    public ChannelId Channel => Frame.Channel;

    /// <summary>是否为协议异常帧，供视图着色（红底）。</summary>
    public bool IsError => Frame.Flags != FrameFlags.None;

    /// <summary>
    /// 是否为本机发出的帧 —— 终端会话的本地回显（T-02），监听会话的注入（M-09）。
    /// 供 <see cref="LogPanelViewModel.ShowSentData"/> 过滤显示（T-05）。
    /// </summary>
    public bool IsTx => Frame.Direction == FrameDirection.Tx;

    /// <summary>
    /// 本行的着色依据（T-04）。**颜色的含义恒为「谁在说话」**：
    ///
    /// <list type="bullet">
    ///   <item>监听会话有两个通道，说话人就是 A / B → 蓝 / 绿，**与本改动无关，不变**</item>
    ///   <item>终端会话只有一个端口，说话人只有两个：**对端**与**我** → RX 默认色 / TX 紫</item>
    /// </list>
    ///
    /// ⚠️ <b>为什么不直接在视图里绑 <see cref="Channel"/> 加个 <c>Direction</c> 判断</b>：
    /// 那会让「什么情况下用哪种颜色」这条规则散在 XAML 里。放在这里，
    /// <c>FrameAccentToBrushConverter</c> 只需做一次枚举到画刷的映射。
    /// </summary>
    public FrameAccent Accent => Frame.Channel switch
    {
        ChannelId.A => FrameAccent.ChannelA,
        ChannelId.B => FrameAccent.ChannelB,
        _ => IsTx ? FrameAccent.Tx : FrameAccent.Rx
    };

    public string TimestampText { get; private set; } = string.Empty;

    /// <summary>与上一帧的时间差（毫秒），监听会话中即响应延迟的观测值。</summary>
    public string DeltaText { get; private set; } = string.Empty;

    /// <summary>
    /// 通道或方向标签。
    ///
    /// <list type="table">
    ///   <item><term><c>COM6 · PLC →</c></term><description>监听会话，**总线上观测到的**</description></item>
    ///   <item><term><c>TX → COM6 · PLC</c></term><description>监听会话，**我们自己注入的**（M-09）</description></item>
    ///   <item><term><c>← RX</c> / <c>→ TX</c></term><description>终端会话（无通道概念）</description></item>
    /// </list>
    ///
    /// ⚠️ <b>2026-08-01 起前半段是端口名而不是 <c>A</c> / <c>B</c></b>（01-spec 4.13）——
    /// 交换 A/B 删掉之后那两个字母不再指向任何用户关心的东西。
    /// 标签由 <c>ChannelViewModel.InlineLabel</c> 组好后传进来，本类不参与组装。
    ///
    /// ⚠️ <b>监听会话下必须区分注入与观测</b>（P1-32，2026-07-31 升 P0 后修）。
    /// 原实现的 A / B 两个分支**完全不看 <c>Frame.Direction</c>**，
    /// 于是自己注入的帧和总线上观测到的帧**长得一模一样**。
    ///
    /// <b>最讽刺的是</b>：<c>MonitorCaptureSession.SendAsync</c> 里那段本地回显的注释写着
    /// 「注入的内容与观测到的内容进入同一条时间轴，**否则用户看不出「刚才那一帧是我自己注入的」**」——
    /// **底层把 <c>Direction</c> 正确地设成了 <c>Tx</c>，渲染层把它丢了**，
    /// 于是本地回显精确地达成了它想避免的那件事。
    ///
    /// <b>为什么把 TX 放在前面而不是加个后缀</b>：注入帧必须**一眼认出来**。
    /// 前缀会打断 A / B 的对齐，那正是想要的效果；而箭头方向从此也有了含义 ——
    /// 观测是「从通道来」，注入是「往通道去」。
    /// </summary>
    public string ChannelText { get; private set; } = string.Empty;

    public string DataText { get; private set; } = string.Empty;

    /// <summary>
    /// 解码摘要的当前语言文本。
    /// 帧上带的只是资源键，落地由 IFrameFormatter 内部的解析器完成 ——
    /// 因此本类不必自己持有本地化服务。
    /// </summary>
    public string? DecodedSummary { get; private set; }

    /// <summary>
    /// 监听会话中由 <c>ChannelViewModel.InlineLabel</c> 提供的通道标签
    /// （<c>COM6</c> 或 <c>COM6 · PLC</c>），用于渲染通道列。终端会话为 null。
    ///
    /// ⚠️ 只能经构造函数或 <see cref="Relabel"/> 赋值，见构造函数中的说明。
    /// </summary>
    public string? ChannelLabel { get; private set; }

    /// <summary>
    /// 改名之后重贴标签（P1-41）。
    ///
    /// <b>为什么需要它</b>：标签在<b>入队那一刻</b>就写进了行里，之后不再回查。
    /// 而[01-spec 4.13] 的立论是「改名只是补标签，端口↔别名的绑定从没变过，
    /// 所以追溯生效才对」—— 导出照这条做了，显示区不跟着改就会出现
    /// <b>上半屏 <c>COM6</c>、下半屏 <c>COM6 · PLC</c></b>，
    /// 而导出文件里整批是同一个名字。<b>同一批帧，界面与文件两个答案。</b>
    ///
    /// ⚠️ <b>由 <c>LogPanelViewModel.RelabelChannel</c> 成批调用</b>，
    /// 不要让每一行自己去订阅别名变更 —— 显示区上限 500 行，
    /// 那会是 500 个事件订阅（与 <c>LocalizedViewModelBase</c> 刻意不下放到
    /// <c>FrameViewModel</c> 是同一个理由）。
    /// </summary>
    public void Relabel(string? channelLabel)
    {
        if (string.Equals(ChannelLabel, channelLabel, StringComparison.Ordinal)) return;

        ChannelLabel = channelLabel;
        ChannelText = BuildChannelText();
        OnPropertyChanged(nameof(ChannelText));
    }

    public void Refresh(DisplayFormat displayFormat, TimestampMode timestampMode,
        int hexBytesPerLine = 16)
    {
        TimestampText = _formatter.FormatTimestamp(Frame, timestampMode);
        DecodedSummary = _formatter.FormatDecodedSummary(Frame);
        // Invariant formatting - the UI language must not change how the decimal point is
        // written, or a German locale prints "4,1" and splits a TSV column in half.
        //
        // ⚠️ This used to call Infrastructure.Formatting.FrameFormatter.FormatMilliseconds by
        // fully-qualified name: a real breach of "App references Infrastructure only from
        // Composition/", and one that no `using` grep could ever have found (P1-47). The rule
        // now lives in Core, so the display layer and the formatter share one source without
        // the display layer knowing an implementation exists.
        DeltaText = Frame.Delta is { } d ? DurationText.Milliseconds(d) : "—";
        DataText = _formatter.FormatData(Frame.Data, displayFormat, hexBytesPerLine);
        ChannelText = BuildChannelText();

        OnPropertyChanged(nameof(TimestampText));
        OnPropertyChanged(nameof(DecodedSummary));
        OnPropertyChanged(nameof(DeltaText));
        OnPropertyChanged(nameof(DataText));
        OnPropertyChanged(nameof(ChannelText));
    }

    /// <summary>
    /// 通道列文本。<b>监听会话</b>用传进来的通道标签，<b>终端会话</b>没有通道、走方向标记。
    ///
    /// ⚠️ <b>监听会话下必须区分注入与观测</b>（P1-32）：<c>TX →</c> 前缀刻意放在最前，
    /// 让注入帧<b>一眼认出来</b> —— 前缀会打断端口名的对齐，那正是想要的效果。
    /// 而箭头方向从此也有含义：观测是「从通道来」，注入是「往通道去」。
    /// </summary>
    /// <remarks>
    /// ⛔ <b>The four strings used to be literals right here</b> (P2-69). They went to
    /// <c>Strings.resx</c> on 2026-08-05 and the branching moved to
    /// <see cref="IFrameFormatter.FormatChannelText"/> -- see that member for why the formatter
    /// is the right home and why this class still holds no localization service.
    /// </remarks>
    private string BuildChannelText() => _formatter.FormatChannelText(Frame, ChannelLabel);
}
