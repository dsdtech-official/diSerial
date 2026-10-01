using CommunityToolkit.Mvvm.ComponentModel;
using DiSerial.App.Localization;
using DiSerial.Core.Models;

namespace DiSerial.App.ViewModels.Panels;

// Edit serial port parameters.
public sealed partial class PortSettingsViewModel : ViewModelBase
{
    // Initialize serial choices.
    public PortSettingsViewModel(IEnumChoiceProvider enumChoices)
    {
        ParityChoices = enumChoices.GetChoices<SerialParity>();
        StopBitsChoices = enumChoices.GetChoices<SerialStopBits>();
        FlowControlChoices = enumChoices.GetChoices<SerialFlowControl>();
    }

    private static readonly SerialPortSettings CoreDefaults = new();

    [ObservableProperty]
    private int _baudRate = CoreDefaults.BaudRate;

    [ObservableProperty]
    private int _dataBits = CoreDefaults.DataBits;

    [ObservableProperty]
    private SerialParity _parity = CoreDefaults.Parity;

    [ObservableProperty]
    private SerialStopBits _stopBits = CoreDefaults.StopBits;

    [ObservableProperty]
    private SerialFlowControl _flowControl = CoreDefaults.FlowControl;

    public IReadOnlyList<int> BaudRates { get; } = SerialPortSettings.StandardBaudRates;

    public IReadOnlyList<int> DataBitsOptions { get; } = [5, 6, 7, 8];

    public IReadOnlyList<LocalizedEnumItem> ParityChoices { get; }

    public IReadOnlyList<LocalizedEnumItem> StopBitsChoices { get; }

    public IReadOnlyList<LocalizedEnumItem> FlowControlChoices { get; }

    // Build serial port settings.
    public SerialPortSettings ToSettings() => new()
    {
        BaudRate = BaudRate,
        DataBits = DataBits,
        Parity = Parity,
        StopBits = StopBits,
        FlowControl = FlowControl
    };

    // Load serial port settings.
    public void LoadFrom(SerialPortSettings settings)
    {
        BaudRate = settings.BaudRate;
        DataBits = settings.DataBits;
        Parity = settings.Parity;
        StopBits = settings.StopBits;
        FlowControl = settings.FlowControl;
    }

    public string ShortDescription => ToSettings().ShortDescription;
}
