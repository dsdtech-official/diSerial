using DiSerial.App.ViewModels;

namespace DiSerial.App.Localization;

/// <summary>
/// 需要随语言切换刷新的 ViewModel 基类。
///
/// ⚠️ 为什么不直接放进 <see cref="ViewModelBase"/>：
/// <c>FrameViewModel</c> 也继承自 ViewModelBase，且每收到一帧就 new 一个，
/// 显示区上限 500 个实例。若基类无差别订阅 CultureChanged，会产生数百个
/// 事件订阅 —— 既拖慢语言切换，又因事件持有强引用形成内存泄漏。
/// 而 FrameViewModel 内部全是十六进制数据与时间戳，没有任何需要翻译的内容。
///
/// 因此本项目采取 **opt-in**：只有主窗口、会话、面板、对话框继承本类，
/// 高频创建的行级 ViewModel 一律留在 ViewModelBase。
/// </summary>
public abstract class LocalizedViewModelBase : ViewModelBase, IDisposable
{
    private bool _disposed;

    protected LocalizedViewModelBase(ILocalizationService localization)
    {
        Localization = localization;
        Localization.CultureChanged += OnCultureChanged;
    }

    protected ILocalizationService Localization { get; }

    /// <summary>取当前语言文本的快捷方式。</summary>
    protected string L(string key) => Localization[key];

    /// <summary>取带占位符的当前语言文本。</summary>
    protected string LF(string key, params object?[] args) => Localization.Format(key, args);

    /// <summary>
    /// 语言变更时的刷新。
    ///
    /// 默认发出「所有属性均已变更」通知 —— Title、StatusText、DescribeState()
    /// 这类无后备字段的计算属性不会自行重新求值，逐个列举既繁琐又易遗漏，
    /// 空字符串一行覆盖全部。
    /// </summary>
    protected virtual void OnCultureChanged() => OnPropertyChanged(string.Empty);

    private void OnCultureChanged(object? sender, EventArgs e) => OnCultureChanged();

    /// <summary>
    /// 派生类的额外清理。基类只负责退订 <c>CultureChanged</c>；
    /// 自己订了别的事件的派生类在这里退。
    ///
    /// ⚠️ <b>刻意做成钩子而不是把 <see cref="Dispose"/> 改成 virtual</b> ——
    /// 后者要求每个派生类都记得调 <c>base.Dispose()</c>，忘了就漏退订，
    /// 而漏退订正是本基类整个设计要防的事。
    /// </summary>
    protected virtual void DisposeCore() { }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        DisposeCore();
        Localization.CultureChanged -= OnCultureChanged;
        GC.SuppressFinalize(this);
    }
}
