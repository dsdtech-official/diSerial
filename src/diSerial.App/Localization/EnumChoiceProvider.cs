using System.Collections.Concurrent;
using DiSerial.App.ViewModels;

namespace DiSerial.App.Localization;

/// <summary>
/// 枚举的本地化选项（类别 C）。
///
/// 背景：<c>DisplayFormat</c>、<c>TimestampMode</c>、<c>SerialParity</c> 等枚举
/// 此前被直接绑定为 ComboBox 的 ItemsSource，靠 ToString() 渲染，
/// 界面上显示的是 <c>HexAndAscii</c>、<c>OnePointFive</c>、<c>RequestToSend</c>
/// 这样的原始枚举名 —— 这在做多语言之前就已经是显示缺陷。
///
/// 资源键约定：<c>Enum.{枚举类型名}.{枚举值名}</c>
/// </summary>
public sealed class LocalizedEnumItem : ViewModelBase
{
    private readonly ILocalizationService _localization;
    private readonly string _key;

    internal LocalizedEnumItem(object value, ILocalizationService localization)
    {
        Value = value;
        _localization = localization;
        _key = $"Enum.{value.GetType().Name}.{value}";

        // 实例总数受控（全应用约 25 个，由 EnumChoiceProvider 缓存复用），
        // 因此可以安全地逐项订阅，不存在 FrameViewModel 那种订阅爆炸问题。
        _localization.CultureChanged += (_, _) => OnPropertyChanged(nameof(DisplayName));
    }

    /// <summary>原始枚举值。ComboBox 通过 SelectedValueBinding 绑定到此。</summary>
    public object Value { get; }

    public string DisplayName => _localization[_key];

    public override string ToString() => DisplayName;
}

/// <summary>
/// 枚举选项提供者。
///
/// 按枚举类型缓存 —— 每个枚举值全应用只创建一个 <see cref="LocalizedEnumItem"/>，
/// 无论开多少个会话。这既控制了 CultureChanged 的订阅数量，
/// 也让 ComboBox 的 SelectedValue 比较保持稳定。
/// </summary>
public interface IEnumChoiceProvider
{
    IReadOnlyList<LocalizedEnumItem> GetChoices<TEnum>() where TEnum : struct, Enum;
}

/// <inheritdoc />
public sealed class EnumChoiceProvider(ILocalizationService localization) : IEnumChoiceProvider
{
    private readonly ConcurrentDictionary<Type, IReadOnlyList<LocalizedEnumItem>> _cache = new();

    public IReadOnlyList<LocalizedEnumItem> GetChoices<TEnum>() where TEnum : struct, Enum =>
        _cache.GetOrAdd(typeof(TEnum), _ => Enum.GetValues<TEnum>()
            .Select(v => new LocalizedEnumItem(v, localization))
            .ToArray());
}
