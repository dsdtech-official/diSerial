using CommunityToolkit.Mvvm.ComponentModel;
using DiSerial.App.Localization;
using DiSerial.App.Services;
using DiSerial.App.ViewModels.Sessions;
using DiSerial.Core.Models;

namespace DiSerial.App.ViewModels.Dialogs;

/// <summary>
/// Monitor session configuration: two serial ports, one per channel.
///
/// <para><b>Two ports are chosen directly, not picked from a list of recognised devices.</b>
/// Any two ports can be combined -- one dual-port adapter, two separate USB cables, or our own
/// diDatatracker all look the same to the software.</para>
///
/// <para>⚠ <b>No alias input here</b> (P0-9, 2026-08-01). At creation time it is impossible to
/// tell which port is which side of the bus -- that only becomes visible once traffic flows.
/// Asking for "PLC / HMI" here forces a guess, and correcting a wrong guess was what the
/// deleted "swap A/B" button existed for. Aliases default to the port name and are edited in
/// the session. Spec 4.13.</para>
/// </summary>
public sealed partial class MonitorConfigViewModel : SessionConfigViewModel
{
    private readonly IAppSettings _settings;

    public MonitorConfigViewModel(
        ILocalizationService localization,
        IEnumChoiceProvider enumChoices,
        IAppSettings settings)
        : base(localization, enumChoices)
    {
        _settings = settings;
        Settings.LoadFrom(settings.Monitor.Serial.ToSettings());
    }

    /// <summary>
    /// What is in each channel's port box -- picked or typed (P1-4). See the terminal config for
    /// why the text is the state rather than the chosen record.
    /// </summary>
    [ObservableProperty]
    private string _channelAText = string.Empty;

    /// <inheritdoc cref="ChannelAText"/>
    [ObservableProperty]
    private string _channelBText = string.Empty;

    public SerialPortInfo? ChannelAPort => FromText(ChannelAText);

    public SerialPortInfo? ChannelBPort => FromText(ChannelBText);

    /// <summary>
    /// Each channel's dropdown selection -- a gesture, not state. See the terminal config for why
    /// these write into the text and reset themselves to null.
    /// </summary>
    public SerialPortInfo? PickedChannelA
    {
        get => ByName(ChannelAText);
        set { if (value is not null) ChannelAText = value.PortName; }
    }

    /// <inheritdoc cref="PickedChannelA"/>
    public SerialPortInfo? PickedChannelB
    {
        get => ByName(ChannelBText);
        set { if (value is not null) ChannelBText = value.PortName; }
    }

    /// <summary>
    /// Both ports must be chosen, and they must differ.
    ///
    /// ⚠ The same port twice is refused rather than silently tolerated: the second open would
    /// fail anyway ("port in use"), and the occupier would be this very dialog.
    /// </summary>
    public override bool CanConfirm =>
        ChannelAPort is not null
        && ChannelBPort is not null
        && !string.Equals(ChannelAPort.PortName, ChannelBPort.PortName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// ⚠ Only called when <see cref="CanConfirm"/> is true -- see the note on the terminal
    /// config's version for why the <c>!</c> is safe and why it moved here (P2-52).
    /// </summary>
    public override NewSessionResult BuildRequest() => new MonitorSessionRequest
    {
        Pair = new SerialChannelPair { ChannelA = ChannelAPort!, ChannelB = ChannelBPort! },
        Settings = Settings.ToSettings()
    };

    public override void RememberSettings() =>
        _settings.Monitor = _settings.Monitor with
        {
            Serial = SerialPreferences.From(Settings.ToSettings())
        };

    public override void ApplyPorts(IReadOnlyList<SerialPortInfo> ports)
    {
        Repopulate(ports);

        // First population prefills the first two ports, which is right most of the time --
        // a tap shows up as an adjacent pair.
        //
        // ⚠ The prefill lives here rather than in a method the dialog calls, so that the
        // dialog never has to know this type needs two ports. That is the same rule the whole
        // split exists for.
        //
        // ⛔ Both boxes must be untouched, not just one: prefilling B after the user has already
        // typed A would pick a partner they never asked for.
        if (string.IsNullOrWhiteSpace(ChannelAText) && string.IsNullOrWhiteSpace(ChannelBText))
        {
            ChannelAText = AvailablePorts.FirstOrDefault()?.PortName ?? string.Empty;
            ChannelBText = AvailablePorts.Skip(1).FirstOrDefault()?.PortName ?? string.Empty;
        }

        // ⚠ Nothing else happens on a later refresh, and that is the point (P1-4). The rule this
        // method used to enforce by hand -- "never silently re-point a channel at some other port
        // after its own was unplugged, or the monitor ends up on a bus the user never chose" --
        // now holds structurally: a refresh does not write these fields at all.
        //
        // ⚠ What changed with it: an unplugged channel no longer goes empty, it keeps its name,
        // so Connect stays enabled and fails at open time with "that serial port does not exist".
        // The old emptying was the safer answer only while the box could not be typed into; now
        // it would be erasing input on a hot-plug.
    }

    public override string DescribeFailure(SessionOpenFailure failure)
    {
        var portName = (failure.Channel == ChannelId.B ? ChannelBPort : ChannelAPort)?.PortName ?? "-";

        return LF(
            LocKeys.DialogOpenFailedMonitor,
            failure.Channel.ToString(),
            portName,
            DescribeReason(failure));
    }

    // ⛔ Each dropdown reads its selection back out of its text, so it has to be told when that
    // moved -- see the terminal config for the full note.
    partial void OnChannelATextChanged(string value)
    {
        OnPropertyChanged(nameof(PickedChannelA));
        RaiseCanConfirmChanged();
    }

    partial void OnChannelBTextChanged(string value)
    {
        OnPropertyChanged(nameof(PickedChannelB));
        RaiseCanConfirmChanged();
    }
}
