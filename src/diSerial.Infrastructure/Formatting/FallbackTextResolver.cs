using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiSerial.Infrastructure.Formatting;

// Resolve fallback text.
public sealed class FallbackTextResolver(ILogger<FallbackTextResolver>? logger = null)
    : ILocalizedTextResolver
{
    private readonly ILogger _logger = logger ?? NullLogger<FallbackTextResolver>.Instance;

    // Resolve fallback display text.
    public string? Resolve(LocalizableText? text)
    {
        if (text is null) return null;
        if (!string.IsNullOrEmpty(text.Fallback)) return Format(text.Fallback, text.Args);
        return text.HasKey ? text.Key : null;
    }

    // Format text with arguments.
    private string Format(string template, IReadOnlyList<object?> args)
    {
        if (args.Count == 0) return template;

        try
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture, template, [.. args]);
        }
        catch (FormatException e)
        {
            _logger.LogDebug(e, "Format failed for template {Template}; showing it as-is.", template);
            return template;
        }
    }
}
