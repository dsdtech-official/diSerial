using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace DiSerial.Infrastructure.Localization;

// Read the macOS UI language.
[SupportedOSPlatform("macos")]
public sealed class MacSystemUiLocale : ISystemUiLocale
{
    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const uint KCFStringEncodingUtf8 = 0x08000100;

    // Read the preferred UI language.
    public CultureInfo? GetPreferred()
    {
        try
        {
            var name = CopyFirstPreferredLanguage();
            if (string.IsNullOrWhiteSpace(name)) return null;

            return CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    // Read the first macOS language.
    private static string? CopyFirstPreferredLanguage()
    {
        var key = CFStringCreateWithCString(IntPtr.Zero, "AppleLanguages", KCFStringEncodingUtf8);
        var app = CFStringCreateWithCString(IntPtr.Zero, "kCFPreferencesAnyApplication", KCFStringEncodingUtf8);
        if (key == IntPtr.Zero || app == IntPtr.Zero) return null;

        try
        {
            var value = CFPreferencesCopyAppValue(key, app);
            if (value == IntPtr.Zero) return null;

            try
            {
                if (CFGetTypeID(value) != CFArrayGetTypeID() || CFArrayGetCount(value) < 1) return null;
                return ReadString(CFArrayGetValueAtIndex(value, 0));
            }
            finally
            {
                CFRelease(value);
            }
        }
        finally
        {
            CFRelease(key);
            CFRelease(app);
        }
    }

    // Decode a Core Foundation string.
    private static string? ReadString(IntPtr cfString)
    {
        if (cfString == IntPtr.Zero) return null;

        if (CFGetTypeID(cfString) != CFStringGetTypeID()) return null;

        var buffer = new byte[64];
        return CFStringGetCString(cfString, buffer, buffer.Length, KCFStringEncodingUtf8)
            ? Encoding.UTF8.GetString(buffer).TrimEnd('\0')
            : null;
    }

    // Create a Core Foundation string.
    [DllImport(CoreFoundation)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string value, uint encoding);

    // Copy a macOS preference value.
    [DllImport(CoreFoundation)]
    private static extern IntPtr CFPreferencesCopyAppValue(IntPtr key, IntPtr applicationId);

    // Read a native string as UTF-8.
    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFStringGetCString(IntPtr value, byte[] buffer, long size, uint encoding);

    // Read the native array size.
    [DllImport(CoreFoundation)]
    private static extern long CFArrayGetCount(IntPtr array);

    // Read a native array item.
    [DllImport(CoreFoundation)]
    private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, long index);

    // Read a native object's type ID.
    [DllImport(CoreFoundation)]
    private static extern IntPtr CFGetTypeID(IntPtr value);

    // Read the native array type ID.
    [DllImport(CoreFoundation)]
    private static extern IntPtr CFArrayGetTypeID();

    // Read the native string type ID.
    [DllImport(CoreFoundation)]
    private static extern IntPtr CFStringGetTypeID();

    // Release a native object.
    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr value);
}
