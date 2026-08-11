using DiSerial.Core.Models;

namespace DiSerial.Core.Abstractions;

/// <summary>
/// 端口插拔监视（C-03a）。定时轮询端口列表，有增减就通知。
///
/// <b>消费者是新建会话对话框</b>：对话框开着的时候插拔串口，端口下拉即时跟着变，
/// 不必关掉重开。轮询周期是准确性与开销的折中 ——
/// Windows 上一次枚举含 WMI 查询约 250ms，过密会持续占用 CPU。
///
/// ⚠️ <b>本接口不再承担「双通道设备识别」</b>（2026-07-29 移除）。
/// 原先有一个 <c>DualChannelDeviceDetected</c> 事件，判据是「两个端口在同一次轮询中
/// 同时出现」，UI 据此弹出插入提示引导创建监听会话（原 M-02）。
///
/// 移除的理由：diDatatracker 本质上就是两个串口，「识别」只是基于共现的猜测 ——
/// 同一周期内插入两个不相干的设备会被误判，而提示条能做的事用户本来就做得到
/// （新建会话对话框默认取前两个端口）。**判据不可靠而收益是快捷方式，不值得。**
/// </summary>
public interface IDeviceWatcher : IAsyncDisposable
{
    bool IsRunning { get; }

    /// <summary>端口列表发生变化（新增或移除）。</summary>
    event EventHandler<PortsChangedEventArgs>? PortsChanged;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}

public sealed class PortsChangedEventArgs(
    IReadOnlyList<SerialPortInfo> added,
    IReadOnlyList<SerialPortInfo> removed,
    IReadOnlyList<SerialPortInfo> current) : EventArgs
{
    public IReadOnlyList<SerialPortInfo> Added { get; } = added;

    public IReadOnlyList<SerialPortInfo> Removed { get; } = removed;

    public IReadOnlyList<SerialPortInfo> Current { get; } = current;
}
