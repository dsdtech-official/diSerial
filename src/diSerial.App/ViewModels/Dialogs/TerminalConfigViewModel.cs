using CommunityToolkit.Mvvm.ComponentModel;
using DiSerial.App.Localization;
using DiSerial.App.Services;
using DiSerial.App.ViewModels.Sessions;
using DiSerial.Core.Models;

namespace DiSerial.App.ViewModels.Dialogs;

// Configure a terminal port.
public sealed partial class TerminalConfigViewModel : SessionConfigViewModel
{
    private readonly IAppSettings _settings;

    // Initialize terminal settings.
    public TerminalConfigViewModel(
        ILocalizationService localization,
        IEnumChoiceProvider enumChoices,
        IAppSettings settings)
        : base(localization, enumChoices)
    {
        _settings = settings;
        Settings.LoadFrom(settings.Terminal.Serial.ToSettings());
    }

    [ObservableProperty]
    private string _portText = string.Empty;

    public SerialPortInfo? SelectedPort => FromText(PortText);

    public SerialPortInfo? PickedPort
    {
        get => ByName(PortText);
        set { if (value is not null) PortText = value.PortName; }
    }

    public override bool CanConfirm => SelectedPort is not null;

    // Build the session request.
    public override NewSessionResult BuildRequest() => new TerminalSessionRequest
    {
        Port = SelectedPort!,
        Settings = Settings.ToSettings()
    };

    // Save session preferences.
    public override void RememberSettings() =>
        _settings.Terminal = _settings.Terminal with
        {
            Serial = SerialPreferences.From(Settings.ToSettings())
        };

    // Update available port choices.
    public override void ApplyPorts(IReadOnlyList<SerialPortInfo> ports)
    {
        Repopulate(ports);

        if (string.IsNullOrWhiteSpace(PortText))
            PortText = InPrefillOrder(AvailablePorts).FirstOrDefault()?.PortName ?? string.Empty;
    }

    // Describe a connection error.
    public override string DescribeFailure(SessionOpenFailure failure) => LF(
        LocKeys.DialogOpenFailedTerminal,
        SelectedPort?.PortName ?? "-",
        DescribeReason(failure));

    // Refresh port selection validity.
    partial void OnPortTextChanged(string value)
    {
        OnPropertyChanged(nameof(PickedPort));

        RaiseCanConfirmChanged();
    }
}
