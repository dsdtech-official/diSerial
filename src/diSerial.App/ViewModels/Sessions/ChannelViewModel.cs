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

    public ChannelViewModel(ChannelId id, SerialPortInfo port)
    {
        Id = id;
        Port = port;

        // ⚠️ 默认取端口名，不是「A」/「B」——「A」既是槽位又当名字用会让
        // 状态栏出现 A:COM6「A」这种自我重复，而端口名是此刻唯一已知的事实。
        Alias = port.PortName;
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
    /// ⭐ <b>帧行通道列用的标签</b>：<c>COM6</c>（没起过名字）或 <c>COM6 · PLC</c>（起过）。
    ///
    /// ⚠️ <b>原先这里是 <c>A · PLC</c>，2026-08-01 换成端口名。</b>
    /// 理由：<c>A</c> 曾经有意义是因为它<b>可以被交换</b>——「谁是 A」是一个
    /// 用户需要确认、也能改变的事实。<b>交换 A/B 随 P0-9 删除之后</b>，
    /// <c>A</c> 退化成「对话框里第一个下拉框」，不指向任何用户关心的东西，
    /// 而真正的身份（哪个物理口）反倒不在行里。见 01-spec 4.13。
    ///
    /// ⚠️ 分隔符是 <b>标点不是文案</b>，刻意不进资源文件 —— 沿用原 <c>A · PLC</c> 的先例。
    /// 进了资源文件就得在语言切换时重算每一行（上限 500 行），
    /// 而这里没有任何一个字需要翻译。
    /// </summary>
    public string InlineLabel => HasCustomAlias ? $"{PortName} · {Alias}" : PortName;

    /// <summary>
    /// 用户是否给这一路起过名字 —— 即别名已不再等于端口名。
    ///
    /// 用途：没起过名字时只显示端口名，不显示 <c>COM6「COM6」</c> / <c>COM6 · COM6</c>
    /// 这种自我重复。<b>状态栏与帧行共用这一条判据</b>，两处不会各说各话。
    /// 判据用「不等于端口名」而不是「非空」，因为默认值本身就是端口名。
    /// </summary>
    public bool HasCustomAlias =>
        !string.IsNullOrWhiteSpace(Alias) && !string.Equals(Alias, PortName, StringComparison.Ordinal);

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
