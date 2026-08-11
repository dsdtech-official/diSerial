using System.Runtime.InteropServices;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using DiSerial.Infrastructure.Serial.Enumeration;
using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Serial;

/// <summary>
/// 真实的串口枚举实现（C-02a）。
///
/// 两步：
///   1. 端口名 —— <c>SerialPort.GetPortNames()</c>，跨平台可用
///   2. 描述文本 —— 交给平台特定的 <see cref="IPortDetailProvider"/>
///
/// 第 2 步失败或未实现（macOS）时第 1 步仍然有效，功能不受影响，
/// 只是列表里少一段说明文字。
/// </summary>
public sealed class SystemPortEnumerator : IPortEnumerator
{
    private readonly IPortDetailProvider _details;

    /// <summary>
    /// <paramref name="loggerFactory"/> 可为 null：只有平台实现需要它，
    /// 用来记录「取描述失败」这类降级（01-spec 4.7 共有第 1 条）。
    /// </summary>
    public SystemPortEnumerator(ILoggerFactory? loggerFactory = null)
        => _details = CreateDetailProvider(loggerFactory);

    public async Task<IReadOnlyList<SerialPortInfo>> GetPortsAsync(
        CancellationToken cancellationToken = default)
    {
        var names = System.IO.Ports.SerialPort.GetPortNames();
        var descriptions = await _details.GetDescriptionsAsync(cancellationToken);

        return names
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(NumericAwareKey, StringComparer.OrdinalIgnoreCase)
            .Select(name => new SerialPortInfo
            {
                PortName = name,
                Description = descriptions.GetValueOrDefault(name, string.Empty)
            })
            .ToArray();
    }

    /// <summary>
    /// 排序键：把端口名尾部的数字左补零，使 COM9 排在 COM10 之前。
    /// 纯字典序会得到 COM1, COM10, COM2 这样的顺序，在端口较多时很难找。
    /// </summary>
    private static string NumericAwareKey(string portName)
    {
        var digitStart = portName.Length;
        while (digitStart > 0 && char.IsAsciiDigit(portName[digitStart - 1])) digitStart--;

        if (digitStart == portName.Length) return portName;

        var prefix = portName[..digitStart];
        var digits = portName[digitStart..];
        return $"{prefix}{digits.PadLeft(6, '0')}";
    }

    private static IPortDetailProvider CreateDetailProvider(ILoggerFactory? loggerFactory)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WindowsPortDetailProvider(
                loggerFactory?.CreateLogger(nameof(WindowsPortDetailProvider)));

        // Every other platform: port names only.
        // Since device identification no longer depends on VID/PID, not implementing this
        // costs no functionality -- macOS support therefore needs no IOKit interop.
        //
        // ⚠ There used to be a LinuxPortDetailProvider reading /sys; it was deleted on
        // 2026-07-29 when Linux was dropped as a target. Its empty catch was the last
        // place in the repository that could not pass
        // SourceConventionTests.NoSilentlySwallowedExceptions.
        // Recover it from git history if it is ever needed, and wire up a logger the way
        // WindowsPortDetailProvider does.
        return new NullPortDetailProvider();
    }
}
