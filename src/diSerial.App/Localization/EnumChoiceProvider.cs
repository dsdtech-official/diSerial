using System.Collections.Concurrent;
using DiSerial.App.ViewModels;

namespace DiSerial.App.Localization;

// Represent a localized choice.
public sealed class LocalizedEnumItem : ViewModelBase
{
    private readonly ILocalizationService _localization;
    private readonly string _key;

    // Initialize a localized choice.
    internal LocalizedEnumItem(object value, ILocalizationService localization)
    {
        Value = value;
        _localization = localization;
        _key = $"Enum.{value.GetType().Name}.{value}";

        _localization.CultureChanged += (_, _) => OnPropertyChanged(nameof(DisplayName));
    }

    public object Value { get; }

    public string DisplayName => _localization[_key];

    // Return the display text.
    public override string ToString() => DisplayName;
}

public interface IEnumChoiceProvider
{
    // Create localized enum choices.
    IReadOnlyList<LocalizedEnumItem> GetChoices<TEnum>() where TEnum : struct, Enum;
}

// Provide localized enum choices.
public sealed class EnumChoiceProvider(ILocalizationService localization) : IEnumChoiceProvider
{
    private readonly ConcurrentDictionary<Type, IReadOnlyList<LocalizedEnumItem>> _cache = new();

    // Create localized enum choices.
    public IReadOnlyList<LocalizedEnumItem> GetChoices<TEnum>() where TEnum : struct, Enum =>
        _cache.GetOrAdd(typeof(TEnum), _ => Enum.GetValues<TEnum>()
            .Select(v => new LocalizedEnumItem(v, localization))
            .ToArray());
}
