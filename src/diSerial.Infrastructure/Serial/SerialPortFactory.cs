using System.Runtime.InteropServices;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using DiSerial.Infrastructure.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Serial;

// Create serial port adapters.
public sealed class SerialPortFactory(
    IMonotonicClock clock,
    ILoggerFactory? loggerFactory = null,
    LoggingOptions? loggingOptions = null) : ISerialPortFactory
{
    public PlatformSupportStatus SupportStatus { get; } = DetectPlatform();

    // Create a configured serial port.
    public ISerialPort Create(string portName, SerialPortSettings settings)
    {
        if (SupportStatus != PlatformSupportStatus.Supported)
        {
            throw new PlatformNotSupportedException(
                $"Serial ports are not supported on this platform ({SupportStatus}).");
        }

        var logger = loggerFactory?.CreateLogger($"Serial.{portName}");

        return new SystemIoSerialPort(
            portName, settings, clock, logger, loggingOptions?.IncludePayload ?? false);
    }

    // Check serial platform support.
    private static PlatformSupportStatus DetectPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return PlatformSupportStatus.Supported;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return PlatformSupportStatus.Supported;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return PlatformSupportStatus.Supported;
        return PlatformSupportStatus.UnknownPlatform;
    }
}
