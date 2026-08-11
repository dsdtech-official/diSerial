using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiSerial.App.Localization;
using DiSerial.App.Services;
using DiSerial.App.ViewModels.Sessions;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiSerial.App.ViewModels.Panels;

/// <summary>
/// The send panel (T-01 / T-02 / T-03a).
///
/// Safety design (M-09): in a monitoring session IsSendEnabled defaults to false.
/// The diDatatracker is a passive tap, but its two virtual COM ports are physically
/// connected to both sides of the bus, so writing to one really does inject data into a
/// live industrial bus and can disturb equipment in production.
/// A monitoring session therefore requires an explicit confirmation from the user before
/// sending is enabled, and keeps a visual warning on screen for as long as it stays on.
/// </summary>
public sealed partial class SendPanelViewModel : ViewModelBase
{
    /// <summary>
    /// How many entries the dropdown shows. The store keeps far more (100).
    ///
    /// ⚠️ <b>20 → 12 on 2026-08-02, and the number is measured, not chosen for looks.</b> With
    /// 20 entries the popup was 622px tall, <b>scrolled at 16 rows</b>, flipped itself above the
    /// control because it did not fit below, and <b>covered the entire frame display</b> up to
    /// the menu bar. Rows are 39px. Twelve entries plus the "clear all" row is 13 rows ≈ 507px,
    /// which stays under that ~16-row scrolling threshold with room to spare.
    /// </summary>
    private const int MaxHistory = 12;

    /// <summary>
    /// <paramref name="logger"/> is optional and defaults to <c>NullLogger</c>, so unit tests
    /// can still <c>new</c> this up directly (the same approach used when five classes were
    /// given loggers in P1-23).
    ///
    /// <para><paramref name="timers"/> is required, deliberately. Defaulting it to a no-op
    /// factory would make every timed-send guard pass for the wrong reason -- see
    /// <see cref="IPeriodicTimerFactory"/>.</para>
    /// </summary>
    public SendPanelViewModel(
        IEnumChoiceProvider enumChoices,
        IPeriodicTimerFactory timers,
        ISendHistoryStore history,
        ILogger? logger = null)
    {
        LineEndingChoices = enumChoices.GetChoices<LineEnding>();
        _timers = timers;
        _history = history;
        _logger = logger ?? NullLogger.Instance;

        ReloadHistory();
    }

    private readonly IPeriodicTimerFactory _timers;

    /// <summary>
    /// ⚠️ <b>Required, not optional with a no-op default</b> — same reasoning as
    /// <see cref="IPeriodicTimerFactory"/> above. A silently-defaulted store would let every
    /// persistence test pass without a store ever being involved.
    /// </summary>
    private readonly ISendHistoryStore _history;

    private readonly ILogger _logger;

    /// <summary>Hands a send request to the session. The payload is the bytes to write.</summary>
    public event EventHandler<SendRequestedEventArgs>? SendRequested;

    /// <summary>Asks to enable sending (monitoring sessions only). The session raises the
    /// injection warning and calls ConfirmEnableSend() back once the user accepts.</summary>
    public event EventHandler? EnableSendRequested;

    /// <summary>
    /// Asks for the whole history to be wiped. The session confirms with the user and calls
    /// <see cref="ConfirmClearHistory"/>.
    ///
    /// <b>Same shape as <see cref="EnableSendRequested"/> and for the same reason</b>: this
    /// panel has no dialog service and should not grow one — asking a question is the session's
    /// job, and keeping it that way is what leaves this class testable without a UI.
    /// </summary>
    public event EventHandler? ClearHistoryRequested;

    /// <summary>
    /// The input could not be parsed into bytes (path 4 of P0-2, see 01-spec 4.7).
    ///
    /// <para>⚠️ <b>This used to be a silent <c>return</c></b>: the user pressed send, nothing
    /// happened on screen, and nothing appeared in the log either -- the easiest of all five
    /// paths to run into.</para>
    /// </summary>
    public event EventHandler? InputRejected;

    public IReadOnlyList<LocalizedEnumItem> LineEndingChoices { get; }

    [ObservableProperty]
    private string _input = string.Empty;

    partial void OnInputChanged(string value)
    {
        // Any edit that is not our own drops the browse position, so the next Up starts from
        // the newest entry again. Without this, typing after browsing would leave a stale
        // cursor and the next Up would jump to wherever the user had stopped last time.
        if (!_applyingHistory) _historyCursor = NotBrowsing;
    }

    /// <summary>True parses the input as HEX, false as ASCII.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditLineEnding))]
    private bool _isHexMode;

    [ObservableProperty]
    private LineEnding _lineEnding = LineEnding.None;

    /// <summary>
    /// Whether the line-ending dropdown is usable: not in HEX mode (P1-34) and not frozen by a
    /// running timed send (T-06).
    ///
    /// <para>Computed here rather than combined in XAML so the P1-34 invariant stays assertable
    /// from a unit test -- a multi-binding would only be checkable with an Avalonia runtime,
    /// which this project's App tests deliberately do not have.</para>
    /// </summary>
    public bool CanEditLineEnding => !IsHexMode && CanEditSendContent;

    /// <summary>
    /// <b>Invariant: in HEX mode the line ending is always <see cref="LineEnding.None"/></b>
    /// (P1-34, decided 2026-07-31).
    ///
    /// <para>HEX is a <b>byte-exact</b> way to type input -- what the user writes is what goes
    /// on the wire -- while a line ending is a concept from <b>text</b> protocols. Mixing the
    /// two has a very concrete consequence: send a frame to a Modbus RTU device with an extra
    /// <c>0D 0A</c> on the end and the CRC is wrong, so the device drops it -- while on screen
    /// the user typed six bytes.</para>
    ///
    /// <para><b>How the two hooks divide the work:</b> <see cref="OnIsHexModeChanged"/> covers
    /// the moment of switching into HEX, <see cref="OnLineEndingChanged"/> covers someone
    /// assigning a value while HEX is already on.</para>
    ///
    /// <para>⚠️ <b>The second hook has no caller today, and that is deliberate</b> (user
    /// decision, 2026-07-31). The dropdown is disabled in HEX mode, and send-panel preferences
    /// are no longer persisted (see AppSettingsModel), so there really is no second assignment
    /// path right now. It stays because it turns the promise into an <b>invariant that does
    /// not depend on assignment order</b> -- and it came close to being needed:
    /// <c>ApplyStoredPreferences</c> sets <b>IsHexMode first and LineEnding second</b>, so had
    /// persistence been kept, an old settings.json with <c>HexMode:true</c> and
    /// <c>LineEnding:CrLf</c> would have restored CRLF at startup -- <b>with the dropdown
    /// disabled, leaving the user unable to change it</b>. Whoever adds another assignment
    /// path later (V1.1 storing these two again, say) will not reopen that hole.</para>
    /// </summary>
    partial void OnIsHexModeChanged(bool value)
    {
        if (value) LineEnding = LineEnding.None;
    }

    /// <inheritdoc cref="OnIsHexModeChanged"/>
    partial void OnLineEndingChanged(LineEnding value)
    {
        // The recursion only goes one level deeper: the inner assignment is None itself, at
        // which point the condition no longer holds.
        if (IsHexMode && value != LineEnding.None) LineEnding = LineEnding.None;
    }

    /// <summary>Whether sending is available. Always true for a terminal session; false by
    /// default for a monitoring session.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInjectionWarning))]
    private bool _isSendEnabled = true;

    /// <summary>Whether this is a monitoring session, which decides if the injection-risk
    /// banner is shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInjectionWarning))]
    private bool _isMonitorSession;

    /// <summary>Keeps the amber warning on screen while sending is enabled in a monitoring
    /// session.</summary>
    public bool ShowInjectionWarning => IsMonitorSession && IsSendEnabled;

    /// <summary>
    /// The ports this panel may send to. ⭐ <b>Empty for a terminal session</b>, which has one
    /// port and therefore nothing to choose between — the view hides the picker on that.
    ///
    /// <para>⚠️ <b>Holds <see cref="ChannelViewModel"/> rather than a plain (id, label) pair,
    /// and that is deliberate</b>: the label has to track the alias. A user who renames channel
    /// A to "PLC" mid-session must see that change here too, and a snapshot taken at construction
    /// would silently keep showing the old name on the one control that decides where bytes go.
    /// <c>InlineLabel</c> is the property that already solves this everywhere else.</para>
    /// </summary>
    public ObservableCollection<ChannelViewModel> SendTargets { get; } = [];

    /// <summary>Whether there is anything to choose between — drives the picker's visibility.</summary>
    public bool HasSendTargets => SendTargets.Count > 0;

    /// <summary>
    /// Which port the next send goes to (P1-33).
    ///
    /// <para>⛔ <b>Default: the first port, chosen by the user on 2026-08-05.</b> I recommended
    /// starting unselected and disabling Send until one is picked, on the grounds that this is
    /// injection onto somebody else's live bus and M-09's whole design is "make it hard to do by
    /// accident". That was overruled. ⚠️ <b>The accepted consequence, recorded per 03-conventions
    /// 0.4 rather than argued again</b>: a user who means COM12 and does not look at this control
    /// sends to COM11, and bus injection cannot be undone. The mitigation that remains is that
    /// the picker shows the port name at all times, so the target is on screen rather than
    /// implied.</para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetChannel))]
    private ChannelViewModel? _selectedTarget;

    /// <summary>
    /// ⭐ <b>Derived, not stored</b> (changed 2026-08-05 with P1-33). It used to be an assignable
    /// property that <c>MonitorSessionViewModel</c> hard-coded to <see cref="ChannelId.A"/> — the
    /// defect itself: the capture layer could route to either channel and nothing on screen could
    /// reach the other one. Deriving it means the selection and the destination cannot disagree.
    ///
    /// <para><see cref="ChannelId.None"/> when nothing is selected, which is exactly the terminal
    /// session's case: one port, no picker, and the session ignores the channel anyway.</para>
    /// </summary>
    public ChannelId TargetChannel => SelectedTarget?.Id ?? ChannelId.None;

    /// <summary>
    /// What the dropdown shows: the most recent <see cref="MaxHistory"/> entries.
    ///
    /// ⚠️ <b>The store keeps more than this</b> (100 rows, user decision 2026-08-02). An entry
    /// that falls off the bottom here is still on disk and comes back the next time it is used
    /// — which is also why <see cref="RemoveHistoryEntry"/> reloads instead of just removing
    /// one row: deleting the 20th should reveal the 21st, not leave a shorter list.
    /// </summary>
    public ObservableCollection<SendHistoryItemViewModel> History { get; } = [];

    /// <summary>
    /// The entry picked from the history dropdown. Always reads back as <c>null</c>.
    ///
    /// <para><b>Picking only fills the box; it does not send</b> (01-spec 4.14). Sending on
    /// selection would make a stray arrow-key press put bytes on a live bus.</para>
    ///
    /// <para>⭐ <b>Picking also restores the entry's input mode</b> (user decision 2026-08-02).
    /// This is not a convenience, it is what makes the entry mean anything: <c>01 02</c> stored
    /// as HEX is two bytes, and replaying it while the panel sits in ASCII would put five
    /// completely different bytes on the wire — the same characters, silently a different
    /// command. Restoring the mode is what makes "pick it and send" reproduce the original.</para>
    ///
    /// <para>⚠️ <b>Known side effect, deliberately accepted</b>: picking a HEX entry runs
    /// <see cref="OnIsHexModeChanged"/>, which forces the line ending to
    /// <see cref="LineEnding.None"/> (P1-34) — and switching back to ASCII does not bring it
    /// back. Documented in 01-spec 4.14 rather than worked around, because the alternative
    /// (restoring the line ending too) is a separate decision that has not been made.</para>
    ///
    /// <para><b>Why it resets itself to null.</b> The dropdown is a picker, not a piece of
    /// state -- nothing downstream should be able to ask "which history entry is selected".
    /// Resetting also makes picking the same entry twice in a row work, which a sticky
    /// selection would silently swallow. The re-entrant assignment settles after one hop,
    /// exactly like <see cref="OnLineEndingChanged"/>.</para>
    /// </summary>
    [ObservableProperty]
    private SendHistoryItemViewModel? _selectedHistoryEntry;

    partial void OnSelectedHistoryEntryChanged(SendHistoryItemViewModel? value)
    {
        if (value is null) return;

        // Reset first: every path out of here must leave the picker unselected, including the
        // timed-send bail-out below.
        SelectedHistoryEntry = null;

        // ⚠️ A timed send freezes the payload AND the mode (01-spec 4.14 promise 3). Picking
        // would change both, so it must not apply here. The dropdown is disabled in that state
        // too; this guard is what makes the promise hold regardless of the view.
        if (!CanEditSendContent) return;

        // The last row is the "clear all" action, not an entry. It must never touch the input
        // box — see SendHistoryItemViewModel for why it lives in the list at all.
        if (value.IsClearAction)
        {
            ClearHistoryRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        IsHexMode = value.IsHexMode;
        Input = value.Text;
    }

    // ---- Up / Down browse the history (P2-51 A2, 01-spec 4.14 promise 6) ----

    /// <summary>Cursor value meaning "not browsing": the box holds whatever the user put there.</summary>
    private const int NotBrowsing = -1;

    private int _historyCursor = NotBrowsing;

    /// <summary>
    /// Set while <see cref="ApplyHistoryEntry"/> writes <see cref="Input"/>, so that
    /// <see cref="OnInputChanged"/> can tell our own write apart from the user typing.
    /// </summary>
    private bool _applyingHistory;

    /// <summary>
    /// Index of the oldest real entry, or <see cref="NotBrowsing"/> when there are none.
    ///
    /// ⚠️ <b>The "clear all" row is pinned last and is not an entry</b> — the arrows must never
    /// land on it (it is an action, and selecting it would wipe the history). Same reasoning as
    /// the guard in <see cref="OnSelectedHistoryEntryChanged"/>; here it is expressed as a
    /// bound rather than a check, so there is no path that can reach it at all.
    /// </summary>
    private int LastEntryIndex =>
        History.Count == 0 ? NotBrowsing
        : History[^1].IsClearAction ? History.Count - 2
        : History.Count - 1;

    /// <summary>
    /// Up — one step towards <b>older</b> entries. From a fresh box this lands on the newest.
    /// </summary>
    [RelayCommand]
    private void HistoryOlder() => MoveHistory(+1);

    /// <summary>
    /// Down — one step towards <b>newer</b> entries.
    /// </summary>
    [RelayCommand]
    private void HistoryNewer() => MoveHistory(-1);

    /// <summary>
    /// Moves the browse cursor and copies that entry into the input box.
    ///
    /// <para><b>Both ends stop; neither wraps</b> (user decision 2026-08-03). Wrapping would let
    /// "hold Up" quietly loop back to the newest entry with nothing on screen saying it had
    /// reached the end.</para>
    ///
    /// <para>⛔ <b>Whatever the user had half-typed is overwritten and does not come back</b>
    /// (user decision 2026-08-03, option A). ⚠️ <b>I recommended the opposite</b> — keeping the
    /// draft and restoring it on the way back down, the way a shell and the browser address bar
    /// do — and was overruled; recording it here per 03-conventions 0.4 rather than dropping it.
    /// <b>The consequence to know:</b> once Up has been pressed there is no keyboard route back
    /// to an empty box, because Down stops at the newest entry rather than stepping past it.
    /// Clearing means selecting the text and deleting it.</para>
    ///
    /// <para>⚠️ <b>Refused while a timed send runs</b>, like every other way of changing the
    /// payload: <see cref="CanEditSendContent"/> is the single place that rule lives (01-spec
    /// 4.14 promise 3). Enter, the Send button and the dropdown all defer to the same flag.</para>
    /// </summary>
    private void MoveHistory(int delta)
    {
        if (!CanEditSendContent) return;

        var last = LastEntryIndex;
        if (last < 0) return;

        var target = _historyCursor + delta;
        if (target < 0 || target > last) return;

        _historyCursor = target;
        ApplyHistoryEntry(History[target]);
    }

    /// <summary>
    /// ⭐ <b>Restores the entry's mode as well as its text</b> — the same thing picking from the
    /// dropdown does (user decision 2026-08-03, and see
    /// <see cref="OnSelectedHistoryEntryChanged"/> for why the mode is not decoration:
    /// <c>01 02</c> as HEX is two bytes, as ASCII it is five). <b>One record reached two ways
    /// has to give the same result</b>, or neither way can be trusted.
    /// </summary>
    private void ApplyHistoryEntry(SendHistoryItemViewModel entry)
    {
        _applyingHistory = true;
        try
        {
            IsHexMode = entry.IsHexMode;
            Input = entry.Text;
        }
        finally
        {
            _applyingHistory = false;
        }
    }

    /// <summary>
    /// Total entries on disk — <b>not <see cref="History"/>.Count</b>. The confirmation prompt
    /// quotes this, and the gap is the reason it has to: the user sees a dozen rows while the
    /// table holds up to a hundred.
    /// </summary>
    public int StoredHistoryCount => _history.Count();

    /// <summary>
    /// Wipes the history after the session has confirmed with the user.
    ///
    /// ⚠️ <b>Deletes first, then rebuilds the list from the store</b> — never clears the
    /// collection directly. If the delete fails it throws (the store deliberately does not
    /// swallow this one), the caller reports it, and <b>the rows stay on screen because they
    /// are still on disk</b>. Clearing the view first would show an empty list over a full
    /// table, i.e. the tool lying about the one thing this feature exists to do.
    /// </summary>
    public void ConfirmClearHistory()
    {
        _history.Clear();
        ReloadHistory();
    }

    /// <summary>
    /// Drops one entry from disk, then rebuilds the visible list so the next-oldest entry
    /// takes its place. Wired to each row's delete button.
    /// </summary>
    private void RemoveHistoryEntry(SendHistoryItemViewModel item)
    {
        _history.Delete(item.Text, item.IsHexMode);
        ReloadHistory();
    }

    private void ReloadHistory()
    {
        History.Clear();

        foreach (var entry in _history.Load(MaxHistory))
        {
            History.Add(new SendHistoryItemViewModel(entry.Text, entry.IsHexMode, RemoveHistoryEntry));
        }

        // ⚠️ Only when there is something to clear. An empty dropdown offering "clear all" is
        // an action that cannot do anything — the "looks operable, does nothing" shape this
        // project deleted M-03 over.
        if (History.Count > 0) History.Add(SendHistoryItemViewModel.ClearAction());
    }

    [RelayCommand]
    private void Send()
    {
        if (!IsSendEnabled || string.IsNullOrEmpty(Input)) return;

        // Refused while a timed send runs (user decision 2026-08-03). The input box is
        // already read-only and the payload was frozen at start, so a manual frame here
        // would go out carrying the snapshot rather than anything the user can see and
        // edit. The view greys the button out too -- this guard is the promise, that is
        // the convenience.
        if (!CanEditSendContent) return;

        if (!TryParse(Input, IsHexMode, LineEnding, out var payload, _logger))
        {
            InputRejected?.Invoke(this, EventArgs.Empty);
            return;
        }

        // ⚠ The box is NOT cleared here. Clearing is the session's call, because only the
        // session knows whether the bytes actually went out -- see ConfirmSent below and
        // P2-58. `SendRequested` is fire-and-forget: by the time it returns, a rejection may
        // not have happened yet (path 2 is caught after an await) or may have happened
        // downstream where this class cannot see it (path 5).
        //
        // History is still recorded unconditionally, which is deliberate and unchanged: a
        // payload the user tried to send is worth keeping whether or not the wire took it.
        SendRequested?.Invoke(this, new SendRequestedEventArgs(TargetChannel, payload, Input));
        PushHistory(Input);
    }

    /// <summary>
    /// Called by the session once the payload really went out (01-spec 4.14, promise ②:
    /// "cleared after a SUCCESSFUL send only").
    /// </summary>
    /// <remarks>
    /// <para><b>Why it takes the text instead of just clearing.</b> A write is asynchronous. The
    /// user can type the next command while the previous one is still on the wire, and clearing
    /// "the input box" at completion time would delete something that was never sent. Comparing
    /// first makes the clear apply to the text that was actually delivered, or to nothing.</para>
    ///
    /// <para><b>Why <c>null</c> means "clear nothing".</b> Timed-send ticks replay a frozen
    /// snapshot rather than the box, and the box is read-only for the duration -- a tick must
    /// never touch it. Passing the text through the event args makes that distinction structural
    /// instead of a runtime check the next call site could forget.</para>
    /// </remarks>
    public void ConfirmSent(string? sentText)
    {
        // ⚠ This line is expressive, not load-bearing, and mutation testing said so: deleting it
        // changes no behaviour, because the comparison below already declines null (Input is
        // never null, and `"frozen" == null` is false). It stays because the contract above
        // promises "null clears nothing" and the code should say that out loud -- but do not
        // mistake it for a guard some test is holding down. The case that holds the *contract*
        // down is ConfirmingWithNoSourceText_ClearsNothing, which survives either spelling.
        if (sentText is null) return;

        if (Input == sentText) Input = string.Empty;
    }

    /// <summary>
    /// Enter in the input box (user decision 2026-08-03, terminal sessions only).
    /// </summary>
    /// <remarks>
    /// ⛔ <b>Monitor sessions are excluded on purpose.</b> There, sending is injection into a
    /// live industrial bus, and M-09's whole design is to make that deliberate rather than
    /// convenient -- Enter is the easiest key in the world to press by accident. A monitor
    /// session still sends through the button.
    ///
    /// <b>The exclusion lives here rather than in the view</b> so that it is a property of the
    /// panel and not of one XAML file: a second call site cannot re-enable it by forgetting.
    /// </remarks>
    [RelayCommand]
    private void SendFromEnter()
    {
        if (IsMonitorSession) return;
        Send();
    }

    // ---- Timed send (T-06, 01-spec 4.14) ----

    /// <summary>
    /// Lower bound for the interval. Values below this are <b>refused</b>, never clamped --
    /// see <see cref="StartTimedSendCommand"/>.
    /// </summary>
    public const int MinIntervalMs = 20;

    /// <summary>Interval a fresh session starts with.</summary>
    public const int DefaultIntervalMs = 1000;

    private IPeriodicTimer? _timedSend;

    /// <summary>
    /// The bytes frozen at start. Non-null exactly while <see cref="IsTimedSendRunning"/>.
    /// </summary>
    private byte[]? _timedPayload;

    /// <summary>
    /// Raised when a start was refused because the interval is below <see cref="MinIntervalMs"/>.
    /// The session turns it into the error banner (01-spec 4.7).
    /// </summary>
    public event EventHandler? IntervalRejected;

    /// <summary>
    /// Raised when a start was refused because there is nothing in the box (P2-50 ②).
    /// The session turns it into the error banner (01-spec 4.7).
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Deliberately not raised by the manual send path.</b> Tapping Enter on an empty box
    /// is an ordinary thing to do and a banner there would be noise; pressing "start timed send"
    /// is a deliberate act that must not do nothing. The asymmetry is the point.
    /// </remarks>
    public event EventHandler? PayloadMissing;

    [ObservableProperty]
    private int _intervalMs = DefaultIntervalMs;

    /// <summary>True while the timer is running.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditSendContent))]
    [NotifyPropertyChangedFor(nameof(CanEditLineEnding))]
    private bool _isTimedSendRunning;

    /// <summary>
    /// False while a timed send is running, which freezes the input box, the ASCII/HEX choice
    /// and the line ending.
    ///
    /// <para><b>Why all three and not just the text.</b> The payload is a snapshot taken at
    /// start. Leaving ASCII/HEX switchable would let the screen say HEX while the wire keeps
    /// carrying the ASCII bytes frozen earlier -- the display and the wire would stop having a
    /// single source, which is the whole point of the snapshot rule (01-spec 4.14, promise 3),
    /// and it is the "the tool is lying" shape this project treats as worse than a crash.</para>
    /// </summary>
    public bool CanEditSendContent => !IsTimedSendRunning;

    /// <summary>
    /// Starts the timed send.
    ///
    /// <para><b>The first payload goes out immediately</b> (01-spec 4.14, question 2 answered
    /// 2026-08-02), matching C-09 recording: pressing start does something visible at once.</para>
    /// </summary>
    [RelayCommand]
    private void StartTimedSend()
    {
        if (IsTimedSendRunning) return;

        // Belt and braces for M-09's fifth constraint. A monitor session has no timed-send UI
        // at all, so this is unreachable from the screen -- but "unreachable from the screen"
        // is exactly the assumption P2-8 recorded getting wrong, so the model refuses too.
        if (!IsSendEnabled || IsMonitorSession) return;

        // Refuse, do not clamp (01-spec 4.14, promise 5). Silently substituting 20 would leave
        // the box reading 5 while the wire ran at 20 -- the tool lying about what it is doing.
        if (IntervalMs < MinIntervalMs)
        {
            IntervalRejected?.Invoke(this, EventArgs.Empty);
            return;
        }

        // P2-50 ②: this used to be a silent `return` -- pressed the button, nothing happened,
        // no banner, no log. That is the "looks operable, does nothing" shape this project
        // deleted M-03 over, and answering P2-50 ① with "keep clearing the box after a send"
        // turned it from a corner case into the end of a normal workflow.
        if (string.IsNullOrEmpty(Input))
        {
            PayloadMissing?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!TryParse(Input, IsHexMode, LineEnding, out var payload, _logger))
        {
            InputRejected?.Invoke(this, EventArgs.Empty);
            return;
        }

        _timedPayload = payload;
        IsTimedSendRunning = true;

        _timedSend = _timers.Create(TimeSpan.FromMilliseconds(IntervalMs), FireTimedSend);
        _timedSend.Start();

        // Recorded once, at start -- not on every tick. What the user chose to send is one
        // history entry regardless of how many copies of it go out.
        PushHistory(Input);

        FireTimedSend();
    }

    /// <summary>
    /// Stops the timed send. <b>Idempotent</b> -- all three mandatory stops
    /// (disconnect, send failure, session disposal) may fire for the same run.
    /// </summary>
    public void StopTimedSend()
    {
        _timedSend?.Stop();
        _timedSend?.Dispose();
        _timedSend = null;
        _timedPayload = null;
        IsTimedSendRunning = false;
    }

    [RelayCommand]
    private void StopTimedSendFromUi() => StopTimedSend();

    /// <summary>
    /// One tick. Sends the snapshot, never the current text -- re-reading
    /// <see cref="Input"/> here would defeat the freeze.
    /// </summary>
    private void FireTimedSend()
    {
        // Second line of defence behind Stop()'s disposal. A test fake is required to fire
        // regardless of its own state (see IPeriodicTimerFactory), so this guard is what a
        // "nothing is sent after Stop()" assertion actually exercises.
        if (!IsTimedSendRunning || _timedPayload is null) return;

        SendRequested?.Invoke(this, new SendRequestedEventArgs(TargetChannel, _timedPayload));
    }

    /// <summary>Handles a click on "enable sending" in a monitoring session by asking the
    /// session to raise the confirmation.</summary>
    [RelayCommand]
    private void RequestEnableSend() => EnableSendRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>The user has confirmed they understand the injection risk.</summary>
    public void ConfirmEnableSend() => IsSendEnabled = true;

    /// <summary>
    /// Records what was just sent: move an existing entry to the front, otherwise insert at the
    /// front and trim to the cap.
    ///
    /// <para><b>Uses <see cref="ObservableCollection{T}.Move"/> rather than Remove + Insert.</b>
    /// The original reason was load-bearing and is worth keeping on record: history used to be
    /// bound to an <i>editable</i> <c>ComboBox</c> together with the input text (T-03a), and a
    /// Remove pulled the currently selected item out of the collection, which nulled the
    /// control's <c>SelectedItem</c> and -- in editable mode -- wiped <c>Text</c>. The symptom
    /// was "picking an entry from the dropdown and sending clears the input box, but typing the
    /// same text and sending does not": one Send, two behaviours, depending on how the text got
    /// in. Measured on 2026-07-29 by comparing both input paths, not deduced.</para>
    ///
    /// <para>⚠️ <b>That coupling is gone as of 2026-08-02</b> -- input is a plain
    /// <c>TextBox</c> and history is its own dropdown whose selection resets to null
    /// (see <see cref="SelectedHistoryEntry"/>), so the class of defect no longer exists
    /// structurally. Move stays because it is still the correct operation: it reorders without
    /// pretending an item left and a different one arrived.</para>
    /// </summary>
    private void PushHistory(string text)
    {
        var isHex = IsHexMode;

        // Persist first, in-memory second. The store is the durable copy; the collection below
        // is only what this panel currently shows.
        //
        // ⚠️ The line ending is handed over as plain text and is recorded, never replayed —
        // see ISendHistoryStore. Whether picking should restore it as well is an open question,
        // and this column is what will let it be answered with data.
        _history.Record(text, isHex, LineEnding.ToString());

        var existing = IndexOf(text, isHex);
        if (existing >= 0)
        {
            if (existing != 0) History.Move(existing, 0);
            return;
        }

        History.Insert(0, new SendHistoryItemViewModel(text, isHex, RemoveHistoryEntry));

        // ⚠️ The clear row is pinned last and is not an entry: it must not be counted towards
        // the cap and must not be what the trim removes. Everything below therefore works on
        // "entries" = Count - 1 once it exists.
        if (!History[^1].IsClearAction) History.Add(SendHistoryItemViewModel.ClearAction());

        while (History.Count - 1 > MaxHistory) History.RemoveAt(History.Count - 2);
    }

    /// <summary>
    /// ⚠️ <b>Identity is (text, mode), matching the store.</b> Comparing text alone would fold
    /// a HEX entry and an ASCII entry with the same characters onto one row, and since picking
    /// restores the mode, the survivor would silently redefine what the other one sent.
    /// </summary>
    private int IndexOf(string text, bool isHexMode)
    {
        for (var i = 0; i < History.Count; i++)
        {
            if (!History[i].IsClearAction
                && History[i].IsHexMode == isHexMode
                && string.Equals(History[i].Text, text, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Parses the input text into the bytes to send. In HEX mode spaces and the common
    /// separators are ignored.
    ///
    /// <b>Input semantics of the two modes (01-spec 4.11):</b>
    /// <list type="table">
    ///   <item><term>HEX</term><description>Byte-exact. <b>No line ending is appended</b>: the
    ///     caller guarantees the <paramref name="lineEnding"/> passed in is always
    ///     <see cref="LineEnding.None"/>, per the invariant on
    ///     <see cref="OnIsHexModeChanged"/></description></item>
    ///   <item><term>ASCII</term><description><b>Accepts 0x00-0x7F only</b>; a character outside
    ///     that range rejects the whole line</description></item>
    /// </list>
    ///
    /// <para>⚠️ <b>This method does not special-case the suffix for HEX.</b> The promise that
    /// HEX carries no line ending is kept by the <b>value</b> (the dropdown is forced to None
    /// and disabled), not by a branch in here -- so what the user sees on screen and what
    /// happens on the wire come from one source and cannot disagree.</para>
    ///
    /// <para><paramref name="logger"/> is optional: this method is <c>static</c> and has no
    /// instance fields to reach, while shared rule 1 of 01-spec 4.7 requires the catch site
    /// itself to leave a trace -- so the caller passes one in. Omitting it gives
    /// <c>NullLogger</c>, which keeps direct calls from unit tests unaffected.</para>
    /// </summary>
    /// <summary>
    /// Cleans up HEX input: drops separators and strips a <c>0x</c> prefix from <b>the start
    /// of each token</b>.
    ///
    /// <para>⛔⭐ <b>That "start of each token" is the entire reason this method exists</b>
    /// (P2-88, 2026-08-08). It used to be a single
    /// <c>Replace("0x", "", OrdinalIgnoreCase)</c> -- <b>unanchored</b>, so it did not care
    /// where the <c>0x</c> appeared. Measured: <c>A0XB</c> was cleaned into <c>AB</c> and a
    /// byte <c>0xAB</c> really went out on the wire; <c>410x42</c> became <c>4142</c>.
    /// ⚠️ <b>The user typed invalid input and got back not a rejection but a different
    /// byte.</b></para>
    ///
    /// <para>⭐ <b>This is the same reasoning as the comment on the ASCII branch</b>, which
    /// says that silent substitution is worse than rejection because it makes the user believe
    /// the right thing was sent -- ⛔ <b>and the HEX branch was doing exactly that</b>.
    /// On 2026-08-08 the scope of promise 2 in 01-spec 4.11 was widened from "ASCII mode" to
    /// <b>both modes</b>, and this method is where that lands on the HEX side.</para>
    ///
    /// <para>⚠️ <b>Deliberately preserved:</b> <c>0x41 0x42</c> (the format people paste out
    /// of a datasheet) keeps working, and <c>4 1</c> is still the single byte <c>0x41</c> --
    /// <b>separators are still merely ignored and do not form token boundaries</b>.
    /// ⛔ Treating a token as a byte was the other option (P2-88's option B) and was not
    /// taken, because it would change input that is legal today.</para>
    /// </summary>
    internal static string StripSeparatorsAndHexPrefixes(string input)
    {
        var cleaned = new StringBuilder(input.Length);

        // ⭐ True at the very start and right after any separator -- the only two places a 0x
        // prefix may sit. Everywhere else a '0' followed by an 'x' is just bad input.
        var atTokenStart = true;

        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];

            if (c is ' ' or '-' or ',')
            {
                atTokenStart = true;
                continue;
            }

            if (atTokenStart
                && c == '0'
                && i + 1 < input.Length
                && input[i + 1] is 'x' or 'X')
            {
                i++;                  // the 'x' goes with it
                atTokenStart = false; // ⛔ so "0x0x41" does not strip twice; it stays invalid
                continue;
            }

            cleaned.Append(c);
            atTokenStart = false;
        }

        return cleaned.ToString();
    }

    public static bool TryParse(
        string input, bool isHex, LineEnding lineEnding, out byte[] payload, ILogger? logger = null)
    {
        payload = [];
        try
        {
            byte[] body;
            if (isHex)
            {
                var cleaned = StripSeparatorsAndHexPrefixes(input);
                if (cleaned.Length == 0 || cleaned.Length % 2 != 0) return false;

                body = new byte[cleaned.Length / 2];
                for (var i = 0; i < body.Length; i++)
                {
                    body[i] = byte.Parse(
                        cleaned.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                }
            }
            else
            {
                // ASCII means ASCII (P1-35, decided 2026-07-31): anything outside 0x00-0x7F
                // rejects the whole line.
                //
                // ⚠️ Encoding.ASCII.GetBytes must NOT be used here -- it silently substitutes
                // '?' for out-of-range characters: the user believes they sent two Chinese
                // characters while `3F 3F` goes out on the wire, with nothing on screen to say
                // so. Silent substitution is worse than rejection.
                //
                // Checking char by char is enough: both code units of a surrogate pair (an
                // emoji) are > 0x7F, so those are rejected too.
                foreach (var c in input)
                {
                    if (char.IsAscii(c)) continue;

                    // Log the code point only, never the text itself -- that is enough to tell
                    // a BOM from a CJK character.
                    // Debug rather than Warning, for the same reason as the HEX parse failure
                    // below: this runs every single time the user mistypes.
                    (logger ?? NullLogger.Instance).LogDebug(
                        "Rejected a non-ASCII character (U+{CodePoint:X4}) in the send input.", (int)c);
                    return false;
                }

                body = Encoding.ASCII.GetBytes(input);
            }

            var suffix = lineEnding switch
            {
                LineEnding.Cr => "\r"u8.ToArray(),
                LineEnding.Lf => "\n"u8.ToArray(),
                LineEnding.CrLf => "\r\n"u8.ToArray(),
                _ => []
            };

            payload = suffix.Length == 0 ? body : [.. body, .. suffix];
            return true;
        }
        catch (FormatException e)
        {
            // Returning false makes the caller raise InputRejected -> SessionViewModel.Report
            // Error, and that is what puts a message on screen (P0-2, path 4). The Debug line
            // here exists only to preserve the text that failed to parse: shared rule 1 of
            // 01-spec 4.7 requires the catch site to leave its own trace rather than rely on
            // the caller.
            //
            // ⚠️ Debug rather than Warning: this runs every time the user mistypes HEX, so it
            // would fire over and over.
            (logger ?? NullLogger.Instance).LogDebug(e, "Failed to parse the send input as bytes.");
            return false;
        }
    }
}

/// <summary>
/// One row in the send-history dropdown: what was sent, which mode produced the bytes, and a
/// way to forget it.
///
/// <b>Immutable apart from the command.</b> Nothing edits an entry — a changed command is a
/// different entry — so there is no observable state here and nothing to notify about.
///
/// ⭐ <b>Why every row carries a delete button</b> (user decision 2026-08-02): send history is
/// the one place in this application that writes payload to disk without being asked
/// (see <see cref="ISendHistoryStore"/>). Something that records what you sent to a customer's
/// bus, permanently, has to come with a way to take it back — and per-row is stronger than a
/// single "clear everything", because the usual reason to want an entry gone is that <i>that
/// one</i> should not have been kept.
/// </summary>
public sealed partial class SendHistoryItemViewModel(
    string text, bool isHexMode, Action<SendHistoryItemViewModel>? delete) : ObservableObject
{
    /// <summary>
    /// The "clear everything" row, pinned to the bottom of the dropdown (user decision
    /// 2026-08-02).
    ///
    /// ⚠️ <b>It is an item in the list, and that has a cost the user accepted knowingly.</b>
    /// I argued for putting it outside the dropdown — as a list item it is reachable by
    /// keyboard, so arrow-down to the end plus Enter destroys up to a hundred entries, and it
    /// mixes an action into a list of values. The decision was that this is not a critical
    /// feature and good enough is good enough. <b>The confirmation prompt is what carries the
    /// safety instead</b>, which is why it quotes the real stored count rather than the dozen
    /// rows on screen.
    ///
    /// Recorded here rather than dropped, per 03-conventions 0.4: an overruled concern becomes
    /// an explicit clause. If the prompt is ever weakened, this is the reason it must not be.
    /// </summary>
    public static SendHistoryItemViewModel ClearAction() => new(string.Empty, false, null)
    {
        IsClearAction = true
    };

    /// <summary>
    /// True only for the row above. Everything that walks the list — de-duplication, the cap,
    /// picking — has to skip it, because it is not a history entry.
    /// </summary>
    public bool IsClearAction { get; private init; }

    public string Text { get; } = text;

    /// <summary>
    /// Restored into the panel when this row is picked — that is what makes the row reproduce
    /// the bytes it originally sent rather than the same characters in whatever mode happens
    /// to be active. Also half of the row's identity; see <c>SendPanelViewModel.IndexOf</c>.
    /// </summary>
    public bool IsHexMode { get; } = isHexMode;

    /// <summary>
    /// ⚠️ Exists only so the two mode tags in the item template can bind their visibility.
    /// The tag text itself must come from resources (XamlTextConventionTests), so the view
    /// carries two <c>TextBlock</c>s and shows one — a single string property here would put
    /// user-visible text in source, which SourceConventionTests forbids.
    /// </summary>
    public bool IsAsciiMode => !IsHexMode && !IsClearAction;

    /// <summary>The mode tag and the delete button belong to entries only.</summary>
    public bool IsEntry => !IsClearAction;

    [RelayCommand]
    private void Delete() => delete?.Invoke(this);
}

public enum LineEnding
{
    None,
    Cr,
    Lf,
    CrLf
}

public sealed class SendRequestedEventArgs(
    ChannelId channel, ReadOnlyMemory<byte> data, string? sourceText = null) : EventArgs
{
    public ChannelId Channel { get; } = channel;

    public ReadOnlyMemory<byte> Data { get; } = data;

    /// <summary>
    /// The input-box text these bytes were parsed from, or <c>null</c> when the send did not come
    /// from the box (a timed-send tick replays a frozen snapshot).
    ///
    /// <para>It exists so the session can tell the panel <b>which</b> text was actually delivered
    /// -- see <see cref="SendPanelViewModel.ConfirmSent"/>. Clearing "the input box" without
    /// naming the text would race with the user typing the next command while the write is still
    /// in flight.</para>
    /// </summary>
    public string? SourceText { get; } = sourceText;
}
