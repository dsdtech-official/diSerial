using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace DiSerial.App.Localization;

// Bind localized XAML text.
public sealed class TranslateExtension : MarkupExtension
{
    // Initialize translation binding.
    public TranslateExtension() { }

    // Initialize translation binding.
    public TranslateExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    // Provide a localized XAML value.
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var source = LocalizationService.Current?.GetBindable(Key);

        if (source is null) return Key;

        return new Binding
        {
            Mode = BindingMode.OneWay,
            Source = source,
            Path = nameof(LocalizedString.Value)
        };
    }
}
