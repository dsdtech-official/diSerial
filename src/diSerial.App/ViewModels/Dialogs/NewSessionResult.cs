using DiSerial.Core.Models;

namespace DiSerial.App.ViewModels.Dialogs;

/// <summary>
/// The creation parameters the new-session dialog returns -- <b>one derived record per session
/// type</b>, not one record with a nullable field per type.
///
/// <para>⚠️ <b>2026-08-03：本记录从 <c>NewSessionDialogViewModel.cs</c> 挪到自己的文件</b> ——
/// 不是整理，是因为它声明了 <c>SessionKind</c>，而对话框外壳那个文件现在有一条
/// 「不许出现任何具体会话类型」的护栏（<c>NewSessionDialogDecouplingTests</c>）。
/// 两者同处一个文件时护栏必然红，⭐ <b>而它红得有道理</b>：请求记录知道类型，外壳不知道。</para>
///
/// <para>⭐ <b>2026-08-04 (P2-52): the nullable-field-pair shape is gone.</b> This used to be one
/// sealed record carrying <c>SessionKind Kind</c>, <c>SerialPortInfo? Port</c> and
/// <c>SerialChannelPair? Pair</c>, with exactly one of the two ever non-null and the factory
/// pulling them back out with <c>?? throw</c>. A third type meant a third nullable field that is
/// null in every other case, and two ways to answer "which type is this" -- the
/// <c>Kind</c> enum and which field happens to be set -- that could disagree with no compiler
/// help.</para>
///
/// <para>⛔ <b><c>Kind</c> was deliberately dropped rather than kept alongside the subclasses.</b>
/// Keeping it would leave the same two sources of truth the split exists to remove. The type
/// <i>is</i> the kind now; the factory pattern-matches on it and the compiler checks the payload
/// is present. Note <c>SessionKind</c> itself is untouched -- it stays in Core for
/// <c>ICaptureSession.Kind</c> and for the recording schema, which are different questions.</para>
/// </summary>
public abstract record NewSessionResult
{
    public required SerialPortSettings Settings { get; init; }

    // ⚠️ 原先这里有 ChannelAAlias / ChannelBAlias，2026-08-01 随对话框的别名输入一并删除（P0-9）。
    // 别名现在由 ChannelViewModel 自己定（默认 = 端口名），不再经过这条请求。

    // SyncParameters (M-03) was carried here until 2026-08-02 (P1-49).
    // Full rationale next to AppSettingsModel.Monitor.
}

/// <summary>
/// A terminal session: one port.
///
/// <para><b><c>required</c> is the point</b> -- it is what replaced the factory's
/// <c>?? throw new InvalidOperationException(...)</c>. A caller that forgets the port now fails
/// to compile instead of failing at the moment the user clicks Connect.</para>
/// </summary>
public sealed record TerminalSessionRequest : NewSessionResult
{
    public required SerialPortInfo Port { get; init; }
}

/// <summary>
/// A monitor session: two ports, as a pair.
///
/// <para>The pair is required for the same reason the terminal's port is -- see
/// <see cref="TerminalSessionRequest"/>.</para>
/// </summary>
public sealed record MonitorSessionRequest : NewSessionResult
{
    public required SerialChannelPair Pair { get; init; }
}
