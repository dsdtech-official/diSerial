using System.Runtime.InteropServices;

namespace DiSerial.Infrastructure.Diagnostics;

/// <summary>
/// 应用的落盘位置 —— <b>设置与日志的唯一来源</b>。
///
/// 抽出来的直接原因是重复：<c>settings.json</c> 与日志目录此前各自算了一遍
/// <c>Path.Combine(ApplicationData, "diSerial", ...)</c>。两处一致纯属巧合，
/// 没有任何东西保证它们不漂移。
///
/// 更重要的是它给 macOS 留了一处改动点：.NET 把
/// <see cref="Environment.SpecialFolder.ApplicationData"/> 在 macOS 上映射为
/// <c>~/.config</c>，而该平台的惯例是 <c>~/Library/Application Support/</c>
/// （日志则是 <c>~/Library/Logs/</c>）。要遵循惯例时，改这一个实现即可
/// <b>同时覆盖设置与日志</b>，而不是两处各改一遍。
/// </summary>
public interface IAppPaths
{
    /// <summary>
    /// Configuration directory. <see cref="SettingsDatabasePath"/> lives here.
    ///
    /// <para>⚠️ This said "settings.json lives here" until 2026-08-07 (P2-82). Pointing at
    /// the property rather than a file name is the actual fix: a name repeated in prose
    /// goes stale silently, a <c>cref</c> stops compiling.</para>
    /// </summary>
    string ConfigDirectory { get; }

    /// <summary>日志目录。刻意作为配置目录的子目录 —— 排查问题只需记住一个位置。</summary>
    string LogDirectory { get; }

    /// <summary>
    /// C-09 记录库（见 docs/02-architecture.md 第十二节）。
    ///
    /// ⚠️ <b>这是用户数据，不是诊断日志</b> —— 用户点「记录」录下来的东西存在这里，
    /// 而 <see cref="LogDirectory"/> 里是给开发者排查用的日志。
    /// 两者的取舍完全不同：日志目录写不了可以静默降级，
    /// 记录库写不了**必须告诉用户**（01-spec 4.7 第 6 条路径）。
    /// </summary>
    string RecordingDatabasePath { get; }

    /// <summary>
    /// User settings database (2026-08-07, replaces <c>settings.json</c>).
    ///
    /// <para>⛔ <b>A separate file from <see cref="RecordingDatabasePath"/>, deliberately.</b>
    /// Putting the settings table into the recordings database was considered and rejected: the
    /// two have opposite lifetimes. The recordings file grows without bound and has no delete
    /// entry point in V1.0 (00-STATUS P2-19), so the expected user action is eventually
    /// "delete it to reclaim disk" — which would take every preference with it, for someone
    /// who only meant to clear captured data.</para>
    ///
    /// <para>⚠️ The same tension already exists one level down and is flagged there:
    /// <c>send_history</c> is user configuration living in the recordings file. That one is
    /// worth its place because it is data the user produced; preferences are not.</para>
    /// </summary>
    string SettingsDatabasePath { get; }
}

/// <summary>
/// 默认实现：<c>%AppData%\diSerial\</c>（Linux / macOS 为 <c>~/.config/diSerial/</c>），
/// 日志在其下的 <c>logs</c> 子目录。三平台同一份实现，零平台特定代码。
/// </summary>
public sealed class AppPaths : IAppPaths
{
    public const string AppFolderName = "diSerial";
    public const string LogFolderName = "logs";
    public const string RecordingFileName = "recordings.db";
    public const string SettingsFileName = "settings.db";

    /// <summary>
    /// 日志目录**不可覆盖** —— 原先的 <c>DISERIAL_LOG_DIR</c> 已于 2026-07-28 取消，
    /// 全项目不再读任何环境变量。要改位置就改这个类，一处覆盖设置与日志。
    /// </summary>
    public AppPaths()
    {
        ConfigDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppFolderName);

        LogDirectory = Path.Combine(ConfigDirectory, LogFolderName);
        RecordingDatabasePath = Path.Combine(ConfigDirectory, RecordingFileName);
        SettingsDatabasePath = Path.Combine(ConfigDirectory, SettingsFileName);
    }

    public string ConfigDirectory { get; }

    public string LogDirectory { get; }

    public string RecordingDatabasePath { get; }

    public string SettingsDatabasePath { get; }

    /// <summary>
    /// 按运行平台选择实现。
    ///
    /// 当前三平台走同一条路径规则，故只有一个实现 —— 这个分支点先立在这里，
    /// 是为了让将来补 macOS 惯例路径时不必回头改调用方。
    ///
    /// <para>⭐ <b>Returns one shared instance, not a fresh one per call</b> (P2-33 ③).
    /// There are three call sites — logging bootstrap needs paths before any container exists,
    /// and the container needs the same paths afterwards. Each used to build its own object, so
    /// "the log directory the logger writes to" and "the log directory the container reports"
    /// agreed <b>by coincidence</b>: the constructor happens to be deterministic. ⛔ That is
    /// precisely the shape <see cref="IAppPaths"/> was introduced to remove — its own summary
    /// says changing this one implementation must cover settings and logs together, which is
    /// only true if there is one of it.</para>
    ///
    /// <para>⚠️ <b>What this does NOT fix, so nobody reads more into it</b>: the interface
    /// comment's claim that tests can point this at a temp directory is still not true here.
    /// Swapping it means registering a different <see cref="IAppPaths"/> in the container —
    /// which works for everything resolved from it, but <b>not</b> for logging bootstrap, which
    /// runs before the container by design (the startup banner has to survive a container that
    /// fails to build). Left as-is deliberately: the alternative is a settable static, and a
    /// settable static shared by a logger and a database is a worse trade.</para>
    /// </summary>
    public static IAppPaths CreateDefault() => Default;

    /// <summary>
    /// ⚠️ <c>Lazy</c> rather than a static field initialiser: this touches the environment
    /// (<see cref="Environment.SpecialFolder.ApplicationData"/>), and a type initialiser that
    /// throws is far harder to diagnose than a first call that does — the failure would surface
    /// as <c>TypeInitializationException</c> from whatever happened to touch the class first.
    /// </summary>
    private static readonly Lazy<IAppPaths> Lazy = new(() =>
    {
        // macOS 将来在此返回遵循 ~/Library 惯例的实现。
        _ = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        return new AppPaths();
    });

    private static IAppPaths Default => Lazy.Value;
}
