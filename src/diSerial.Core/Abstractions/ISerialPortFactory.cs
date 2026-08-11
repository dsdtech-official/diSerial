using DiSerial.Core.Models;

namespace DiSerial.Core.Abstractions;

/// <summary>
/// Factory for serial port instances.
///
/// It concentrates the "which platform implementation" decision in one place:
///   V1.0 — Windows returns the System.IO.Ports based implementation
///   V1.1 — macOS returns a P/Invoke termios based implementation
/// Callers above only ever see ISerialPort and never notice the difference.
/// </summary>
public interface ISerialPortFactory
{
    /// <summary>
    /// 当前平台的支持状态。用于在 UI 上提前给出明确提示而非等到打开端口才崩溃。
    ///
    /// 返回状态码而非文本 —— 本层不产出用户可见的本地化字符串，
    /// 由 App 层的 <c>PlatformStatusPresenter</c> 映射为当前语言的提示。
    /// </summary>
    PlatformSupportStatus SupportStatus { get; }

    /// <summary>当前平台是否受支持。</summary>
    bool IsPlatformSupported => SupportStatus == PlatformSupportStatus.Supported;

    ISerialPort Create(string portName, SerialPortSettings settings);
}
