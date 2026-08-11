namespace DiSerial.Infrastructure.Serial.Enumeration;

/// <summary>
/// Platform-specific description text for a port.
///
/// Port <b>names</b> are available everywhere through <c>SerialPort.GetPortNames()</c>,
/// but a readable description ("Prolific USB-to-Serial Comm Port") needs one
/// implementation per platform:
///   Windows — WMI Win32_PnPEntity
///   macOS   — not implemented; degrades to showing the port name only
///
/// ⚠️ This interface provides <b>only</b> description text, never device identity such
/// as VID/PID. Device identification is always based on port co-occurrence and is
/// independent of the chip vendor -- see IDeviceWatcher. So leaving macOS unimplemented
/// costs no functionality at all, only some readability in the list.
/// </summary>
internal interface IPortDetailProvider
{
    /// <summary>返回「端口名 → 描述」的映射。取不到的端口不出现在结果中。</summary>
    Task<IReadOnlyDictionary<string, string>> GetDescriptionsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 取不到描述时的退化实现（macOS、未知平台，或 WMI 不可用时）。
/// 端口列表照常工作，只是每一项少一段说明文字。
/// </summary>
internal sealed class NullPortDetailProvider : IPortDetailProvider
{
    public Task<IReadOnlyDictionary<string, string>> GetDescriptionsAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
}
