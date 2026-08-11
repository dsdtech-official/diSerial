using System.Reflection;

namespace DiSerial.App;

/// <summary>
/// Product facts read off this assembly's own metadata (P1-16, 2026-08-02).
///
/// <para><b>Why this exists at all.</b> The version used to be written into the About text --
/// in both languages, by hand. That put a fact inside translatable content, which means two
/// copies that go stale independently and a translator who has to know it is not words. The
/// single source is now <c>Directory.Build.props</c>; everything else reads the attributes the
/// build stamps from it.</para>
///
/// <para>⚠️ <b>This is not a second source of truth next to
/// <c>PlatformDiagnosticsBase.AppVersion()</c>.</b> Both read the same
/// <c>AssemblyInformationalVersion</c> off the same entry assembly. They differ only in how
/// much of it each audience gets, and only one of the two parses anything:</para>
/// <list type="bullet">
///   <item>the startup banner takes the value <b>whole</b> (<c>1.0.0+&lt;full sha&gt;</c>) --
///   its job is to identify one exact build, including for machine readers;</item>
///   <item>the About dialog shows the part before <c>+</c> -- a user reads it to answer "which
///   release is this", and a 40-character hash does not help with that.</item>
/// </list>
///
/// <para>⛔ Do not move this into Core. Core is the domain layer and has no business asking the
/// hosting process what it is called; the closest precedent (<c>DurationText</c>, P1-47) moved
/// up because three layers had to render the same value identically, which is not the case
/// here -- there are two readers and one of them wants the raw string.</para>
/// </summary>
internal static class AppInfo
{
    /// <summary>
    /// Version as shown to a user, e.g. <c>1.0.0</c>.
    ///
    /// <para>⚠️ Build metadata (everything from <c>+</c> onwards) is dropped on purpose, and
    /// pre-release labels (<c>-rc.1</c>) are deliberately <b>kept</b>: "is this a release
    /// build" is exactly what a user needs to be able to see, while the commit hash is not.</para>
    ///
    /// <para>⚠️ Falls back to the four-part assembly version, and only then to a placeholder.
    /// An About box that says nothing is worse than one that says <c>1.0.0.0</c>.</para>
    /// </summary>
    public static string DisplayVersion { get; } = ComputeDisplayVersion();

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
