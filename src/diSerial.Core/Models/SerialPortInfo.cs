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

    /// <summary>
    /// The one authoritative encoding of a port name for use inside a FILE NAME:
    /// <c>COM3</c> stays <c>COM3</c>, <c>/dev/cu.usbserial-A1</c> becomes
    /// <c>cu.usbserial-A1</c>.
    ///
    /// <para>It lives in Core for the same reason <see cref="SerialPortSettings.ShortDescription"/>
    /// does: three call sites build default export file names, and they must agree
    /// character for character.</para>
    ///
    /// <para>⛔ <b>P2-117.</b> Before this existed, every call site pasted the raw port name
    /// into the file name. On Windows that is safe -- and a comment in
    /// <c>MonitorSessionViewModel</c> said so outright: "a port name is naturally safe in a
    /// file name". On macOS a port name IS a path, so the "file name" carried slashes and
    /// the export silently created directories: a file the dialog showed as living in
    /// <c>/tmp</c> was written to <c>/tmp/diserial-/dev/</c>. Nothing reported an error.</para>
    ///
    /// <para>⭐ <b>Only the last path segment is kept</b> (2026-08-15, user's call). On macOS
    /// <c>/dev/</c> is a constant prefix, so dropping it loses no identifying information --
    /// "the port can be recovered from the file name", which is exactly why 01-spec 4.10
    /// lets the <c>Port</c> column stay out of the file, still holds.</para>
    ///
    /// <para>⚠️ Empty in, empty out: no substitute label is invented. A fabricated
    /// <c>"port"</c> would read like a real port name, and this project has already ruled
    /// that silently plausible answers are worse than visible gaps (P2-88 / P2-90).</para>
    /// </summary>
    public static string FileNameSegment(string? portName)
    {
        if (string.IsNullOrWhiteSpace(portName)) return string.Empty;

        // ⛔ BOTH separators are handled here rather than calling Path.GetFileName: that
        //    method is PLATFORM-DEPENDENT ('\' is an ordinary character on Unix), and a
        //    port-name rule that behaves differently per platform is the very defect this
        //    method exists to fix.
        var cut = portName.LastIndexOfAny(['/', '\\']);
        var segment = cut >= 0 ? portName[(cut + 1)..] : portName;

        // ⛔ Our own explicit set, NOT Path.GetInvalidFileNameChars(): that returns ~41
        //    characters on Windows and 2 on Unix, so both the behaviour AND the tests
        //    guarding it would differ per platform -- again the defect being fixed.
        //    This set is the Windows one, i.e. the stricter of the two, applied everywhere.
        var chars = segment.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (InvalidInFileName.Contains(chars[i]) || char.IsControl(chars[i]))
                chars[i] = '-';

        return new string(chars);
    }

    private const string InvalidInFileName = "/\\:*?\"<>|";
}
