using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiSerial.App.Localization;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;

namespace DiSerial.App.ViewModels.Sessions;

/// <summary>
/// The dual-port monitoring session -- the core of V1.0, and the only reason this software
/// cannot simply be replaced.
///
/// Showing each port's data in its own pane would be no better than opening two PuTTY
/// windows; the software would have no reason to exist. The value is the <b>merged
/// timeline</b>: both directions merged into a single view in time order, coloured by
/// origin, reconstructing one complete conversation.
/// </summary>
public sealed partial class MonitorSessionViewModel : SessionViewModel
{
    public MonitorSessionViewModel(
        SerialChannelPair pair,
        SerialPortSettings settings,
        ICaptureSession capture,
        ISessionRecorder recorder,
        SessionContext context)
        : base(capture, recorder, context)
    {
        Settings = settings;

        // Aliases are no longer prefilled by the dialog; each starts out equal to its port
        // name (the P0-9 fix, see ChannelViewModel). Which port is which side of the bus
        // cannot be known when the session is created, so the user watches a few seconds of
        // traffic and renames afterwards.
        ChannelA = new ChannelViewModel(ChannelId.A, pair.ChannelA);
        ChannelB = new ChannelViewModel(ChannelId.B, pair.ChannelB);

        // A rename has to reach three places: the status bar and the title (both computed
        // properties), and the frame rows already on screen (P1-41).
        Subscribe(OnChannelPropertyChanged,
            h => ChannelA.PropertyChanged += h, h => ChannelA.PropertyChanged -= h);
        Subscribe(OnChannelPropertyChanged,
            h => ChannelB.PropertyChanged += h, h => ChannelB.PropertyChanged -= h);
        ApplyChannelPlaceholders();

        // ⚠️ Safe defaults (M-09): sending is disabled by default.
        // These three are deliberately NOT persisted -- injecting data onto a live
        // industrial bus can disturb production equipment, so "it was on last time,
        // therefore it is on now" is an unacceptable default. Every new monitor session
        // has to enable it explicitly again.
        // (The store is settings.db since 2026-08-07; it was settings.json before. What
        // matters here is that these never enter it, whatever it is called.)
        SendPanel.IsMonitorSession = true;
        SendPanel.IsSendEnabled = false;

        // P1-33: both channels are offered, and the destination is picked by port name rather
        // than by "A" or "B" -- the A/B labels stopped being user-facing with M-05a, and the
        // thing the user actually knows is which COM port is wired where.
        //
        // This line used to be `SendPanel.TargetChannel = ChannelId.A`, which is the whole of
        // P1-33: the capture layer routes to either channel, and nothing on screen could reach
        // the second one.
        SendPanel.SendTargets.Add(ChannelA);
        SendPanel.SendTargets.Add(ChannelB);

        // Default: the first port (user decision 2026-08-05; the alternative and its accepted
        // cost are recorded on SendPanelViewModel.SelectedTarget).
        SendPanel.SelectedTarget = ChannelA;
        // ⭐ This one is why P2-44 exists: it was the only subscription in the whole project
        // without a matching removal, and nothing failed because of it. Registering the
        // removal here is now the only way to subscribe at all.
        Subscribe(OnEnableSendRequested,
            h => SendPanel.EnableSendRequested += h, h => SendPanel.EnableSendRequested -= h);

        // Display and send preferences come from what was remembered last time (defaults:
        // absolute timestamps, channel column, delta column -- without those two columns a
        // merged timeline cannot show who spoke when).
        // This has to come last: any earlier and the lines above would overwrite it.
        ApplyStoredPreferences();
    }

    public SerialPortSettings Settings { get; }

    /// <summary>
    /// Aliases are stored with the batch: opening one six months later, "PLC / HMI" is far
    /// more useful than "COM2 / COM4".
    /// <para>⚠️ The alias captured is the one in effect <b>at the moment recording started</b>;
    /// renaming during a recording does not retroactively change the batch.</para>
    /// </summary>
    protected override RecordingBatchInfo DescribeRecordingBatch() => new(
        SessionKind.Monitor,
        ChannelA.PortName, ChannelB.PortName,
        ChannelA.Alias, ChannelB.Alias,
        Settings);

    /// <summary>
    /// Carries both ports and the serial parameters, matching the "stop recording" export
    /// path (P0-7 b): that one builds <c>diserial-COM6-COM7-115200-8N1-...</c>, and this
    /// deliberately uses the same shape.
    ///
    /// <para>⚠️ <b>The file name carries ports, not aliases.</b> An alias may be empty, may
    /// contain spaces, and both channels may carry the same one, whereas a port name is
    /// naturally safe in a file name. Aliases travel in the exported <c>Alias</c> column
    /// instead (P0-7 a).</para>
    /// </summary>
    protected override string DescribeExportBaseName(string kind) =>
        $"diserial-{ChannelA.PortName}-{ChannelB.PortName}" +
        $"-{Settings.ShortDescription.Replace(' ', '-')}-{DateTime.Now:yyyyMMdd-HHmmss}";

    /// <summary>
    /// ⚠️ <b>Never replaced after construction</b> (2026-08-01, P0-9). These used to be
    /// <c>[ObservableProperty]</c> because swapping A/B exchanged the two objects -- which is
    /// exactly what caused P0-9: the exchange happened on the view-model side only, while the
    /// capture side's port-to-<c>ChannelId</c> mapping did not move at all.
    /// Identity now lives in the alias, so the slots never have to change and these two
    /// references no longer need to be observable.
    /// </summary>
    public ChannelViewModel ChannelA { get; }

    /// <inheritdoc cref="ChannelA"/>
    public ChannelViewModel ChannelB { get; }

    // SyncParameters (M-03) lived here until 2026-08-02 (P1-49), together with a
    // write-back to settings. Both the constructor and the factory's object initializer
    // assigned it, so every monitor session paid for two debounced disk writes to
    // persist a flag nothing ever read. Full rationale next to AppSettingsModel.Monitor.

    // ViewMode (a MonitorViewMode observable, always Merged) lived here until 2026-08-05
    // (P2-37). Nothing bound it, nothing read it, no test named it -- the property existed
    // only to say "V1.2 will add Split". A field that states a plan is a comment with a
    // change-notification cost, and an ObservableProperty that never changes is worse than
    // no property: it reads as a supported mode switch. The plan itself survives on
    // MonitorViewMode in Core/Models/Enums.cs, where it costs nothing.

    public override SessionKind Kind => SessionKind.Monitor;

    public override string Title => LF(LocKeys.SessionMonitorTitle, ChannelA.Alias, ChannelB.Alias);

    /// <summary>
    /// ⚠️ <b>Since 2026-08-01 there are no <c>A:</c> / <c>B:</c> prefixes and no per-channel
    /// A/B arrows</b> -- the byte count follows the description of its own channel directly.
    /// Once swapping A/B was removed those two letters pointed at nothing, and keeping them
    /// would have left the status bar as the only place in the UI still saying "A/B", out of
    /// step with the frame rows. See 01-spec 4.13.
    /// </summary>
    public override string StatusText => LF(
        LocKeys.SessionMonitorStatus,
        Describe(ChannelA), ChannelA.BytesReceived,
        Describe(ChannelB), ChannelB.BytesReceived,
        Settings.ShortDescription,
        DescribeState(),
        LogPanel.FrameCount);

    /// <summary>
    /// How one channel is written in the status bar: <b>the port name alone while it has
    /// never been renamed</b>, with the alias appended once it has.
    ///
    /// <para>⚠️ Without that rule, an alias defaulting to the port name would render as
    /// <c>COM6 "COM6"</c> -- and under the current design "before renaming" is a stage every
    /// session passes through, not a corner case.</para>
    ///
    /// <para>⚠️ <b>The separator differs from the frame row's <c>InlineLabel</c> on purpose.</b>
    /// In the status bar each channel is immediately followed by its byte count, so the middle
    /// dot used in frame rows would make the arrow look attached to the alias. The predicate
    /// (<c>HasCustomAlias</c>) is the same one; only the presentation differs, which is why the
    /// two places can never disagree.</para>
    /// </summary>
    private string Describe(ChannelViewModel channel) => channel.HasCustomAlias
        ? LF(LocKeys.ChannelPortWithAlias, channel.PortName, channel.Alias)
        : channel.PortName;

    /// <summary>
    /// Keeps things in step when a channel property changes. <b>Only alias changes do
    /// anything</b>: <c>BytesReceived</c> changes on every frame, and refreshing the log for
    /// that would drag the UI down.
    /// </summary>
    private void OnChannelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ChannelViewModel.Alias)) return;
        if (sender is not ChannelViewModel channel) return;

        // 1. Rows already in the log (P1-41). Without this the top half of the screen says
        //    COM6 and the bottom half COM6 - PLC, while exporting the batch uses a single
        //    name: two answers, screen and file.
        LogPanel.RelabelChannel(channel.Id, channel.InlineLabel);

        // 2. The status bar and the title are computed properties: without an explicit
        //    notification they refresh only when the next frame arrives, so on a silent bus
        //    the old name would stay on screen indefinitely.
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(StatusText));
    }

    /// <summary>
    /// Placeholder text for the alias box, e.g. "Alias for COM6".
    ///
    /// <para>It lives here rather than in <c>ChannelViewModel</c> so that class need not hold
    /// an <c>ILocalizationService</c>: it then does not have to subscribe to
    /// <c>CultureChanged</c> nor be <c>IDisposable</c>, and this class already has that
    /// subscription anyway.</para>
    /// </summary>
    private void ApplyChannelPlaceholders()
    {
        ChannelA.AliasPlaceholder = LF(LocKeys.ChannelAliasPlaceholder, ChannelA.PortName);
        ChannelB.AliasPlaceholder = LF(LocKeys.ChannelAliasPlaceholder, ChannelB.PortName);
    }

    /// <summary>The placeholder follows a language switch -- it is the only localized text in
    /// this class with a backing field.</summary>
    protected override void OnCultureChanged()
    {
        base.OnCultureChanged();
        ApplyChannelPlaceholders();
    }

    // NOTE: no DisposeCore override any more. Its whole job was releasing the two channel
    // subscriptions, and those now register their own removal where they are made (P2-44).
    // The override is not merely redundant — keeping it would put the same discipline back
    // in a second place, which is what let EnableSendRequested slip through in the first one.

    /// <summary>
    /// The monitor session words the connection state as "Monitoring / Not started".
    ///
    /// NOTE: override <c>DescribeConnectionState</c>, not <c>DescribeState</c> — the latter adds
    /// the "display paused" suffix (P1-40) and is deliberately non-virtual so it cannot be lost.
    /// </summary>
    protected override string DescribeConnectionState() => State switch
    {
        ConnectionState.Connected => L(LocKeys.StateMonitoring),
        ConnectionState.Connecting => L(LocKeys.StateConnecting),
        ConnectionState.Faulted => L(LocKeys.StateFaulted),
        _ => L(LocKeys.StateNotStarted)
    };

    /// <summary>
    /// ⛔ <b>"Swap A/B" (M-06) was removed on 2026-08-01. Do not add it back.</b>
    ///
    /// What it was for -- "I got the two sides of the bus the wrong way round" -- is now
    /// expressed by <b>renaming the port</b>: the alias sits on the same object as the port,
    /// so a rename needs nothing else to be brought back into line.
    ///
    /// The old implementation exchanged only the two view-model objects while the capture
    /// side's port-to-<c>ChannelId</c> mapping stayed put, so after a swap every channel
    /// attribution on screen was wrong -- and because aliases were resolved at export time,
    /// frames captured <b>before</b> the swap were rewritten too. That was P0-9.
    ///
    /// ⚠️ If someone later asks to "show PLC in blue", that is a <b>colouring</b> request:
    /// change M-05a's colours (currently a fixed blue and green), <b>not</b> bring slot
    /// swapping back. The specification is 01-spec 4.13.
    /// </summary>
    /// <summary>
    /// ⭐ <b>Returns null when the channel was never renamed</b> (P2-22, 2026-08-01), which
    /// leaves an empty cell in the exported <c>Alias</c> column.
    ///
    /// An alias defaults to the port name, so writing it unconditionally would put
    /// <c>Port=COM4</c> and <c>Alias=COM4</c> in the file -- the same value in two columns.
    ///
    /// Empty is the "no value" a parser already understands (see <c>IExportService</c>), the
    /// same reasoning as leaving the first frame's <c>DeltaMs</c> empty -- and it carries one
    /// extra piece of information: <b>a parser can tell "the user named this" from "they did
    /// not"</b>, which writing the port name would hide.
    ///
    /// ⚠️ The predicate is <see cref="ChannelViewModel.HasCustomAlias"/> rather than "not
    /// empty" -- <b>the same one the frame rows and the status bar use</b> -- so a user who
    /// types <c>COM4</c> as the alias by hand also counts as "never named", and all three
    /// places agree.
    /// </summary>
    protected override string? ResolveChannelAlias(ChannelId channel) => channel switch
    {
        ChannelId.A => ChannelA.HasCustomAlias ? ChannelA.Alias : null,
        ChannelId.B => ChannelB.HasCustomAlias ? ChannelB.Alias : null,
        _ => null
    };

    /// <summary>
    /// Clearing resets the per-channel byte counters too (P2-20, option Y).
    ///
    /// These live here rather than in the log panel, which is exactly why Clear used to miss
    /// them: the frame count went to zero while the byte counts kept running, putting two
    /// different epochs on one status line.
    /// </summary>
    protected override void OnCleared()
    {
        ChannelA.BytesReceived = 0;
        ChannelB.BytesReceived = 0;
    }

    protected override string? ResolveChannelPort(ChannelId channel) => channel switch
    {
        ChannelId.A => ChannelA.PortName,
        ChannelId.B => ChannelB.PortName,
        _ => null
    };

    protected override string? ResolveChannelLabel(ChannelId channel) => channel switch
    {
        ChannelId.A => ChannelA.InlineLabel,
        ChannelId.B => ChannelB.InlineLabel,
        _ => null
    };

    /// <summary>
    /// ⚠️ <b>Counts <c>Rx</c> only</b> (P1-32, fixed after it was raised to P0 on 2026-07-31).
    ///
    /// <para>This count is shown as <b>bytes received</b> in the side panel and next to the
    /// channel in the status bar -- counting bytes <b>we injected ourselves</b> would be
    /// reporting injection as observation.
    /// Measured evidence: 6 bytes injected and 0 actually received, while both the side panel
    /// and the status bar showed 6.</para>
    ///
    /// <para>⚠️ <b>Injected bytes are not part of any visible counter now.</b> They appear
    /// only as a row on the merged timeline, marked as TX by
    /// <see cref="Panels.FrameViewModel.ChannelText"/>.
    /// Whether to show a separate injected-byte count is its own product question: undecided,
    /// and recorded in 00-STATUS.</para>
    /// </summary>
    protected override void OnFrameAppended(SerialFrame frame)
    {
        if (frame.Direction != FrameDirection.Rx) return;

        switch (frame.Channel)
        {
            case ChannelId.A: ChannelA.BytesReceived += frame.Length; break;
            case ChannelId.B: ChannelB.BytesReceived += frame.Length; break;
        }
    }

    /// <summary>
    /// The injection-risk confirmation shown when the user clicks "enable sending" (the
    /// second layer of M-09's protection).
    ///
    /// <para>The diDatatracker is a passive tap, but its two virtual COM ports are physically
    /// connected to both sides of the bus, so a write really does inject into a live
    /// industrial bus and can disturb a PLC line that is in production.</para>
    /// </summary>
    private async void OnEnableSendRequested(object? sender, EventArgs e)
    {
        var confirmed = await Context.DialogService.ShowConfirmationAsync(
            L(LocKeys.InjectionTitle),
            L(LocKeys.InjectionMessage),
            L(LocKeys.InjectionConfirm),
            L(LocKeys.CommonCancel));

        if (confirmed) SendPanel.ConfirmEnableSend();
    }
}
