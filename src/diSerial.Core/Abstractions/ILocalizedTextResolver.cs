using DiSerial.Core.Models;

namespace DiSerial.Core.Abstractions;

public interface ILocalizedTextResolver
{
    // Resolve localized display text.
    string? Resolve(LocalizableText? text);
}
