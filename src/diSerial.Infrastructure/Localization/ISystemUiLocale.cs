using System.Globalization;
using System.Runtime.InteropServices;

namespace DiSerial.Infrastructure.Localization;

public interface ISystemUiLocale
{
    // Read the preferred UI language.
    CultureInfo? GetPreferred();
}

// Resolve the system UI language.
public static class SystemUiLocale
{
    // Create a system locale provider.
    public static ISystemUiLocale Create()
        => RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? new MacSystemUiLocale()
            : new NoSystemUiLocale();
}

// Provide an empty locale result.
public sealed class NoSystemUiLocale : ISystemUiLocale
{
    // Read the preferred UI language.
    public CultureInfo? GetPreferred() => null;
}
