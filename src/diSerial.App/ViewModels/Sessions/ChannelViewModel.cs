using CommunityToolkit.Mvvm.ComponentModel;
using DiSerial.Core.Models;

namespace DiSerial.App.ViewModels.Sessions;

/// <summary>
/// 监听会话中的一路通道（M-05a）。
///
/// V1.0 支持重命名（如 COM3 → "PLC"），配色固定为蓝/绿；
/// 自定义配色排期在 V1.1。
///
/// ⚠️ <b>别名默认就是端口名</b>（2026-08-01，P0-9 的修法）。
/// 建会话时根本判断不出哪个口是总线的哪一侧 —— 那要看了流量才知道，
/// 所以对话框不再预填别名，用户进监听界面看几秒再改名。
///
/// ⭐ <b>这一路的身份由「别名」承载，不由 A/B 槽位承载</b>，
/// 因此 <c>Id</c> 一旦构造就不再变 —— 原先那个「重设槽位」的方法随交换 A/B 一并删除。
/// 别名与端口绑在同一个对象上，不存在「采集侧与显示侧要对齐」的问题，
/// 而 P0-9 正是那个对齐断掉造成的。详见 01-spec 4.13。
/// </summary>
public sealed partial class ChannelViewModel : ViewModelBase
{
    /// <summary>通道 A 的固定配色（蓝）。</summary>
    public const string ColorA = "#378ADD";

    /// <summary>通道 B 的固定配色（绿）。</summary>
    public const string ColorB = "#1D9E75";

    /// <summary>
    /// ⛔ <b>P2-115.</b> Longest default alias the frame row's channel column can show.
    ///
    /// <para><b>Measured, not chosen</b> (2026-08-15, from the accessibility tree rather than by
    /// counting characters on a screenshot -- the two answers differed by 2 characters, and those
    /// 2 were the difference between fitting and overlapping):</para>
    ///
    /// <code>
    ///   column          624..754  = 130 px   (LogPanelView ColumnDefinitions "110,60,130,*")
    ///   text starts at  644                  (Margin 10 + Border 3 + Spacing 7)
    ///   available       754 - 644 = 110 px
    ///   advance         7.2 px/char          (12pt Menlo, 0.6 em; four samples agreed)
    ///   capacity        110 / 7.2 = 15.3 characters
    ///   "TX -> " costs  5                    (Frame.Channel.Injected is the longer template)
    ///   alias budget    15 - 5    = 10
    /// </code>
    ///
    /// <para>⚠️ <b>This is the whole channel text's budget minus the template</b>, not the
    /// column's capacity. Getting that wrong is what made the first two attempts land on 17 and
    /// then 12, both of which still overlap.</para>
    /// </summary>
    private const int MaxDefaultAliasLength = 10;

    /// <summary>
    /// The prefix every macOS serial device node carries. It is identical on every row, so it
    /// carries no information in a column this narrow.
    /// </summary>
    private const string UnixPortPrefix = "/dev/cu.";

    public ChannelViewModel(ChannelId id, SerialPortInfo port)
    {
        Id = id;
        Port = port;

        // The alias starts as a readable form of the port name, never as "A"/"B": a letter that
        // is both the slot and the name produces self-repeating labels, and the port is the only
        // fact known at this moment (01-spec 4.13).
        Alias = DefaultAliasFor(port.PortName);
    }

    /// <summary>
    /// ⛔ <b>P2-115.</b> The alias a channel starts with: the port name, shortened enough to fit
    /// the frame row's channel column.
    ///
    /// <para><b>Why the port name cannot be used as-is.</b> On macOS a port is
    /// <c>/dev/cu.usbserial-AQ8DUVGD</c> -- 26 characters where Windows has <c>COM11</c>. The
    /// column is sized for the Windows length, so the label overflowed and was drawn on top of
    /// the data column: two texts in the same pixels, neither readable. ⚠️ <b>The code is shared
    /// and the column is shared; only the platform's own string length differs.</b></para>
    ///
    /// <para><b>The rule</b> (user decision, 2026-08-15):</para>
    /// <list type="number">
    ///   <item>drop the <c>/dev/cu.</c> prefix -- lossless, identical on every row;</item>
    ///   <item>if still too long, keep what follows the last <c>-</c>. For the family this
    ///         product exists for (<c>usbserial-&lt;serial&gt;</c>) that tail IS the device
    ///         serial number, which is exactly what tells two identical adapters apart;</item>
    ///   <item>with no <c>-</c> to cut at (<c>usbmodem14201</c>, <c>SLAB_USBtoUART</c>), keep the
    ///         leading <see cref="MaxDefaultAliasLength"/> characters.</item>
    /// </list>
    ///
    /// <para>⚠️ <b>The cap is applied to the tail as well, and that case is NOT in the rule as
    /// stated</b> -- it was left open. A vendor whose serial number runs past
    /// <see cref="MaxDefaultAliasLength"/> would otherwise walk straight back into the overflow
    /// this exists to prevent, so the obvious intent is followed rather than the literal wording.</para>
    ///
    /// <para>⭐ <b>Deliberately not gated on the platform.</b> It is driven by the data, not by
    /// <c>OperatingSystem.IsMacOS()</c>: <c>COM11</c> has no prefix to strip and is under the
    /// cap, so Windows keeps exactly what it had. That also means every case below is
    /// exercisable on both machines -- the same reason
    /// <c>SystemPortEnumerator.IsMacOsCallinNode</c> is platform-free.</para>
    /// </summary>
    public static string DefaultAliasFor(string portName)
    {
        if (string.IsNullOrEmpty(portName)) return string.Empty;

        var name = portName.StartsWith(UnixPortPrefix, StringComparison.Ordinal)
            ? portName[UnixPortPrefix.Length..]
            : portName;

        if (name.Length <= MaxDefaultAliasLength) return name;

        var lastDash = name.LastIndexOf('-');
        if (lastDash >= 0 && lastDash < name.Length - 1)
        {
            name = name[(lastDash + 1)..];
        }

        return name.Length <= MaxDefaultAliasLength ? name : name[..MaxDefaultAliasLength];
    }

    public ChannelId Id { get; }

    [ObservableProperty]
    private SerialPortInfo _port;

    /// <summary>用户可编辑的别名，显示在合并时间轴的通道列上。</summary>
    [ObservableProperty]
    private string _alias;

    [ObservableProperty]
    private long _bytesReceived;

    /// <summary>
    /// 固定配色（M-05a）。⚠️ <b>这是 <see cref="Id"/> 在界面上仅剩的用途</b> ——
    /// 2026-08-01 起 A / B 两个字母不再出现在任何地方，槽位退回纯内部概念，
    /// 用户从侧栏的「色条 + 端口名」建立颜色与端口的对应。
    /// </summary>
    public string ColorHex => Id == ChannelId.A ? ColorA : ColorB;

    public string PortName => Port.PortName;

    /// <summary>
    /// The alias this channel starts with. Everything the user has not renamed reads as this.
    /// </summary>
    public string DefaultAlias => DefaultAliasFor(PortName);

    /// <summary>
    /// ⭐ <b>The label the frame row's channel column shows</b>: <b>the alias, and only the
    /// alias</b>.
    ///
    /// <para>⛔ <b>It used to be <c>COM6 · PLC</c></b> -- port name and alias together (01-spec
    /// 4.13 clause 6). Changed 2026-08-15 (user decision) as the fix for P2-115: the port name is
    /// <b>identical on every row of the session</b>, so in a 110px column it spends the width
    /// that the part which actually varies needs. On macOS it did not merely crowd the data
    /// column, it was drawn on top of it.</para>
    ///
    /// <para>⚠️ <b>The port name is not lost</b>, it stops being repeated per row: the status bar
    /// still writes <c>port「alias」</c> and the side panel still lists port against colour. The
    /// export keeps its separate <c>Port</c> and <c>Alias</c> columns either way.</para>
    ///
    /// <para>⚠️ Falls back to <see cref="DefaultAlias"/> when the user clears the box, so the
    /// column can never go blank -- the old code reached the same outcome through
    /// <see cref="HasCustomAlias"/>.</para>
    /// </summary>
    public string InlineLabel => string.IsNullOrWhiteSpace(Alias) ? DefaultAlias : Alias;

    /// <summary>
    /// Whether the user has named this channel -- that is, the alias is no longer the one it
    /// started with.
    ///
    /// <para>⛔ <b>The comparison is against <see cref="DefaultAlias"/>, not the port name</b>
    /// (changed 2026-08-15 with P2-115). Once the default stopped being the port name verbatim,
    /// comparing against the port name would have made <b>every</b> macOS channel look renamed
    /// from the first frame -- and that answer is consumed in three places, one of which writes
    /// files: the status bar would print <c>port「port-derived-name」</c>, and
    /// <c>ResolveChannelAlias</c> would fill the exported <c>Alias</c> column for channels nobody
    /// ever named, destroying the "named" / "never named" distinction 01-spec 4.13 clause 8
    /// promises a parser. ⚠️ <b>Nothing would have thrown.</b></para>
    ///
    /// <para>⭐ A user who types the default back in by hand still counts as "never named",
    /// which is the same forgiving behaviour the port-name comparison had.</para>
    /// </summary>
    public bool HasCustomAlias =>
        !string.IsNullOrWhiteSpace(Alias) && !string.Equals(Alias, DefaultAlias, StringComparison.Ordinal);

    /// <summary>
    /// 别名输入框的占位文字，形如「COM6 的别名」。
    ///
    /// ⚠️ <b>由 <c>MonitorSessionViewModel</c> 填入并在语言切换时重填</b> ——
    /// 本类刻意不持有 <c>ILocalizationService</c>：它不订阅 <c>CultureChanged</c>，
    /// 也就不必是 <c>IDisposable</c>，而它的宿主本来就有那条订阅，复用即可。
    /// </summary>
    [ObservableProperty]
    private string _aliasPlaceholder = string.Empty;

    partial void OnAliasChanged(string value)
    {
        OnPropertyChanged(nameof(InlineLabel));
        OnPropertyChanged(nameof(HasCustomAlias));
    }
}
