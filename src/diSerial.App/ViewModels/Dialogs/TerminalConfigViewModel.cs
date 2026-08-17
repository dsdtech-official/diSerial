using CommunityToolkit.Mvvm.ComponentModel;
using DiSerial.App.Localization;
using DiSerial.App.Services;
using DiSerial.App.ViewModels.Sessions;
using DiSerial.Core.Models;

namespace DiSerial.App.ViewModels.Dialogs;

/// <summary>
/// Terminal session configuration: one serial port.
///
/// <para>The matching view is <c>Views/Dialogs/TerminalConfigView.axaml</c>, found by naming
/// convention -- see <see cref="SessionConfigViewModel"/>.</para>
/// </summary>
public sealed partial class TerminalConfigViewModel : SessionConfigViewModel
{
    private readonly IAppSettings _settings;

    public TerminalConfigViewModel(
        ILocalizationService localization,
        IEnumChoiceProvider enumChoices,
        IAppSettings settings)
        : base(localization, enumChoices)
    {
        _settings = settings;
        Settings.LoadFrom(settings.Terminal.Serial.ToSettings());
    }

    /// <summary>
    /// What is in the port box -- picked from the list or typed (P1-4).
    ///
    /// <para>⭐ <b>The text is the state, not the chosen record.</b> The control is an
    /// <c>AutoCompleteBox</c> whose <c>Text</c> and <c>ItemsSource</c> are independent
    /// properties, so a hot-plug rebuilding <see cref="SessionConfigViewModel.AvailablePorts"/>
    /// cannot touch what the user typed. ⛔ <b>That is why this is not an editable
    /// <c>ComboBox</c></b>: there, the two are one thing, and a collection change wipes the box
    /// -- the P0-8 regression, which P1-45 removed from the send panel by splitting the control
    /// rather than by fixing the wipe.</para>
    /// </summary>
    [ObservableProperty]
    private string _portText = string.Empty;

    /// <summary>The port this terminal will open, resolved from <see cref="PortText"/>.</summary>
    public SerialPortInfo? SelectedPort => FromText(PortText);

    /// <summary>
    /// The dropdown's selection, which is a <b>gesture rather than state</b>: choosing an entry
    /// writes its name into <see cref="PortText"/> and immediately resets itself to null.
    ///
    /// <para>⭐ <b>Why it resets.</b> <see cref="PortText"/> is the single source of truth. If the
    /// dropdown kept its own selection, the dialog would hold two answers to "which port", and
    /// the pair drifts the moment either side changes — the hot-plug rebuild being the obvious
    /// way. Same shape as <c>SendPanelViewModel.SelectedHistoryEntry</c>, which also always reads
    /// back null (P1-45).</para>
    ///
    /// <para>⚠️ <b>This is the picker half of a design that is not finished</b> — the
    /// sectioned list and the "Other…" text entry are still to come (00-STATUS P1-4). Read that
    /// entry before changing the shape here.</para>
    /// </summary>
    public SerialPortInfo? PickedPort
    {
        get => ByName(PortText);
        set { if (value is not null) PortText = value.PortName; }
    }

    public override bool CanConfirm => SelectedPort is not null;

    /// <summary>
    /// ⚠ Only called when <see cref="CanConfirm"/> is true, which is what makes
    /// <c>SelectedPort!</c> safe here. Before P2-52 the null flowed all the way into the
    /// factory and was caught there; now the record refuses to be built without a port, so the
    /// assertion sits next to the check that establishes it.
    /// </summary>
    public override NewSessionResult BuildRequest() => new TerminalSessionRequest
    {
        Port = SelectedPort!,
        Settings = Settings.ToSettings()
    };

    public override void RememberSettings() =>
        _settings.Terminal = _settings.Terminal with
        {
            Serial = SerialPreferences.From(Settings.ToSettings())
        };

    public override void ApplyPorts(IReadOnlyList<SerialPortInfo> ports)
    {
        Repopulate(ports);

        // Prefills the first port so a fresh dialog is immediately usable.
        //
        // ⛔ Only while the box is still untouched. Once there is text in it -- typed or picked --
        // no later refresh may change it. Before P1-4 this method re-pointed the selection at the
        // first port whenever the chosen one disappeared; with a text box that same move would be
        // overwriting the user's input on a hot-plug, which is exactly what the control was
        // chosen to prevent.
        //
        // ⚠ So an unplugged port now KEEPS its name here and Connect stays enabled. It fails at
        // open time with "that serial port does not exist -- it may have been unplugged, or the
        // name may be wrong", which says the true thing. What is still refused is silently
        // pointing the session at some OTHER port, which is what the old fallback did.
        if (string.IsNullOrWhiteSpace(PortText))
            PortText = AvailablePorts.FirstOrDefault()?.PortName ?? string.Empty;
    }

    public override string DescribeFailure(SessionOpenFailure failure) => LF(
        LocKeys.DialogOpenFailedTerminal,
        SelectedPort?.PortName ?? "-",
        DescribeReason(failure));

    partial void OnPortTextChanged(string value)
    {
        // ⛔ The dropdown reads its selection back out of PortText, so it has to be told when the
        // text moved -- otherwise picking is one-way and any later change (a refresh, or the
        // typed entry still to come) leaves the control showing a stale port.
        OnPropertyChanged(nameof(PickedPort));

        RaiseCanConfirmChanged();
    }
}
