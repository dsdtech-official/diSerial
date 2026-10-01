using CommunityToolkit.Mvvm.ComponentModel;
using DiSerial.App.Localization;

namespace DiSerial.App.ViewModels;

// Represent a language choice.
public sealed partial class LanguageMenuItemViewModel(LanguageOption option) : ViewModelBase
{
    public LanguageOption Option { get; } = option;

    public string NativeName => Option.NativeName;

    [ObservableProperty]
    private bool _isSelected;
}
