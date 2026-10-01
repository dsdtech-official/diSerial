using DiSerial.Core.Models;

namespace DiSerial.Core.Abstractions;

public interface ISerialPortFactory
{
    PlatformSupportStatus SupportStatus { get; }

    bool IsPlatformSupported => SupportStatus == PlatformSupportStatus.Supported;

    // Create a configured serial port.
    ISerialPort Create(string portName, SerialPortSettings settings);
}
