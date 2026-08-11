using CommunityToolkit.Mvvm.ComponentModel;
using DiSerial.App.Localization;

namespace DiSerial.App.ViewModels;

/// <summary>
/// 语言菜单中的一项。
///
/// 单独建模而不是直接把 <see cref="LanguageOption"/> 绑到菜单上，
/// 是因为需要一个可变更通知的 <see cref="IsSelected"/> 来驱动勾选标记 ——
/// LanguageOption 本身是不可变记录，不应为界面状态污染它。
///
/// 语言名一律以该语言自身书写（endonym），不参与翻译：
/// 界面已经切到看不懂的语言时，用户要靠这些字自己认出目标语言。
/// </summary>
public sealed partial class LanguageMenuItemViewModel(LanguageOption option) : ViewModelBase
{
    public LanguageOption Option { get; } = option;

    public string NativeName => Option.NativeName;

    /// <summary>是否为当前界面语言，绑定到菜单项的勾选标记。</summary>
    [ObservableProperty]
    private bool _isSelected;
}
