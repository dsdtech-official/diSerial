using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DiSerial.Core.Abstractions;

namespace DiSerial.Infrastructure.Diagnostics;

// Supply diagnostic context.
public sealed record PlatformDiagnosticsContext(IMonotonicClock Clock, string LogDirectory);

public interface IPlatformDiagnostics
{
    string PlatformName { get; }

    // Collect platform diagnostics.
    IReadOnlyList<KeyValuePair<string, string>> Collect(PlatformDiagnosticsContext context);
}

// Collect shared platform facts.
public abstract class PlatformDiagnosticsBase : IPlatformDiagnostics
{
    public abstract string PlatformName { get; }

    // Collect platform diagnostics.
    public IReadOnlyList<KeyValuePair<string, string>> Collect(PlatformDiagnosticsContext context)
    {
        var facts = new List<KeyValuePair<string, string>>
        {
            new("app.version", AppVersion()),
            new("os.description", RuntimeInformation.OSDescription),
            new("os.architecture", RuntimeInformation.OSArchitecture.ToString()),
            new("process.architecture", RuntimeInformation.ProcessArchitecture.ToString()),
            new("runtime.version", RuntimeInformation.FrameworkDescription),
            new("runtime.rid", RuntimeInformation.RuntimeIdentifier),

            new("globalization.invariantMode", IsInvariantGlobalization().ToString()),
            new("globalization.icuAvailable", IsIcuAvailable().ToString()),

            new("culture.current", CultureInfo.CurrentCulture.Name),
            new("culture.ui", CultureInfo.CurrentUICulture.Name),

            new("clock.timerResolutionMs", context.Clock.Resolution.TotalMilliseconds
                .ToString("G17", CultureInfo.InvariantCulture)),

            new("log.directory", context.LogDirectory)
        };

        facts.AddRange(CollectPlatformSpecific());
        return facts;
    }

    // Collect platform-specific facts.
    protected virtual IEnumerable<KeyValuePair<string, string>> CollectPlatformSpecific()
        => [];

    // Read the application version.
    private static string AppVersion()
    {
        var entry = Assembly.GetEntryAssembly();
        if (entry is null) return "unknown";

        var informational = entry
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        return string.IsNullOrWhiteSpace(informational)
            ? entry.GetName().Version?.ToString() ?? "unknown"
            : informational;
    }

    // Detect invariant globalization.
    private static bool IsInvariantGlobalization()
        => AppContext.TryGetSwitch("System.Globalization.Invariant", out var enabled) && enabled;

    // Check ICU globalization support.
    private static bool IsIcuAvailable()
    {
        try
        {
            _ = CultureInfo.GetCultureInfo("en-US");
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }
}

// Collect Windows platform facts.
[SupportedOSPlatform("windows")]
public sealed class WindowsPlatformDiagnostics : PlatformDiagnosticsBase
{
    public override string PlatformName => "Windows";

    // Collect platform-specific facts.
    protected override IEnumerable<KeyValuePair<string, string>> CollectPlatformSpecific()
    {
        yield return new KeyValuePair<string, string>(
            "windows.version", Environment.OSVersion.VersionString);
    }
}

// Collect portable platform facts.
public sealed class PortablePlatformDiagnostics(string platformName) : PlatformDiagnosticsBase
{
    public override string PlatformName { get; } = platformName;
}

// Select platform diagnostics.
public static class PlatformDiagnostics
{
    // Create platform diagnostics.
    public static IPlatformDiagnostics Create()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsPlatformDiagnostics();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return new PortablePlatformDiagnostics("Linux");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return new PortablePlatformDiagnostics("macOS");

        return new PortablePlatformDiagnostics("Unknown");
    }
}
