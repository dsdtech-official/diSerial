using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;

namespace DiSerial.Infrastructure.Replay;

/// <summary>
/// 在真实端口列表之后追加回放端口。
///
/// 用装饰器而非改动 <see cref="Serial.SystemPortEnumerator"/>：
/// 真实实现保持纯粹，开发期能力完全叠加在外层，
/// 移除时只需在组合根里去掉一层包装。
/// </summary>
public sealed class ReplayAwarePortEnumerator(IPortEnumerator inner) : IPortEnumerator
{
    public async Task<IReadOnlyList<SerialPortInfo>> GetPortsAsync(
        CancellationToken cancellationToken = default)
    {
        var real = await inner.GetPortsAsync(cancellationToken);

        var replay = ReplayScenarios.All.Select(s => new SerialPortInfo
        {
            PortName = ReplayScenarios.PortNameFor(s),
            Description = s.Description
        });

        // 回放端口排在真实端口之后，避免在有真硬件时抢占默认选中项。
        return [.. real, .. replay];
    }
}

/// <summary>
/// 遇到回放端口名时返回 <see cref="ReplaySerialPort"/>，其余交给真实工厂。
///
/// 上层（采集会话、ViewModel、View）完全不知道这一层的存在 ——
/// 它们拿到的都是 <see cref="ISerialPort"/>。
/// </summary>
public sealed class ReplayAwareSerialPortFactory(
    ISerialPortFactory inner,
    IMonotonicClock clock,
    ReplayOptions options) : ISerialPortFactory
{
    /// <summary>
    /// 平台状态取真实工厂的结论。
    ///
    /// 刻意不因为「有回放端口可用」就把不支持的平台报成支持 ——
    /// 否则 macOS 上的用户会以为串口能用，反而更误导。
    /// </summary>
    public PlatformSupportStatus SupportStatus => inner.SupportStatus;

    public ISerialPort Create(string portName, SerialPortSettings settings)
    {
        if (ReplayScenarios.FindByPortName(portName) is { } script)
        {
            return new ReplaySerialPort(portName, settings, script, options, clock);
        }

        return inner.Create(portName, settings);
    }
}
