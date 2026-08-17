using System.Runtime.InteropServices;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using DiSerial.Infrastructure.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Serial;

/// <summary>
/// Serial port factory: the single place platform differences converge.
///
/// Callers only ever receive an <see cref="ISerialPort"/>; this class decides which
/// implementation backs it. <see cref="SupportStatus"/> lets the UI say so <b>before the
/// user clicks connect</b> rather than throwing PlatformNotSupportedException on open.
///
/// <para>⭐ <b>Measured 2026-08-13 on the MacBook Air M4: macOS returns Supported and is
/// served by <see cref="SystemIoSerialPort"/> like the other two platforms.</b> This class
/// used to report MacOsNotImplemented, on the strength of a claim -- "System.IO.Ports
/// throws PlatformNotSupportedException on macOS" -- that lived in four code comments and
/// four documents and <b>was never once measured</b>. It is false: GetPortNames does not
/// throw, a /dev/cu.* port opens, and an FTDI loopback round-trips byte for byte with the
/// baud rate demonstrably applied. See docs/04-platforms.md 2.1a.</para>
///
/// <para>⚠️ <b>termios was not disproved, only bypassed.</b> System.IO.Ports cannot reach
/// ioctl(IOSSIOSPEED), so non-standard baud rates would still need the P/Invoke path that
/// docs/04-platforms.md 2.2 describes. That is a V1.1 item, and it is why those sections
/// are still on file.</para>
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

        // Windows, Linux and macOS are all served by the System.IO.Ports implementation.
        // A TermiosSerialPort would only be needed for non-standard baud rates (V1.1).
        return new SystemIoSerialPort(
            portName, settings, clock, logger, loggingOptions?.IncludePayload ?? false);
    }

    private static PlatformSupportStatus DetectPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return PlatformSupportStatus.Supported;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return PlatformSupportStatus.Supported;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return PlatformSupportStatus.Supported;
        return PlatformSupportStatus.UnknownPlatform;
    }
}
