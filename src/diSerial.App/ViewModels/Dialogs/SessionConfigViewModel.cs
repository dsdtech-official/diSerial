using System.Collections.ObjectModel;
using DiSerial.App.Localization;
using DiSerial.App.ViewModels.Panels;
using DiSerial.App.ViewModels.Sessions;
using DiSerial.Core.Models;

namespace DiSerial.App.ViewModels.Dialogs;

// Configure a session type.
public abstract class SessionConfigViewModel : LocalizedViewModelBase
{
    // Initialize session settings.
    protected SessionConfigViewModel(ILocalizationService localization, IEnumChoiceProvider enumChoices)
        : base(localization)
    {
        Settings = new PortSettingsViewModel(enumChoices);
    }

    public PortSettingsViewModel Settings { get; }

    public ObservableCollection<SerialPortInfo> AvailablePorts { get; } = [];

    public abstract bool CanConfirm { get; }

    // Build the session request.
    public abstract NewSessionResult BuildRequest();

    // Save session preferences.
    public abstract void RememberSettings();

    // Update available port choices.
    public abstract void ApplyPorts(IReadOnlyList<SerialPortInfo> ports);

    // Describe a connection error.
    public abstract string DescribeFailure(SessionOpenFailure failure);

    public event EventHandler? CanConfirmChanged;

    // Notify configuration validity.
    protected void RaiseCanConfirmChanged() => CanConfirmChanged?.Invoke(this, EventArgs.Empty);

    // Find a port by its name.
    protected SerialPortInfo? ByName(string? portName) => portName is null
        ? null
        : AvailablePorts.FirstOrDefault(p => p.PortName == portName);

    // Resolve an entered port name.
    protected SerialPortInfo? FromText(string? text)
    {
        var name = text?.Trim();

        return string.IsNullOrEmpty(name)
            ? null
            : ByName(name) ?? new SerialPortInfo { PortName = name };
    }

    // Refresh the port choice list.
    protected void Repopulate(IReadOnlyList<SerialPortInfo> ports)
    {
        AvailablePorts.Clear();
        foreach (var port in ports) AvailablePorts.Add(port);
    }

    // Order suggested port choices.
    protected static IReadOnlyList<SerialPortInfo> InPrefillOrder(IReadOnlyList<SerialPortInfo> ports) =>
    [
        .. ports.Where(IsLikelyUsbAdapter),
        .. ports.Where(p => !IsLikelyUsbAdapter(p))
    ];

    // Identify likely USB adapters.
    private static bool IsLikelyUsbAdapter(SerialPortInfo port) =>
        port.PortName.Contains("usbserial", StringComparison.OrdinalIgnoreCase)
        || port.PortName.Contains("usbmodem", StringComparison.OrdinalIgnoreCase);

    // Describe port support status.
    protected string DescribeReason(SessionOpenFailure failure) =>
        SessionErrorPresenter.Describe(failure.Kind, Localization);
}
