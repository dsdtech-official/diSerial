using CommunityToolkit.Mvvm.ComponentModel;
using DiSerial.App.Localization;
using DiSerial.Core.Models;

namespace DiSerial.App.ViewModels.Panels;

/// <summary>
/// 串口参数编辑（C-04a）。
///
/// V1.0 只提供标准波特率列表。自定义波特率需要平台特定实现
/// （Linux TCSETS2 / macOS IOSSIOSPEED），排期在 V1.1 ——
/// 届时把 BaudRate 改为可编辑的 ComboBox 即可，其余不变。
/// </summary>
public sealed partial class PortSettingsViewModel : ViewModelBase
{
    public PortSettingsViewModel(IEnumChoiceProvider enumChoices)
    {
        ParityChoices = enumChoices.GetChoices<SerialParity>();
        StopBitsChoices = enumChoices.GetChoices<SerialStopBits>();
        FlowControlChoices = enumChoices.GetChoices<SerialFlowControl>();
    }

    // 默认值取自领域模型，不在此另写一份。
    // 两处各写一遍迟早会漂移，症状是「对话框显示 9600、实际下发别的」——
    // 那种偏差不会报错，只会让人对着数据发呆。
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

    /// <summary>波特率与数据位是纯数字，无需本地化。</summary>
    public IReadOnlyList<int> BaudRates { get; } = SerialPortSettings.StandardBaudRates;

    public IReadOnlyList<int> DataBitsOptions { get; } = [5, 6, 7, 8];

    public IReadOnlyList<LocalizedEnumItem> ParityChoices { get; }

    public IReadOnlyList<LocalizedEnumItem> StopBitsChoices { get; }

    public IReadOnlyList<LocalizedEnumItem> FlowControlChoices { get; }

    public SerialPortSettings ToSettings() => new()
    {
        BaudRate = BaudRate,
        DataBits = DataBits,
        Parity = Parity,
        StopBits = StopBits,
        FlowControl = FlowControl
    };

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
