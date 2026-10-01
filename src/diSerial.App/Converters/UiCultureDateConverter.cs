using System.Globalization;
using Avalonia.Data.Converters;
using DiSerial.App.Localization;

namespace DiSerial.App.Converters;

// Parse and format UI dates.
public sealed class UiCultureDateConverter(ILocalizationService localization) : IValueConverter
{
    internal const string AmericanPattern = "MM'/'dd'/'yyyy";

    // Choose the UI date pattern.
    internal static string PatternFor(CultureInfo culture)
    {
        if (IsEnglish(culture)) return AmericanPattern;

        var pattern = culture.DateTimeFormat.ShortDatePattern;
        var separator = culture.DateTimeFormat.DateSeparator;

        return string.IsNullOrEmpty(separator)
            ? pattern
            : pattern.Replace(separator, $"'{separator}'", StringComparison.Ordinal);
    }

    // Format the date input hint.
    internal static string HintFor(CultureInfo culture) =>
        PatternFor(culture).Replace("'", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    // Parse a date in the UI culture.
    internal static DateTime? Parse(string? text, CultureInfo culture)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var trimmed = text.Trim();

        return DateTime.TryParseExact(
                   trimmed, PatternFor(culture), culture, DateTimeStyles.None, out var exact)
            ? exact.Date
            : DateTime.TryParse(trimmed, culture, DateTimeStyles.None, out var lenient)
                ? lenient.Date
                : null;
    }

    public string Hint => HintFor(localization.CurrentCulture);

    // Format a date in the UI culture.
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var ui = localization.CurrentCulture;

        return value switch
        {
            DateTime d => d.ToString(PatternFor(ui), ui),
            DateTimeOffset o => o.DateTime.ToString(PatternFor(ui), ui),
            _ => string.Empty
        };
    }

    // Parse a date in the UI culture.
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Parse(value as string, localization.CurrentCulture);

    // Check for an English culture.
    private static bool IsEnglish(CultureInfo culture) =>
        string.Equals(culture.TwoLetterISOLanguageName, "en", StringComparison.OrdinalIgnoreCase);
}
