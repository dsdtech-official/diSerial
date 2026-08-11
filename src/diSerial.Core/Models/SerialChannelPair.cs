namespace DiSerial.Core.Models;

/// <summary>
/// 监听会话所用的两路串口通道。
///
/// 命名刻意保持中性（而非 DiDatatrackerPair）：监听功能对<b>任意</b>双通道
/// 串口设备都成立 —— 一台双串口适配器、两根独立的 USB 转串口线、
/// 或本公司的 diDatatracker，在软件看来没有区别。
///
/// 这既是架构原则（不排他绑定自家硬件 → 用户基数最大化），
/// 也是工程上的必需（硬件更换芯片方案时软件不受影响）。
///
/// <b>关于 A/B 方向</b>：哪一路对应机壳上哪个物理接口，是布线决定的，
/// USB 元数据里没有这个信息，任何自动推断都不可靠。因此
/// <see cref="Swap"/> 不是兜底手段，而是用户确定方向的<b>主要途径</b>。
/// </summary>
public sealed record SerialChannelPair
{
    public required SerialPortInfo ChannelA { get; init; }

    public required SerialPortInfo ChannelB { get; init; }

    /// <summary>交换 A / B 通道，返回新实例。</summary>
    public SerialChannelPair Swap() => this with { ChannelA = ChannelB, ChannelB = ChannelA };

    public override string ToString() => $"{ChannelA.PortName} / {ChannelB.PortName}";
}
