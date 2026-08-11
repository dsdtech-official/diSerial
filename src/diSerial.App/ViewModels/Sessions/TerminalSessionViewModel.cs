using DiSerial.App.Localization;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;

namespace DiSerial.App.ViewModels.Sessions;

/// <summary>
/// 单串口终端会话（V1.0 「够用即可」范围）。
///
/// V1.0 只做：打开端口、收发数据、本地回显、发送历史。
/// 刻意不实现快捷发送宏、定时循环发送、ANSI 着色、信号线控制、VT100 仿真 ——
/// 这些排期在 V1.1 / V1.2，届时软件才开始正面对标同类工具。
/// </summary>
public sealed class TerminalSessionViewModel : SessionViewModel
{
    public TerminalSessionViewModel(
        SerialPortInfo port,
        SerialPortSettings settings,
        ICaptureSession capture,
        ISessionRecorder recorder,
        SessionContext context)
        : base(capture, recorder, context)
    {
        Port = port;
        Settings = settings;

        // Sending is always available on a terminal session. These are intrinsic to the
        // session type, not user preferences, so they are not persisted -- putting them in
        // the settings store would produce fields that look editable and do nothing.
        // (The store is settings.db since 2026-08-07; it was settings.json before.)
        SendPanel.IsMonitorSession = false;
        SendPanel.IsSendEnabled = true;

        // ⚠️ `SendPanel.TargetChannel = ChannelId.None` used to sit here. It is derived now
        // (P1-33): with no entries in SendTargets nothing can be selected, and TargetChannel
        // reads back None on its own. A terminal session has one port -- there is nothing to
        // choose between, so the picker does not appear at all.

        // 显示与发送偏好取上次记住的值（默认：相对时间戳、无通道列与增量列）。
        // 必须放在最后 —— 早于上面那几行会被它们覆盖。
        ApplyStoredPreferences();
    }

    public SerialPortInfo Port { get; }

    public SerialPortSettings Settings { get; }

    public override SessionKind Kind => SessionKind.Terminal;

    /// <summary>终端只有一个端口，通道 B 与两个别名均为 null。</summary>
    protected override RecordingBatchInfo DescribeRecordingBatch() =>
        new(SessionKind.Terminal, Port.PortName, null, null, null, Settings);

    /// <summary>
    /// 带上端口与串口参数，与「停止记录」那条导出路径的口径一致（P0-7 b）。
    /// ⚠️ <c>ShortDescription</c> 里的空格要换成连字符 —— 这个串要进文件名。
    /// </summary>
    protected override string DescribeExportBaseName(string kind) =>
        $"diserial-{Port.PortName}-{Settings.ShortDescription.Replace(' ', '-')}" +
        $"-{DateTime.Now:yyyyMMdd-HHmmss}";

    public override string Title => LF(LocKeys.SessionTerminalTitle, Port.PortName);

    public override string StatusText => LF(
        LocKeys.SessionTerminalStatus,
        Port.PortName,
        Settings.ShortDescription,
        DescribeState(),
        LogPanel.FrameCount,
        LogPanel.ByteCount);
}
