using System.Globalization;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiSerial.App.Localization;

// Resolve localized text.
public sealed class LocalizedTextResolver(
    ILocalizationService localization,
    ILogger<LocalizedTextResolver>? logger = null) : ILocalizedTextResolver
{
    private readonly ILogger _logger = logger ?? NullLogger<LocalizedTextResolver>.Instance;

    // Resolve localized display text.
    public string? Resolve(LocalizableText? text)
    {
        if (text is null) return null;

        if (text.HasKey && localization.Find(text.Key) is { } template)
        {
            return Format(template, text.Args);
        }

        if (!string.IsNullOrEmpty(text.Fallback))
        {
            return Format(text.Fallback, text.Args);
        }

        return text.HasKey ? $"!{text.Key}!" : null;
    }

    // Format resource arguments.
    private string Format(string template, IReadOnlyList<object?> args)
    {
        if (args.Count == 0) return template;

        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, [.. args]);
        }
        catch (FormatException e)
        {
            _logger.LogDebug(e, "Format failed for template {Template}; showing it as-is.", template);
            return template;
        }
    }
}
