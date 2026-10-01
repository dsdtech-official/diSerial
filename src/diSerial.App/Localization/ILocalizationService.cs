using System.ComponentModel;
using System.Globalization;

namespace DiSerial.App.Localization;

public interface ILocalizationService : INotifyPropertyChanged
{
    string this[string key] { get; }

    // Look up a translated string.
    string? Find(string key);

    // Get a live localized string.
    LocalizedString GetBindable(string key);

    // Format a translated string.
    string Format(string key, params object?[] args);

    CultureInfo CurrentCulture { get; }

    IReadOnlyList<LanguageOption> AvailableLanguages { get; }

    // Change the active UI language.
    void SetLanguage(CultureInfo culture);

    event EventHandler? CultureChanged;
}

// Describe a language choice.
public sealed record LanguageOption(string CultureName, string NativeName)
{
    public CultureInfo Culture { get; } = CultureInfo.GetCultureInfo(CultureName);

    // Return the display text.
    public override string ToString() => NativeName;
}

// Bind a localized resource.
public sealed class LocalizedString : INotifyPropertyChanged
{
    private readonly ILocalizationService _localization;
    private readonly string _key;

    // Initialize a live translation.
    internal LocalizedString(ILocalizationService localization, string key)
    {
        _localization = localization;
        _key = key;
        _localization.CultureChanged += (_, _) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
    }

    public string Value => _localization[_key];

    public event PropertyChangedEventHandler? PropertyChanged;

    // Return the display text.
    public override string ToString() => Value;
}
