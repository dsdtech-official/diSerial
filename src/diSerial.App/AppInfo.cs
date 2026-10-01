using System.Reflection;

namespace DiSerial.App;

// Read assembly product facts.
internal static class AppInfo
{
    public static string DisplayVersion { get; } = ComputeDisplayVersion();

    public static bool IsPackagedBuild { get; } = ComputeIsPackagedBuild();

    internal const string PackagedBuildKey = "DiSerial.PackagedBuild";

    // Detect a packaged application.
    private static bool ComputeIsPackagedBuild() =>
        IsPackagedMarker(typeof(AppInfo).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => string.Equals(a.Key, PackagedBuildKey, StringComparison.Ordinal))
            ?.Value);

    // Detect the packaged marker.
    internal static bool IsPackagedMarker(string? value) =>
        !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);

    // Build the displayed version.
    private static string ComputeDisplayVersion()
    {
        var assembly = typeof(AppInfo).Assembly;

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus < 0 ? informational : informational[..plus];
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
