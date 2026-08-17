using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DiSerial.Core.Abstractions;

namespace DiSerial.Infrastructure.Diagnostics;

/// <summary>启动横幅的采集上下文。新增需要上报的运行期事实时在此追加字段。</summary>
public sealed record PlatformDiagnosticsContext(IMonotonicClock Clock, string LogDirectory);

/// <summary>
/// The startup banner -- records "what environment is this running in" into the log once,
/// at process start.
///
/// <b>This is the most platform-specific piece of the logging component, and the main
/// seam left for macOS.</b> The facts worth reporting genuinely differ per platform:
///
/// <list type="bullet">
///   <item>macOS: the number of <c>/dev/cu.*</c> nodes (<c>tty.*</c> blocks waiting for
///         DCD, so the two must not be confused)</item>
///   <item>Windows: the current implementation, which adds the detailed version number</item>
/// </list>
///
/// To add a platform, write another implementation and add one branch to
/// <see cref="PlatformDiagnostics.Create"/>; the caller (<c>LoggingBootstrap</c>) does
/// not change at all.
/// </summary>
public interface IPlatformDiagnostics
{
    /// <summary>平台名，用于日志中区分横幅来源。</summary>
    string PlatformName { get; }

    /// <summary>
    /// 采集本次运行的环境事实。返回值保持插入顺序 —— 横幅是给人和机器读的，
    /// 字段顺序稳定才便于跨次运行做对比。
    /// </summary>
    IReadOnlyList<KeyValuePair<string, string>> Collect(PlatformDiagnosticsContext context);
}

/// <summary>
/// 三平台共有的事实。子类只负责追加自己特有的部分。
/// </summary>
public abstract class PlatformDiagnosticsBase : IPlatformDiagnostics
{
    public abstract string PlatformName { get; }

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

            // ⚠️ 这两条直接对应 P0-3：缺 ICU 时应用会在窗口出现之前就崩，
            // 且异常信息完全指不到病根。横幅里如实记下来，一眼可判。
            new("globalization.invariantMode", IsInvariantGlobalization().ToString()),
            new("globalization.icuAvailable", IsIcuAvailable().ToString()),

            new("culture.current", CultureInfo.CurrentCulture.Name),
            new("culture.ui", CultureInfo.CurrentUICulture.Name),

            // The timer's own period, stated honestly -- the product claims software-level,
            // not hardware-level, timestamps.
            //
            // ⛔ The key says "timer" on purpose (P2-109, 2026-08-15, user's call). It used to
            // be "clock.resolutionMs", which reads as "the precision you get" -- and that is
            // NOT what this number is. Observable serial-event precision is set by the USB
            // bridge's latency timer, measured at 16 ms on the FTDI loopback and up to 10.4 ms
            // of inter-chunk gap on a Prolific device (Q-1). That is three to five orders of
            // magnitude coarser than the value printed here.
            //
            // ⚠️ We deliberately do NOT print an observable-precision number: it depends on
            // whichever adapter the user plugged in, cannot be read at run time, and any
            // constant we picked would be a guess dressed as a measurement (03-conventions
            // 9.5). Renaming the key removes the false claim without inventing a new one.
            new("clock.timerResolutionMs", context.Clock.Resolution.TotalMilliseconds
                .ToString("G17", CultureInfo.InvariantCulture)),

            new("log.directory", context.LogDirectory)
        };

        facts.AddRange(CollectPlatformSpecific());
        return facts;
    }

    /// <summary>子类在此追加本平台特有的事实。默认无。</summary>
    protected virtual IEnumerable<KeyValuePair<string, string>> CollectPlatformSpecific()
        => [];

    /// <summary>
    /// The build identifier for the startup banner (P1-16, 2026-08-02).
    ///
    /// <para>⛔ <b>Not <c>GetName().Version</c></b>, which is what this used to read. That is
    /// <c>AssemblyVersion</c>: always four parts, and by convention held stable across patch
    /// releases so assembly references keep resolving. It reported <c>1.0.0.0</c> before any
    /// version was declared -- and it would still report <c>1.0.0.0</c> afterwards, so
    /// declaring <c>&lt;Version&gt;</c> alone would have left this banner line exactly as
    /// useless while looking fixed.</para>
    ///
    /// <para><c>AssemblyInformationalVersion</c> is the one that can tell two builds apart: the
    /// SDK appends <c>+&lt;git sha&gt;</c> to it for free, giving <c>1.0.0+d6b5937...</c>.</para>
    ///
    /// <para>⚠️ <b>The full SHA is kept, not shortened.</b> This line is also read by machines
    /// (the .jsonl sink), and "which commit exactly" is the entire question it exists to answer;
    /// a shortened hash is one collision away from not answering it. The About dialog is the
    /// place that shows users a short version -- see <c>AppInfo.DisplayVersion</c>.</para>
    ///
    /// <para>⚠️ Falls back to <c>GetName().Version</c> rather than to "unknown": a bare
    /// four-part number is still worth more than nothing when a host has stripped the
    /// attribute.</para>
    /// </summary>
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

    /// <summary>运行时是否处于全球化不变模式（此时任何具名区域都不可用）。</summary>
    private static bool IsInvariantGlobalization()
        => AppContext.TryGetSwitch("System.Globalization.Invariant", out var enabled) && enabled;

    /// <summary>
    /// 直接探测那个会导致崩溃的调用本身，而不是只读开关 ——
    /// 开关没打开但 ICU 库缺失时，行为同样是抛异常。
    /// </summary>
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

/// <summary>Windows 平台。当前唯一有针对性实现的平台。</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPlatformDiagnostics : PlatformDiagnosticsBase
{
    public override string PlatformName => "Windows";

    protected override IEnumerable<KeyValuePair<string, string>> CollectPlatformSpecific()
    {
        yield return new KeyValuePair<string, string>(
            "windows.version", Environment.OSVersion.VersionString);
    }
}

/// <summary>
/// Fallback implementation for macOS and any platform without a dedicated one.
///
/// It reports only the facts common to every platform. That is <b>not</b> a statement of
/// support -- it means "no platform-specific collection has been written yet". Replace it
/// with a dedicated implementation when adding a platform; see
/// <see cref="IPlatformDiagnostics"/>.
/// </summary>
public sealed class PortablePlatformDiagnostics(string platformName) : PlatformDiagnosticsBase
{
    public override string PlatformName { get; } = platformName;
}

/// <summary>按运行平台选择 <see cref="IPlatformDiagnostics"/> 实现。</summary>
public static class PlatformDiagnostics
{
    public static IPlatformDiagnostics Create()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsPlatformDiagnostics();
        }

        // A dedicated macOS implementation is still to be written; see the interface's
        // doc comment for what each platform should collect.
        //
        // ⚠ The Linux branch below is deliberately NOT deleted: this only decides what
        // the startup banner reports, and reporting the real platform name is strictly
        // better than falling through to "Unknown". diSerial is not released for Linux.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return new PortablePlatformDiagnostics("Linux");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return new PortablePlatformDiagnostics("macOS");

        return new PortablePlatformDiagnostics("Unknown");
    }
}
