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

    /// <summary>The port this terminal will open.</summary>
    [ObservableProperty]
    private SerialPortInfo? _selectedPort;

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
        var selected = SelectedPort?.PortName;

        Repopulate(ports);

        // Falls back to the first port so a fresh dialog is immediately usable; a port that is
        // still present keeps its selection.
        SelectedPort = ByName(selected) ?? AvailablePorts.FirstOrDefault();
    }

    public override string DescribeFailure(SessionOpenFailure failure) => LF(
        LocKeys.DialogOpenFailedTerminal,
        SelectedPort?.PortName ?? "-",
        DescribeReason(failure));

    partial void OnSelectedPortChanged(SerialPortInfo? value) => RaiseCanConfirmChanged();
}
