using System.Globalization;
using DiSerial.Core.Abstractions;
using DiSerial.Infrastructure.Diagnostics;
using DiSerial.Infrastructure.Time;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Serilog.Formatting.Compact;

namespace DiSerial.App.Composition;

/// <summary>
/// 日志装配 —— <b>整个应用唯一引用 Serilog 的位置</b>。
///
/// 其余代码只依赖 <c>ILogger&lt;T&gt;</c>（Microsoft.Extensions.Logging 的标准抽象），
/// 因此换掉 Serilog 只需要改本文件。这与组合根对 Infrastructure 的处理是同一套路。
///
/// <b>⚠️ 必须在 DI 容器建立之前调用</b>（见 <c>Program.Main</c>）。
/// 缺 ICU 时应用会崩在解析 <c>LocalizationService</c> 的过程中，
/// 也就是在窗口出现之前 —— 若 logger 是容器里的服务，
/// 这一类最有价值的崩溃恰好一条日志都留不下。
/// </summary>
public static class LoggingBootstrap
{
    private const string TextTemplate =
        "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// 当前进程的日志管线。由 <c>Program.Main</c> 在最早期设置一次。
    ///
    /// 这里用静态入口是因为 Avalonia 自行构造 <c>App</c>，拿不到构造参数 ——
    /// 与 <c>LocalizationService.Current</c> 属同一类框架限制下的妥协。
    /// 区别在于本属性<b>有安全的默认值</b>：未初始化时（如可视化设计器加载）
    /// 退化为不记日志，而不是抛异常 —— 日志不该成为应用起不来的原因。
    /// </summary>
    public static LoggingRuntime Current { get; private set; } = LoggingRuntime.Disabled();

    /// <summary>
    /// 建立日志管线并写入启动横幅。
    ///
    /// <b>日志本身绝不能成为应用起不来的原因</b>：目录不可写等情况一律降级为
    /// 「不写文件」，而不是抛出。
    /// </summary>
    public static LoggingRuntime Initialize()
    {
        // ⚠️ 顺序在 2026-07-28 反转过，改动前请读完这段。
        //
        // 日志级别现在来自 diserial.dev.json（原 DISERIAL_LOG 环境变量），
        // 所以开发者配置必须先于 logger 读出来。但 DeveloperOptions.Load 又需要
        // 一个 logger 来报告「文件损坏 / schemaVersion 不符 / 没开任何开关」——
        // 鸡生蛋。做法是读两遍：
        //
        //   1. logger: null 读一遍，只为拿到 logLevel，不产生任何诊断
        //   2. 用该级别建立 logger
        //   3. 再读一遍，这次带 logger —— 把第 1 步咽下去的诊断补记出来
        //
        // 第 3 步不能省：省掉的后果是「dev.json 读坏了，却没有任何日志说这件事」，
        // 而那正是这个文件的容错设计刻意要避免的（受众是开发者，静默降级会让人查半天）。
        // 文件只有几百字节，读两遍的代价可以忽略。
        var developer = DeveloperOptions.Load(logger: null);
        var options = LoggingOptions.From(developer);

        // 这个时钟实例随后会被注册进 DI 容器复用。
        // 必须是同一个实例 —— MonotonicClock 以构造时的墙钟为原点，
        // 而墙钟在 Windows 上分辨率仅 15.6ms，两个实例的原点可能差出一整个刻度，
        // 那样日志时间戳就与 SerialFrame 对不齐了。
        var clock = new MonotonicClock();

        // 路径在日志之前就要定下来 —— 设置与日志共用同一个根目录，
        // 由这一个实例统一给出，避免两处各算一遍（见 IAppPaths）。
        var paths = AppPaths.CreateDefault();

        if (options.IsDisabled)
        {
            return Current = new LoggingRuntime(
                NullLoggerFactory.Instance, clock, options, paths,
                logDirectory: null, logger: null, developer: developer);
        }

        var logger = TryCreateLogger(options, clock, paths, out var directory);

        if (logger is null)
        {
            return Current = new LoggingRuntime(
                NullLoggerFactory.Instance, clock, options, paths,
                logDirectory: null, logger: null, developer: developer);
        }

        var factory = new SerilogLoggerFactory(logger, dispose: false);

        // 第 3 步：补记第 1 步咽下去的诊断。返回值丢弃 —— 配置已经在上面拿到了，
        // 这一遍只为让「读文件时发生了什么」进日志。
        _ = DeveloperOptions.Load(logger: factory.CreateLogger("Diagnostics.Developer"));

        // 把 Avalonia 框架自己的日志接进同一个管线。
        //
        // ⚠️ **与 debugMode 无关，两种形态都接**（见 AvaloniaLogBridge 的注释）：
        // 绑定错误只出现在 Avalonia 的日志里，而它是静默失败 ——
        // 用户形态承诺「异常不得静默丢失」，这一条正在其范围内。
        // 音量由 logLevel 决定，与项目其余日志共用同一个旋钮。
        //
        // ⚠️ 必须在 BuildAvaloniaApp() 之前设好，且 Program 里不能再调 .LogToTrace() ——
        // 那个扩展方法会把 Logger.Sink 覆盖掉。
        Avalonia.Logging.Logger.Sink = new AvaloniaLogBridge(factory);

        var runtime = new LoggingRuntime(factory, clock, options, paths, directory, logger, developer);

        WriteStartupBanner(factory, clock, directory!, developer);
        return Current = runtime;
    }

    /// <summary>
    /// 挂上进程级的兜底捕获。
    ///
    /// 没有这两条，缺 ICU 那类「窗口出现前就崩」的故障只会往 stderr 吐一段栈，
    /// GUI 进程下通常没人看得到。
    /// </summary>
    public static void HookUnhandledExceptions(ILoggerFactory factory)
    {
        var logger = factory.CreateLogger("Diagnostics.Crash");

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                // 从 Main 抛出的异常会同时走到这里与 Main 的 catch。
                // 去重后只留一条 —— 崩溃栈动辄数 KB，记两遍纯属噪音。
                if (MarkLogged(ex))
                {
                    logger.LogCritical(ex, "Unhandled exception, terminating={Terminating}", e.IsTerminating);
                }
            }
            else
            {
                logger.LogCritical("Unhandled non-exception throw, terminating={Terminating}", e.IsTerminating);
            }

            Log.CloseAndFlush();
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            logger.LogError(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };
    }

    /// <summary>
    /// 同一个异常实例是否尚未被记录过。
    ///
    /// 保留 AppDomain 兜底而不是只靠 <c>Main</c> 的 catch，是因为
    /// <b>后台线程上的未捕获异常永远走不到 Main</b>；反过来只留兜底也不行 ——
    /// 某些终止路径上它不保证被调用。两个都留，靠这里去重。
    /// </summary>
    internal static bool MarkLogged(Exception exception)
        => Interlocked.Exchange(ref _lastLogged, exception) != exception;

    private static Exception? _lastLogged;

    private static Logger? TryCreateLogger(
        LoggingOptions options, IMonotonicClock clock, IAppPaths paths, out string? directory)
    {
        directory = null;

        try
        {
            Directory.CreateDirectory(paths.LogDirectory);
            directory = paths.LogDirectory;

            var level = ToSerilogLevel(options.MinimumLevel);

            return new LoggerConfiguration()
                .MinimumLevel.Is(level)

                // ⚠️ Avalonia 一律封顶在 Warning，**不随 logLevel 放开**（P1-24，2026-07-29）。
                //
                // 起因：AvaloniaLogBridge 刻意把 Avalonia 的 Information 降级为 MEL 的 Debug
                // （Info 级每次布局记两条，拖一次窗口几百条）。于是 debug 这一档同时成了
                // 「串口读块」与「Avalonia 布局细节」的开关 —— 而后者的量级压倒前者。
                //
                // 撞车的两处文档各自都对：
                //   AvaloniaLogBridge 注释       「想看布局细节就把 logLevel 调到 debug」
                //   Q-1 的验证方案（1c）          「把 logLevel 调到 debug 看 ReadChunk」
                // 实测：串口数据必须靠 -NotMatch "Avalonia.Layout" 过滤才读得出来。
                //
                // 选 A（压整个 Avalonia）而不是只压 Layout / Visual / Property，理由是
                // 后者要维护一份「靠记得去更新」的 area 清单，而那份清单写不成护栏 ——
                // 无法静态知道新版本 Avalonia 又添了哪个高频 area。
                //
                // 代价已明确接受：失去「调 debug 看 Avalonia 细节」的能力。
                // 至今浮现过的 Avalonia 问题（P1-20 Binding / P1-21 OpenGL / P1-22 Control）
                // **全是 Warning 级**，一条都不会被这行挡掉。
                //
                // ⚠️ 但「布局日志没用」是错的，别照着这句话把它当垃圾。它有三个真用途：
                //   1. 抓布局循环（Arrange 里改了影响 Measure 的属性 -> 反复触发不收敛）。
                //      症状是界面卡死而调用栈看不出问题，日志几乎是唯一线索
                //   2. 量化布局开销 —— **P1-6 换自绘控件前的性能对比正需要它**
                //   3. 判断控件有没有进入布局树（"不显示"时区分尺寸为 0 还是没进树）
                //
                // 需要上述任一项时，把本行的 Warning 临时改成 Debug 即可。
                // 之所以默认关掉而不是默认打开：那三件事都是**专门的排查活动**，
                // 而 ReadChunk 是**每次接硬件都要看**的东西。默认服务后者。
                .MinimumLevel.Override("Avalonia", LogEventLevel.Warning)

                .Enrich.With(new MonotonicTimestampEnricher(clock))
                .Enrich.WithProperty("pid", Environment.ProcessId)

                // 人读的一份。
                .WriteTo.Async(a => a.File(
                    Path.Combine(paths.LogDirectory, "diserial-.log"),
                    outputTemplate: TextTemplate,
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: LoggingOptions.FileSizeLimitBytes,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: LoggingOptions.RetainedFileCount,
                    shared: false))

                // 机器读的一份：JSON Lines，一行一个事件，字段可直接取用。
                // 与文本那份同时写 —— 不做成按需开启，是因为「按需」意味着
                // 故障第一次发生时没有结构化记录，而不可复现的故障恰恰最难查。
                .WriteTo.Async(a => a.File(
                    new CompactJsonFormatter(),
                    Path.Combine(paths.LogDirectory, "diserial-.jsonl"),
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: LoggingOptions.FileSizeLimitBytes,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: LoggingOptions.RetainedFileCount,
                    shared: false))

                .CreateLogger();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // 目录不可写（只读介质、权限、路径非法）。降级为不写文件 ——
            // 日志不可用是个遗憾，让应用起不来是个事故。
            return null;
        }
    }

    private static void WriteStartupBanner(
        ILoggerFactory factory, IMonotonicClock clock, string logDirectory, DeveloperOptions developer)
    {
        var diagnostics = PlatformDiagnostics.Create();
        var facts = diagnostics.Collect(new PlatformDiagnosticsContext(clock, logDirectory))
            // 记进横幅：拿到日志时要能一眼看出这次运行有没有开模拟数据。
            .Append(new KeyValuePair<string, string>(
                "developer.debugMode", developer.DebugMode.ToString()))
            // 回放同样会产生模拟数据，且它走的是真实采集链路 ——
            // 产出的帧在日志里与真硬件的帧长得一模一样。不记这一条，
            // 拿到日志的人无从判断那批报文是不是回放脚本跑出来的。
            // coalesced 与 on 要能分辨：前者是攒帧模拟，对 Q-1 的结论意义不同。
            .Append(new KeyValuePair<string, string>(
                "developer.replay",
                developer.DebugMode ? developer.Replay ?? "off" : "off (user form)"))
            // 级别也记一条：日志里少了什么，可能只是因为音量调低了。
            .Append(new KeyValuePair<string, string>(
                "developer.logLevel", developer.LogLevel ?? "info"))
            // 分帧阈值覆盖（01-spec 4.8.5）：它直接改变界面上「一行是什么」——
            // 覆盖成 0 时每块一行，屏幕上的分行与默认状态完全不同。
            // 不记这一条，拿到日志的人会拿着一份按非默认阈值切出来的帧去推断驱动行为。
            // 与 developer.replay 是同一个道理：能改变屏幕内容的配置必须留痕（原 P2-7 的教训）。
            .Append(new KeyValuePair<string, string>(
                "developer.idleGap", developer.IdleGapDescription));

        factory.CreateLogger("Diagnostics.Startup").LogInformation(
            "diSerial starting on {Platform}. Environment: {@Environment}",
            diagnostics.PlatformName,
            facts.ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal));
    }

    private static LogEventLevel ToSerilogLevel(LogLevel level) => level switch
    {
        LogLevel.Trace => LogEventLevel.Verbose,
        LogLevel.Debug => LogEventLevel.Debug,
        LogLevel.Information => LogEventLevel.Information,
        LogLevel.Warning => LogEventLevel.Warning,
        LogLevel.Error => LogEventLevel.Error,
        LogLevel.Critical => LogEventLevel.Fatal,
        _ => LogEventLevel.Information
    };
}

/// <summary>
/// 日志管线的运行期句柄。由 <c>Program.Main</c> 持有，进程退出时释放。
///
/// 同时承载那个必须被 DI 复用的 <see cref="Clock"/> ——
/// 见 <see cref="LoggingBootstrap.Initialize"/> 里关于时钟原点的说明。
/// </summary>
public sealed class LoggingRuntime(
    ILoggerFactory loggerFactory,
    IMonotonicClock clock,
    LoggingOptions options,
    IAppPaths paths,
    string? logDirectory,
    Logger? logger,
    DeveloperOptions? developer = null) : IDisposable
{
    public ILoggerFactory LoggerFactory { get; } = loggerFactory;

    /// <summary>开发者开关（<c>diserial.dev.json</c>）。文件不存在时为全关。</summary>
    public DeveloperOptions Developer { get; } = developer ?? DeveloperOptions.Disabled;

    /// <summary>与串口时间戳同源的时钟。组合根必须把它注册为 <c>IMonotonicClock</c> 单例。</summary>
    public IMonotonicClock Clock { get; } = clock;

    public LoggingOptions Options { get; } = options;

    /// <summary>设置与日志共用的路径来源。组合根据此构造 <c>IAppSettings</c>。</summary>
    public IAppPaths Paths { get; } = paths;

    /// <summary>实际写入的目录；为 null 表示日志已关闭或目录不可写。</summary>
    public string? LogDirectory { get; } = logDirectory;

    /// <summary>不记日志的空实现，供未初始化场景（如可视化设计器）使用。</summary>
    public static LoggingRuntime Disabled() => new(
        NullLoggerFactory.Instance,
        new MonotonicClock(),
        new LoggingOptions { MinimumLevel = LogLevel.None },
        AppPaths.CreateDefault(),
        logDirectory: null,
        logger: null);

    public void Dispose()
    {
        // 异步 sink 需要显式 flush，否则退出时会丢掉队列里未落盘的事件 ——
        // 而崩溃前最后那几条恰恰是最要紧的。
        logger?.Dispose();
        Log.CloseAndFlush();
    }
}

/// <summary>
/// 给每条日志补一个与 <c>SerialFrame.Timestamp</c> 同源的时间戳。
///
/// <b>为什么不能只靠 Serilog 自带的 Timestamp</b>：那个取自 <c>DateTimeOffset.Now</c>，
/// Windows 上分辨率仅 15.6ms。而本产品要分辨的是 4ms 级的帧间隔 ——
/// 用系统时钟打点的日志无法与数据帧逐条对齐，诊断价值大打折扣。
/// </summary>
internal sealed class MonotonicTimestampEnricher(IMonotonicClock clock) : ILogEventEnricher
{
    private readonly DateTimeOffset _origin = clock.Now;

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory factory)
    {
        var now = clock.Now;

        // 绝对值用于与 SerialFrame 对齐；相对毫秒用于快速看事件间隔。
        logEvent.AddPropertyIfAbsent(factory.CreateProperty("MonoTime", now));
        logEvent.AddPropertyIfAbsent(factory.CreateProperty(
            "MonoMs", Math.Round((now - _origin).TotalMilliseconds, 3)));
    }
}
