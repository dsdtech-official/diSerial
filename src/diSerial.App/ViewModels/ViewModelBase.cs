using CommunityToolkit.Mvvm.ComponentModel;

namespace DiSerial.App.ViewModels;

/// <summary>
/// 所有 ViewModel 的基类。
///
/// 采用 CommunityToolkit.Mvvm 而非 ReactiveUI：前者基于源生成器，
/// 对 NativeAOT 与 Trimming 友好；后者依赖反射，会阻碍后续的体积优化。
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
}
