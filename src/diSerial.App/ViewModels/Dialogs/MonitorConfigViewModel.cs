using CommunityToolkit.Mvvm.ComponentModel;
using DiSerial.App.Localization;
using DiSerial.App.Services;
using DiSerial.App.ViewModels.Sessions;
using DiSerial.Core.Models;

namespace DiSerial.App.ViewModels.Dialogs;

// Configure two monitored ports.
public sealed partial class MonitorConfigViewModel : SessionConfigViewModel
{
    private readonly IAppSettings _settings;

    private bool _prefilled;

    // Initialize monitoring settings.
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
    private string _channelAText = string.Empty;

    [ObservableProperty]
    private string _channelBText = string.Empty;

    public SerialPortInfo? ChannelAPort => FromText(ChannelAText);

    public SerialPortInfo? ChannelBPort => FromText(ChannelBText);

    public SerialPortInfo? PickedChannelA
    {
        get => ByName(ChannelAText);
        set { if (value is not null) ChannelAText = value.PortName; }
    }

    public SerialPortInfo? PickedChannelB
    {
        get => ByName(ChannelBText);
        set { if (value is not null) ChannelBText = value.PortName; }
    }

    public override bool CanConfirm =>
        ChannelAPort is not null
        && ChannelBPort is not null
        && !string.Equals(ChannelAPort.PortName, ChannelBPort.PortName, StringComparison.OrdinalIgnoreCase);

    // Build the session request.
    public override NewSessionResult BuildRequest() => new MonitorSessionRequest
    {
        Pair = new SerialChannelPair { ChannelA = ChannelAPort!, ChannelB = ChannelBPort! },
        Settings = Settings.ToSettings()
    };

    // Save session preferences.
    public override void RememberSettings() =>
        _settings.Monitor = _settings.Monitor with
        {
            Serial = SerialPreferences.From(Settings.ToSettings())
        };

    // Update available port choices.
    public override void ApplyPorts(IReadOnlyList<SerialPortInfo> ports)
    {
        var channelA = ChannelAText;
        var channelB = ChannelBText;

        Repopulate(ports);

        if (!_prefilled && string.IsNullOrWhiteSpace(channelA) && string.IsNullOrWhiteSpace(channelB))
        {
            if (AvailablePorts.Count == 0) return;

            var prefill = InPrefillOrder(AvailablePorts);

            _prefilled = true;
            ChannelAText = prefill[0].PortName;
            ChannelBText = prefill.Skip(1).FirstOrDefault()?.PortName ?? string.Empty;
            return;
        }

        ChannelAText = ByName(channelA)?.PortName ?? string.Empty;
        ChannelBText = ByName(channelB)?.PortName ?? string.Empty;
    }

    // Describe a connection error.
    public override string DescribeFailure(SessionOpenFailure failure)
    {
        var portName = (failure.Channel == ChannelId.B ? ChannelBPort : ChannelAPort)?.PortName ?? "-";

        return LF(
            LocKeys.DialogOpenFailedMonitor,
            failure.Channel.ToString(),
            portName,
            DescribeReason(failure));
    }

    // Update the paired port choice.
    partial void OnChannelATextChanged(string value)
    {
        OnPropertyChanged(nameof(PickedChannelA));
        RaiseCanConfirmChanged();
    }

    // Refresh channel B validation.
    partial void OnChannelBTextChanged(string value)
    {
        OnPropertyChanged(nameof(PickedChannelB));
        RaiseCanConfirmChanged();
    }
}
