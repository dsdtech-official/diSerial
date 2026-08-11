using DiSerial.Core.Models;

namespace DiSerial.Core.Abstractions;

/// <summary>
/// 串口枚举（C-02a）。
///
/// 只负责「当前有哪些端口」这一快照式问题。
///
/// ⚠️ 双通道设备的配对<b>不在</b>本接口职责内。配对依据是「两个端口在同一次
/// 轮询中同时出现」，属于时序信息，单次快照无从判断，因此交由
/// <see cref="IDeviceWatcher"/> 负责。
///
/// 端口名本身跨平台可得（SerialPort.GetPortNames）；描述文本需要平台特定
/// 实现，取不到时降级为只有端口名，不影响任何功能。
/// </summary>
public interface IPortEnumerator
{
    /// <summary>枚举当前系统上的全部串口。</summary>
    Task<IReadOnlyList<SerialPortInfo>> GetPortsAsync(CancellationToken cancellationToken = default);
}
