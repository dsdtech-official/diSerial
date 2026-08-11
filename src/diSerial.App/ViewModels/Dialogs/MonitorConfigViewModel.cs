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

    [ObservableProperty]
    private SerialPortInfo? _channelAPort;

    [ObservableProperty]
    private SerialPortInfo? _channelBPort;

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
        var channelA = ChannelAPort?.PortName;
        var channelB = ChannelBPort?.PortName;

        Repopulate(ports);

        // First population prefills the first two ports, which is right most of the time --
        // a tap shows up as an adjacent pair.
        //
        // ⚠ The prefill lives here rather than in a method the dialog calls, so that the
        // dialog never has to know this type needs two ports. That is the same rule the whole
        // split exists for.
        if (channelA is null && channelB is null)
        {
            ChannelAPort = AvailablePorts.FirstOrDefault();
            ChannelBPort = AvailablePorts.Skip(1).FirstOrDefault();
            return;
        }

        // ⚠ No fallback to "the first port" on a later refresh, unlike the terminal: silently
        // re-pointing a channel at some other port after its own was unplugged would put a
        // monitor on a bus the user never chose. An unplugged channel goes empty and blocks
        // Connect instead.
        ChannelAPort = ByName(channelA);
        ChannelBPort = ByName(channelB);
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

    partial void OnChannelAPortChanged(SerialPortInfo? value) => RaiseCanConfirmChanged();

    partial void OnChannelBPortChanged(SerialPortInfo? value) => RaiseCanConfirmChanged();
}
