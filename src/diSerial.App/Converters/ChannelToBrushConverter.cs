using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DiSerial.App.ViewModels.Panels;
using DiSerial.App.ViewModels.Sessions;
using DiSerial.Core.Models;

namespace DiSerial.App.Converters;

/// <summary>
/// 通道 → 颜色。合并时间轴用颜色区分数据来源，
/// 这是「一眼看出谁在说话」的关键，也是合并视图相对两个独立终端的核心价值。
///
/// V1.0 配色固定（A 蓝 / B 绿）；自定义配色排期在 V1.1。
/// </summary>
public sealed class ChannelToBrushConverter : IValueConverter
{
    private static readonly IBrush BrushA = SolidColorBrush.Parse(ChannelViewModel.ColorA);
    private static readonly IBrush BrushB = SolidColorBrush.Parse(ChannelViewModel.ColorB);
    private static readonly IBrush BrushNeutral = SolidColorBrush.Parse("#888780");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ChannelId channel
            ? channel switch
            {
                ChannelId.A => BrushA,
                ChannelId.B => BrushB,
                _ => BrushNeutral
            }
            : BrushNeutral;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 着色依据 → 前景画刷（T-04，规格见 docs/01-spec.md 4.12）。
///
/// <b>返回 <see cref="AvaloniaProperty.UnsetValue"/> 表示「不着色」</b>，
/// 让控件退回主题的默认前景色。写死一个「近似默认色」会在浅色 / 深色主题之间露馅，
/// 而不着色的那一类恰恰是屏幕上的绝大多数 —— 它必须跟着主题走。
///
/// ⚠️ <b>这里刻意不是 <c>null</c>，而且这个区别是实机跑出来的</b>（2026-08-01）：
/// 绑定到 <c>null</c> 会给 <c>Foreground</c> 设一个**本地值 null**，
/// 语义是「**不画**」而不是「用默认值」—— 界面上的结果是
/// <b>通道列与数据列的文字整片消失</b>，因为 RX 走的正是这个分支。
/// <b>而单元测试当时是绿的</b>：它断言的是「返回 null」，
/// 也就是**把错误的假设本身当成了判据**。只有
/// <see cref="AvaloniaProperty.UnsetValue"/> 才表示「这条绑定不提供值」。
///
/// <b>两种用法，靠 <c>ConverterParameter</c> 区分</b>：
/// <list type="table">
///   <item>
///     <term>无参数</term>
///     <description><b>通道列</b>：四类各自上色，监听会话的蓝 / 绿保持原样</description>
///   </item>
///   <item>
///     <term><c>TxOnly</c></term>
///     <description><b>数据列</b>：只给终端会话的 TX 上紫色，其余一律不着色</description>
///   </item>
/// </list>
///
/// ⚠️ <b>数据列为什么只认 TX</b>：本轮确认的范围是「**单串口终端**里区分收发」。
/// 若数据列也按通道上色，监听会话每一行的正文都会变成蓝 / 绿 ——
/// 那是没被要求的视觉改动，而且会把现在克制的配色（窄色条 + 默认色正文）搞吵。
/// 见 03-conventions 0.2「顺手是最常见的越界形式」。
///
/// ⚠️ <b>紫色是避开来的</b>：蓝与绿在监听会话里表示通道 A / B，终端里复用会误导；
/// 橙色是 M-09 注入警告的「危险」色，而终端会话写串口本来就是正常操作。
/// </summary>
public sealed class FrameAccentToBrushConverter : IValueConverter
{
    /// <summary>数据列用的 <c>ConverterParameter</c>：只给 TX 上色。</summary>
    public const string TxOnly = "TxOnly";

    /// <summary>终端会话中本机发出的数据。见类注释里「为什么是紫色」。</summary>
    public const string ColorTx = "#8C5BD8";

    private static readonly IBrush BrushA = SolidColorBrush.Parse(ChannelViewModel.ColorA);
    private static readonly IBrush BrushB = SolidColorBrush.Parse(ChannelViewModel.ColorB);
    private static readonly IBrush BrushTx = SolidColorBrush.Parse(ColorTx);

    /// <summary>「不着色」—— 见类注释，<b>不能用 <c>null</c> 代替</b>。</summary>
    private static object NotColoured => AvaloniaProperty.UnsetValue;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not FrameAccent accent) return NotColoured;

        if (parameter as string == TxOnly)
        {
            return accent == FrameAccent.Tx ? BrushTx : NotColoured;
        }

        return accent switch
        {
            FrameAccent.ChannelA => BrushA,
            FrameAccent.ChannelB => BrushB,
            FrameAccent.Tx => BrushTx,
            _ => NotColoured   // 终端会话的 RX：退回主题默认前景色
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 行是否显示（T-05，规格见 docs/01-spec.md 4.12）。
/// 两个入参：<c>[0]</c> 本行是不是 TX、<c>[1]</c> 当前是否显示发送数据。
///
/// ⚠️ <b>这是「过滤」不是「闸门」</b>：帧照常进显示缓冲，只是不画。
/// 于是重新勾上时它们**立刻全部回来** —— 与
/// <see cref="Panels.LogPanelViewModel.IsPaused"/>「丢弃且不补」正相反，
/// 那条区别写在 01-spec 4.12 的对照表里。
/// </summary>
public sealed class SentVisibilityConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        // 绑定尚未就绪时 Avalonia 传 UnsetValue —— 那时一律显示，
        // 宁可多画一行也不要让整个显示区在初始化瞬间空掉。
        var isTx = values.Count > 0 && values[0] is true;
        var showSent = values.Count < 2 || values[1] is not false;

        return !isTx || showSent;
    }
}

/// <summary>
/// 行底色（4.9.3 异常帧 + T-04 收发区分，规格见 docs/01-spec.md 4.12）。
///
/// <b>两个信号共用同一个面，所以优先级写在这一处，而不是散在几条绑定里。</b>
/// 入参：<c>[0]</c> 这一帧是不是异常帧、<c>[1]</c> 着色依据。
///
/// ⭐ <b>2026-08-04 用户定：方向区分由「行底色」承担，不再是数据列的前景色。</b>
/// 判据是实机看出来的：底色是<b>面</b>，字色是<b>线</b> —— 余光扫过去时面看得见、线看不见，
/// 而这正是「一眼分出哪几行是我发的」要的能力。
/// 顺带满足了原先那条理由（着色不能只落在可被关掉的通道列上）：<b>行底色永远可见</b>。
///
/// ⚠️ <b>异常帧红底优先，TX 底色让位</b>（2026-08-04 用户定）——
/// 理由就是 4.12 里本来写着的那句：<b>「这帧解析出错」比「这帧是我发的」更要紧</b>。
/// ⛔ <b>V1.0 里这一幕不会出现</b>：<c>IsError</c> 看的是 <c>SerialFrame.Flags</c>，
/// 而<b>没有任何一处给那个字段赋过非 <c>None</c> 的值</b>。
/// <b>规则先定在这里，是为了将来接上硬件错误那天不必重新想一遍。</b>
///
/// ⚠️ <b>Do not shorten this back to "FrameFlags has zero producers"</b> (2026-08-08).
/// The <i>type</i> does have producers -- <c>SystemIoSerialPort.MapLineError</c> and
/// <c>ReplayScenarios</c>'s <c>ReplayFault</c> -- but they feed the <b>session-level error
/// event</b>, never a frame. The shorter wording reads as "grep finds nothing", and a
/// <c>grep FrameFlags</c> finds plenty; that misreading happened once already.
/// ⛔ P1-52 closed with <b>frame-level red marking deliberately not done</b>: the driver's
/// <c>ErrorReceived</c> never says which bytes were affected.
///
/// ⚠️ <b>两个颜色都是低透明度叠加色，这不是风格选择</b>：
/// <c>App.axaml</c> 是 <c>RequestedThemeVariant="Default"</c>，<b>跟随系统深浅色</b>。
/// 不透明的淡黄底在深色主题下会配上浅色的默认前景，几乎读不了；
/// 而 20%–25% 的叠加色在浅色主题下是淡黄、深色主题下自动变暗，<b>前景色一个字都不用动</b>。
/// </summary>
public sealed class FrameRowBackgroundConverter : IMultiValueConverter
{
    /// <summary>异常帧（4.9.3）。取值与本条目自 2026-07 起未变。</summary>
    public const string ColorError = "#33E24B4A";

    /// <summary>
    /// 终端会话中本机发出的数据（T-04）。
    /// ⚠️ <b>浓淡是人眼定的，没有判据可算</b>：40%（<c>#66</c>）→ 25%（<c>#40</c>）→
    /// <b>18%（<c>#2E</c>）定案</b>，三档都是在真界面上看过截图才往下走的。
    /// </summary>
    public const string ColorTxBackground = "#2EF5C518";

    private static readonly IBrush ErrorBackground = SolidColorBrush.Parse(ColorError);
    private static readonly IBrush TxBackground = SolidColorBrush.Parse(ColorTxBackground);

    public object Convert(
        IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        // 绑定尚未就绪时 Avalonia 传 UnsetValue —— 那时按「都不是」处理，
        // 宁可少画一层底色，也不要在初始化瞬间闪一片颜色。
        if (values.Count > 0 && values[0] is true) return ErrorBackground;
        if (values.Count > 1 && values[1] is FrameAccent.Tx) return TxBackground;

        return Brushes.Transparent;
    }
}

/// <summary>
/// 布尔 → 边框粗细。监听会话中启用发送后，用持续可见的橙色边框
/// 提示当前处于「可向总线注入数据」的状态（M-09 第三层防护）。
/// </summary>
public sealed class BoolToThicknessConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? new Thickness(2) : new Thickness(0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
