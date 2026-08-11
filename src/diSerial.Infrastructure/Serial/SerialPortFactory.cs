using System.Runtime.InteropServices;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using DiSerial.Infrastructure.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Serial;

/// <summary>
/// 串口实例工厂 —— 平台差异的唯一收敛点。
///
/// 这是整个跨平台策略的关键：调用方永远只拿到 <see cref="ISerialPort"/>，
/// 具体用哪个实现由本类按运行平台决定。新增 macOS 支持时，
/// 只需在此追加一个分支并提供 TermiosSerialPort，
/// 上层（会话、ViewModel、View）零改动。
///
/// <see cref="SupportStatus"/> 让 UI 能在<b>用户点连接之前</b>就给出明确提示，
/// 而不是等到打开端口时抛 PlatformNotSupportedException 才崩溃。
/// </summary>
/// <param name="loggerFactory">
/// 可选。每个端口拿到以自身端口名为分类的 logger，日志里可直接按端口过滤。
/// 未提供时串口链路不产生日志，其余行为完全不变。
/// </param>
/// <param name="loggingOptions">
/// 可选。仅用于决定是否记录报文十六进制内容；未提供时一律不记。
/// </param>
public sealed class SerialPortFactory(
    IMonotonicClock clock,
    ILoggerFactory? loggerFactory = null,
    LoggingOptions? loggingOptions = null) : ISerialPortFactory
{
    public PlatformSupportStatus SupportStatus { get; } = DetectPlatform();

    public ISerialPort Create(string portName, SerialPortSettings settings)
    {
        if (SupportStatus != PlatformSupportStatus.Supported)
        {
            throw new PlatformNotSupportedException(
                $"Serial ports are not supported on this platform ({SupportStatus}).");
        }

        // 分类名带上端口名，监听会话的两路通道在日志里天然可分。
        var logger = loggerFactory?.CreateLogger($"Serial.{portName}");

        // Windows uses the System.IO.Ports implementation.
        // macOS will return a TermiosSerialPort here in a later version.
        return new SystemIoSerialPort(
            portName, settings, clock, logger, loggingOptions?.IncludePayload ?? false);
    }

    private static PlatformSupportStatus DetectPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return PlatformSupportStatus.Supported;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return PlatformSupportStatus.Supported;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return PlatformSupportStatus.MacOsNotImplemented;
        return PlatformSupportStatus.UnknownPlatform;
    }
}
