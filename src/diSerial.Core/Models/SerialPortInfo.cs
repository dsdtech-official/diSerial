namespace DiSerial.Core.Models;

/// <summary>
/// 串口设备信息。
///
/// ⚠️ 刻意<b>不包含</b> USB VID/PID、序列号等设备身份信息。
///
/// 早期版本用 VID/PID 判定「这是不是 diDatatracker」，那是错误的设计：
///   1. VID/PID 属于串口芯片厂商（如 Prolific），不属于本产品 ——
///      一旦更换芯片方案，已发布的软件就认不出新硬件；
///   2. 同款芯片的其他厂商产品会被误判为本产品；
///   3. 它把软件逻辑绑死在了硬件 BOM 上，耦合方向是反的。
///
/// 现在双通道设备的识别改由 <c>IDeviceWatcher</c> 依据「端口共现」完成，
/// 不依赖任何设备身份信息，因此对任意双通道串口设备都有效。
/// </summary>
public sealed record SerialPortInfo
{
    /// <summary>系统端口名，如 COM3 或 /dev/ttyUSB0。</summary>
    public required string PortName { get; init; }

    /// <summary>
    /// 设备描述，如 "Prolific USB-to-Serial Comm Port"。
    /// 仅用于让用户在多个端口中辨认目标，不参与任何判定逻辑。
    /// 取不到时为空字符串（macOS 与未知平台即为此情况）。
    /// </summary>
    public string Description { get; init; } = string.Empty;

    public string DisplayName =>
        string.IsNullOrEmpty(Description) ? PortName : $"{PortName} — {Description}";
}
