using DiSerial.Core.Models;
using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Diagnostics;

/// <summary>
/// Log events for the capture-session layer.
///
/// Event ids are the <b>14xx</b> band, alongside <see cref="SerialPortLog"/> (10xx/11xx/12xx,
/// one port's lifecycle and traffic) and <see cref="DeviceWatcherLog"/> (13xx, the port list
/// itself). Category name is <c>Session</c>, matching the logger the factory hands to
/// <c>MonitorCaptureSession</c> and the one <c>SessionViewModel</c> uses — session-level facts
/// stay one filterable dimension.
///
/// ⚠️ <b>Keep session-level events here.</b> The state-transition event the diagnostic
/// contract asks for (03-conventions 8.4) landed here with P1-11 on 2026-08-05 —
/// do not start a second class for anything of this kind.
/// </summary>
public static partial class SessionLog
{
    /// <summary>
    /// A merged-timeline frame arrived with a <b>negative</b> Δ, i.e. its start is earlier than
    /// the end of the frame emitted before it.
    ///
    /// <para><b>This is not an error and nothing is corrected.</b> The monitor session
    /// deliberately does not reorder across channels (01-spec 4.9.2): the two readers share one
    /// event, frames go out in arrival order, and a negative Δ is the honest, visible signal
    /// that arrival order and timestamp order disagree within one flush period.</para>
    ///
    /// <para>⭐ <b>Why log something the UI already shows.</b> "A negative number shows up on
    /// screen now and then" cannot be counted. Once this event exists the reordering is
    /// <b>measurable in the field</b>, so the question "is it more frequent than the ~5 ms flush
    /// window predicts, and should we add a short reorder window after all" gets answered with
    /// evidence rather than an impression. That is the concrete half 03-conventions 0.4 asks
    /// for: a concern that was overruled leaves behind a hook that can turn it into data.</para>
    ///
    /// <para>⚠️ <b>Debug, not Warning.</b> It fires per frame on a busy two-way bus, which puts
    /// it in the same bucket as the per-chunk read log (03-conventions 8.2 ②). Nothing is
    /// broken when it fires.</para>
    /// </summary>
    [LoggerMessage(EventId = 1401, Level = LogLevel.Debug,
        Message = "Merged timeline out of order: frame {Sequence} on channel {Channel} "
            + "has DeltaMs={DeltaMs:F3} (negative; arrival order differs from timestamp order)")]
    public static partial void NegativeDelta(
        ILogger logger, long sequence, ChannelId channel, double deltaMs);

    /// <summary>
    /// A capture session moved from one connection state to another (P1-11).
    ///
    /// <para>⭐ <b>What this is for: attributing a disconnect after the fact.</b> Until this
    /// existed, a session's whole lifecycle was invisible in the log — a support log could show
    /// a read loop faulting and, later, an error notice being shown, with nothing tying them
    /// into a sequence. The 2026-08-05 P1-54 investigation had to run a purpose-built probe to
    /// see transitions at all, because the product does not record them.</para>
    ///
    /// <para>⚠️ <b>Emitted from <c>SetState</c>, which already de-duplicates</b> (it returns
    /// early when the state is unchanged), so every line here is a real transition. That also
    /// makes it the one place a new code path cannot forget — the same argument
    /// <c>SessionViewModel.OnStateChanged</c> makes for hanging work off the assignment point
    /// rather than off each command.</para>
    ///
    /// <para>⚠️ <b>Information, not Debug.</b> It fires a handful of times per session, not per
    /// frame, and it is exactly what someone reading a field log needs first. Contrast
    /// <see cref="NegativeDelta"/> above, which is per frame and therefore Debug.</para>
    ///
    /// <para>⛔ <b>The reason is deliberately not a parameter.</b> Why a session faulted is
    /// already on the record from two other places — <c>SerialPortLog.ReadLoopFaulted</c> with
    /// the exception, and the App layer's "Error notice shown: kind=…" warning. Repeating it
    /// here would create a third copy that can disagree with the other two.</para>
    /// </summary>
    [LoggerMessage(EventId = 1402, Level = LogLevel.Information,
        Message = "Session state: {From} -> {To} ({Kind} session on {Target})")]
    public static partial void StateTransition(
        ILogger logger, ConnectionState from, ConnectionState to, SessionKind kind, string target);
}
