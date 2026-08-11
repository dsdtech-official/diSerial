using CommunityToolkit.Mvvm.ComponentModel;
using DiSerial.App.Localization;
using DiSerial.App.Services;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;

namespace DiSerial.App.ViewModels.Panels;

/// <summary>
/// The serial control signal panel (T-07, spec 4.15).
///
/// <para><b>Two output checkboxes</b> (DTR, RTS — both start unticked) and <b>three input
/// indicators</b> (CTS, DSR, DCD) with three states each.</para>
///
/// <para>⛔ <b>RI is absent on purpose.</b> <c>System.IO.Ports.SerialPort</c> has no
/// ring-indicator level property, only the edge event <c>SerialPinChange.Ring</c> — so there is
/// no level to show, and a dot pretending otherwise would be lying. User decision 2026-08-06.
/// See <see cref="SerialControlLines"/>.</para>
/// </summary>
public sealed partial class SignalPanelViewModel : LocalizedViewModelBase
{
    /// <summary>
    /// Poll period (spec 4.15, promise 6 and the "real time" scale — user decision 2026-08-06).
    ///
    /// <para>⛔ <b>Polling is not a shortcut taken instead of subscribing to
    /// <c>PinChanged</c>; it is the only correct option.</b> Measured 2026-08-06 on the HHD
    /// virtual driver with <c>tools\signals.ps1</c>: DCD's level toggled six times and
    /// <c>CDChanged</c> fired <b>zero</b> times (only <c>DsrChanged</c>, six times — with a
    /// <c>DataReceived</c> control in the same run, so "no event" was not merely "no
    /// subscription"). An event-driven DCD indicator therefore sits on a stale value for as long
    /// as the session lives.</para>
    /// </summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly IControlLineSession _session;
    private readonly IPeriodicTimer _poll;

    private bool _disposed;

    public SignalPanelViewModel(
        IControlLineSession session,
        IPeriodicTimerFactory timers,
        ILocalizationService localization)
        : base(localization)
    {
        _session = session;

        // Background priority: three property reads every 250 ms must never compete with the
        // frame pump for the UI thread. A signal indicator that lags a frame burst by a few
        // hundred ms is invisible; a display that stutters is not.
        IsRtsOwnedByFlowControl = session.IsRtsOwnedByFlowControl;

        _poll = timers.Create(PollInterval, Poll, TimerPriority.Background);
        _poll.Start();

        // ⚠️ Read once, immediately. Waiting for the first tick would leave all three showing
        // Unknown for a quarter second after every expand -- brief, but it is the state that
        // means "we do not know", so showing it when we could simply look is a small lie.
        Poll();
    }

    /// <summary>
    /// <b>Expanded by default</b> (user decision 2026-08-07) and <b>not remembered across
    /// restarts</b> — the same rule as the "advanced" panel beside it.
    ///
    /// <para>⭐ <b>The default flipped once the panels went side by side.</b> The user's
    /// reasoning: hiding a panel is V1.1's job, from a View menu — "if the user does not need
    /// it, they choose not to show it". Folding is then a per-session convenience rather than
    /// the way a panel gets off the screen.</para>
    ///
    /// <para>⛔ <b>While this is false, nothing on screen reports the three lines</b> — user
    /// decision 2026-08-06, "the header does not carry status". ⚠️ That clause is <b>not</b>
    /// retired by the new default: it now needs the user to collapse the panel deliberately,
    /// which is a much smaller exposure than starting that way, but it is not zero. Spec 4.15
    /// carries the wording. <b>Do not add a status summary to the header without asking.</b></para>
    /// </summary>
    [ObservableProperty]
    private bool _isExpanded = true;

    /// <summary>DTR — <b>starts unticked</b> (user decision 2026-08-06).</summary>
    [ObservableProperty]
    private bool _dtrAsserted;

    /// <summary>RTS — <b>starts unticked</b> (user decision 2026-08-06).</summary>
    [ObservableProperty]
    private bool _rtsAsserted;

    /// <summary>
    /// True when hardware flow control owns RTS, which makes the checkbox read-only.
    ///
    /// <para>⭐ <b>The control stays visible and shows its real level rather than disappearing</b>
    /// — the same shape as the line-ending dropdown greying out in HEX mode (spec 4.11): "it does
    /// not apply right now" and "here is what it is" are two facts, and hiding the control would
    /// only tell the first.</para>
    /// </summary>
    [ObservableProperty]
    private bool _isRtsOwnedByFlowControl;

    [ObservableProperty]
    private ControlLineState _cts = ControlLineState.Unknown;

    [ObservableProperty]
    private ControlLineState _dsr = ControlLineState.Unknown;

    [ObservableProperty]
    private ControlLineState _dcd = ControlLineState.Unknown;

    /// <summary>
    /// The state word beside the CTS dot — <b>the half of the indicator that is not a colour</b>.
    ///
    /// <para>⛔ <b>Spec 4.15 promise 5 requires shape and word to change with the state, not just
    /// the fill.</b> A green/grey pair alone is unreadable to a colour-blind user, and the third
    /// state (unknown) is the one that matters most: it is the difference between "the line is
    /// low" and "nobody looked".</para>
    ///
    /// <para>⚠️ <b>Recomputed on culture change for free</b> — <see cref="LocalizedViewModelBase"/>
    /// raises an all-properties notification, and these have no backing field.</para>
    /// </summary>
    public string CtsText => Describe(Cts);

    /// <inheritdoc cref="CtsText"/>
    public string DsrText => Describe(Dsr);

    /// <inheritdoc cref="CtsText"/>
    public string DcdText => Describe(Dcd);

    private string Describe(ControlLineState state) => L(state switch
    {
        ControlLineState.High => LocKeys.SignalStateHigh,
        ControlLineState.Low => LocKeys.SignalStateLow,
        _ => LocKeys.SignalStateUnknown
    });

    /// <summary>
    /// How many times the port has actually been asked for the three levels.
    ///
    /// <para>⛔ <b>The observation point for "it really polls"</b> (spec 4.15, promise 6). Without
    /// it, "the indicator never updated because nothing polls" and "the indicator never updated
    /// because the level never changed" are the same picture on screen and the same green in a
    /// test.</para>
    /// </summary>
    public int PollCount { get; private set; }

    /// <summary>
    /// Reads all three lines in one pass and publishes them.
    ///
    /// <para>⚠️ <b>Runs on the UI thread</b> (that is what <see cref="IPeriodicTimer"/>
    /// guarantees), so the property sets need no marshalling.</para>
    /// </summary>
    private void Poll()
    {
        if (_disposed) return;

        PollCount++;

        var lines = _session.ReadControlLines();
        Cts = lines.Cts;
        Dsr = lines.Dsr;
        Dcd = lines.Dcd;
    }

    // The three state words have no backing field, so the generated setters above do not know
    // to announce them. Without these the dot would change and the word beside it would not --
    // which is precisely the half of promise 5 that colour cannot carry.
    partial void OnCtsChanged(ControlLineState value) => OnPropertyChanged(nameof(CtsText));

    partial void OnDsrChanged(ControlLineState value) => OnPropertyChanged(nameof(DsrText));

    partial void OnDcdChanged(ControlLineState value) => OnPropertyChanged(nameof(DcdText));

    /// <summary>
    /// Pushes a checkbox change onto the wire (spec 4.15, promise 3: changes take effect
    /// immediately).
    ///
    /// <para>⚠️ <b>Fire-and-forget is deliberate</b>: awaiting from a property setter would need
    /// an async void, which is worse. What that costs, and why it is bounded today, is on
    /// <see cref="Push"/> — it is not as simple as this comment used to claim.</para>
    /// </summary>
    partial void OnDtrAssertedChanged(bool value) => Push(SerialOutputLine.Dtr, value);

    partial void OnRtsAssertedChanged(bool value)
    {
        // Flow control owns the line; the checkbox is read-only in the view, and this is the
        // guard behind that. Writing anyway would either be overwritten by the driver or break
        // the handshake -- see SystemIoSerialPort.ApplyOutputLines.
        if (IsRtsOwnedByFlowControl) return;

        Push(SerialOutputLine.Rts, value);
    }

    /// <summary>
    /// ⛔ <b>What the discarded task can carry, stated exactly</b> (P2-86, 2026-08-08).
    ///
    /// <para>This comment used to read "<c>SetOutputLineAsync</c> swallows and logs its own I/O
    /// failures, so there is no outcome here to await or report". <b>That is false as written.</b>
    /// The catch inside <c>SystemIoSerialPort.SetOutputLineAsync</c> wraps
    /// <c>ApplyOutputLines</c> only — the one call that actually touches the driver. Two throws
    /// sit outside it and would land on the discarded task above, where .NET drops them: the
    /// user ticks the box, nothing reaches the wire, and <b>the log stays empty</b>.</para>
    ///
    /// <para>⚠️ <b>Both are unreachable today, and the two reasons are not equally strong. That
    /// difference is the whole point of writing this down:</b></para>
    ///
    /// <list type="bullet">
    /// <item><description><b><c>ObjectDisposedException</c> — held off by an ordering in
    /// another file, and it is brittle.</b> <c>SessionViewModel.DisposeAsync</c> calls
    /// <c>SignalPanel?.Dispose()</c> <i>before</i> <c>await _capture.DisposeAsync()</c>, so
    /// <c>_disposed</c> above is already true by the time the port could reject the call.
    /// ⛔ Nothing binds the two files together: swap those two lines and this paragraph
    /// silently becomes a lie. The comment there now says so.</description></item>
    /// <item><description><b><c>ArgumentOutOfRangeException</c> — held off by the compiler, and
    /// that is solid.</b> <c>SerialOutputLine</c> has exactly two members, and the only two
    /// calls into this method pass one each as a literal. The <c>default:</c> arm of the switch
    /// inside the port is dead unless someone adds a third member, which is a change the
    /// compiler puts in front of them anyway.</description></item>
    /// </list>
    ///
    /// <para>⭐ Two alternatives were on the table and were rejected with their costs measured:
    /// logging the faulted task from here (this class holds no logger, so it would pull one
    /// through the constructor and the DI registration to report something that cannot happen),
    /// and widening the port's try to cover both throws (that changes
    /// <c>SetOutputLineAsync</c>'s contract from "throws when used after disposal" to "silently
    /// does nothing", taking a real signal away from every other caller). See 00-STATUS
    /// P2-86.</para>
    /// </summary>
    private void Push(SerialOutputLine line, bool asserted)
    {
        if (_disposed) return;

        _ = _session.SetOutputLineAsync(line, asserted);
    }

    /// <summary>
    /// ⚠️ <b>The hook, not an override of <c>Dispose</c></b> — see
    /// <see cref="LocalizedViewModelBase.DisposeCore"/> for why the base class does it this way.
    /// </summary>
    protected override void DisposeCore()
    {
        _disposed = true;

        _poll.Stop();
        _poll.Dispose();
    }
}
