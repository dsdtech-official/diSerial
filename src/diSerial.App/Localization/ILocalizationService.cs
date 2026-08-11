using System.ComponentModel;
using System.Globalization;

namespace DiSerial.App.Localization;

/// <summary>
/// 多语言服务契约（C-25）。
///
/// 与 <c>ISerialPort</c> 同一套抽象纪律：调用方只依赖本接口，
/// 后端资源格式（RESX / JSON / 第三方库）成为可替换的实现细节。
///
/// 实现 <see cref="INotifyPropertyChanged"/> 是为了支持**运行时实时切换** ——
/// XAML 通过索引器绑定取值，语言变更时统一发出通知即可刷新全部界面文本，
/// 无需重启应用。
/// </summary>
public interface ILocalizationService : INotifyPropertyChanged
{
    /// <summary>按键取当前语言的文本。键不存在时返回 <c>!Key!</c> 以便开发期立刻发现。</summary>
    string this[string key] { get; }

    /// <summary>
    /// 按键查找文本；键不存在时返回 null。
    ///
    /// 与索引器的区别在于「查不到」是否可区分：索引器返回 <c>!Key!</c> 便于
    /// 界面上一眼看出漏配，而调用方若需要在查不到时走兜底逻辑
    /// （如第三方解码器插件自带的文本），就必须能拿到 null。
    /// </summary>
    string? Find(string key);

    /// <summary>
    /// 取某个键的可绑定包装对象，供 XAML 标记扩展使用。
    ///
    /// 为什么不让 XAML 直接绑索引器：Avalonia 对 CLR 索引器的变更通知要求
    /// 特定的属性名（"Item[]"），而「所有属性均已变更」的空字符串通知覆盖不到它，
    /// 导致语言切换后界面不刷新。改绑一个普通属性可彻底绕开该语义差异。
    ///
    /// 实现方按键缓存，因此每个键全应用只有一个实例与一次事件订阅。
    /// </summary>
    LocalizedString GetBindable(string key);

    /// <summary>带占位符的文本。用于 Title、StatusText 等需要拼接运行时值的场景。</summary>
    string Format(string key, params object?[] args);

    /// <summary>当前界面语言。</summary>
    CultureInfo CurrentCulture { get; }

    /// <summary>可供用户选择的语言列表。</summary>
    IReadOnlyList<LanguageOption> AvailableLanguages { get; }

    /// <summary>
    /// 切换界面语言。
    ///
    /// ⚠️ 实现只应设置 <c>CurrentUICulture</c>（决定资源查找），
    /// 绝不可修改 <c>CurrentCulture</c> —— 后者会改变数字与日期格式，
    /// 使 "4.1" 在德语区域变成 "4,1"，直接破坏 CSV 导出。
    /// </summary>
    void SetLanguage(CultureInfo culture);

    /// <summary>语言已变更。供 ViewModel 刷新计算属性。</summary>
    event EventHandler? CultureChanged;
}

/// <summary>一个可选语言。名称以该语言自身书写（endonym），不参与翻译。</summary>
public sealed record LanguageOption(string CultureName, string NativeName)
{
    public CultureInfo Culture { get; } = CultureInfo.GetCultureInfo(CultureName);

    public override string ToString() => NativeName;
}

/// <summary>
/// 单个资源键的可绑定包装。XAML 绑定其 <see cref="Value"/> 属性，
/// 语言切换时由本对象发出普通属性变更通知，实现界面即时刷新。
/// </summary>
public sealed class LocalizedString : INotifyPropertyChanged
{
    private readonly ILocalizationService _localization;
    private readonly string _key;

    internal LocalizedString(ILocalizationService localization, string key)
    {
        _localization = localization;
        _key = key;
        _localization.CultureChanged += (_, _) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
    }

    public string Value => _localization[_key];

    public event PropertyChangedEventHandler? PropertyChanged;

    public override string ToString() => Value;
}
