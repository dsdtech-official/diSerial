using Avalonia.Logging;
using Microsoft.Extensions.Logging;
using AvaloniaLevel = Avalonia.Logging.LogEventLevel;
using MelLevel = Microsoft.Extensions.Logging.LogLevel;

namespace DiSerial.App.Composition;

/// <summary>
/// 把 Avalonia 框架自己的日志接进本项目的 <c>ILogger</c> 管线。
///
/// <b>为什么必须做这件事</b>：Avalonia 的 <b>绑定错误只出现在这里</b>，
/// 别处一个字都没有。而项目此前用的是 <c>.LogToTrace()</c>，
/// 它写到 <c>System.Diagnostics.Trace</c> 监听器 ——
/// <b>GUI 进程、没挂调试器时根本没有监听器，那些消息是丢掉的。</b>
///
/// 这与 P0-3 是同一个病：「只往 stderr 吐一段栈，GUI 进程下没人看得到」。
/// 而项目已经为此付过代价：<c>{loc:Translate}</c> 不能绑索引器那次，
/// 症状是「语言设置存下来了、界面纹丝不动」，排查成本很高。
///
/// <b>⚠️ 与 <c>debugMode</c> 无关，刻意无条件启用</b>（2026-07-28 明确决定）。三条理由：
///
/// <list type="number">
///   <item><b>绑定错误是静默失败，属于用户形态的承诺范围。</b>
///         01-spec 4.7 用户形态第 1 条：「异常不得静默丢失」。</item>
///   <item><b>发布版才最需要它</b> —— 用户现场挂不上调试器，日志文件是唯一证据。
///         挂到 debugMode 后面，等于在最需要证据的场合把证据关掉。</item>
///   <item><b>更简单</b>：「Avalonia 的日志和项目自己的日志走同一根管子、
///         同一个音量旋钮」比「开发形态才并入」少一条规则。</item>
/// </list>
///
/// <b>音量由 <c>logLevel</c> 决定</b>，不是由形态决定 —— 走
/// <see cref="ILogger.IsEnabled"/>，因此与项目其余日志用的是同一个旋钮。
/// </summary>
internal sealed class AvaloniaLogBridge(ILoggerFactory factory) : ILogSink
{
    /// <summary>
    /// 每个 area 一个 logger，类别名形如 <c>Avalonia.Binding</c>。
    ///
    /// 这样做是为了让 area 成为可过滤的维度 —— 排查绑定问题时
    /// 只看 <c>Avalonia.Binding</c> 那一类即可，不必在 Layout / Visual 的噪音里翻。
    /// </summary>
    private readonly Dictionary<string, ILogger> _loggers = [];
    private readonly Lock _gate = new();

    public bool IsEnabled(AvaloniaLevel level, string area)
        => LoggerFor(area).IsEnabled(ToMel(level));

    public void Log(AvaloniaLevel level, string area, object? source, string messageTemplate)
        => Write(level, area, source, messageTemplate, []);

    public void Log<T0>(
        AvaloniaLevel level, string area, object? source, string messageTemplate, T0 pv0)
        => Write(level, area, source, messageTemplate, [pv0]);

    public void Log<T0, T1>(
        AvaloniaLevel level, string area, object? source, string messageTemplate, T0 pv0, T1 pv1)
        => Write(level, area, source, messageTemplate, [pv0, pv1]);

    public void Log<T0, T1, T2>(
        AvaloniaLevel level, string area, object? source, string messageTemplate,
        T0 pv0, T1 pv1, T2 pv2)
        => Write(level, area, source, messageTemplate, [pv0, pv1, pv2]);

    public void Log(
        AvaloniaLevel level, string area, object? source, string messageTemplate,
        params object?[] propertyValues)
        => Write(level, area, source, messageTemplate, propertyValues);

    /// <summary>
    /// Avalonia 与 MEL 的消息模板约定相同（都是 <c>{Name}</c> + 位置绑定），
    /// 所以模板与参数可以直接透传，结构化字段名跟着 Avalonia 自己的命名。
    ///
    /// ⚠️ <b>绝不让日志把应用带下去</b>：模板与参数对不上时退回一条纯文本，
    /// 而不是把异常抛回 Avalonia 的渲染 / 布局路径。
    /// </summary>
    private void Write(
        AvaloniaLevel level, string area, object? source, string template, object?[] values)
    {
        var logger = LoggerFor(area);
        var mel = ToMel(level);

        if (!logger.IsEnabled(mel)) return;

        try
        {
            // source 是「哪个控件 / 哪个绑定」，对绑定错误尤其关键。
            // 追加在末尾而不是插在开头 —— 保持 Avalonia 原模板可被原样搜索。
            logger.Log(mel, template + " [source={AvaloniaSource}]",
                [.. values, Describe(source)]);
        }
        catch (Exception e)
        {
            logger.Log(mel, "Avalonia log formatting failed for template {Template}: {Reason}",
                template, e.Message);
        }
    }

    private ILogger LoggerFor(string area)
    {
        lock (_gate)
        {
            if (_loggers.TryGetValue(area, out var existing)) return existing;

            var created = factory.CreateLogger("Avalonia." + area);
            _loggers[area] = created;
            return created;
        }
    }

    /// <summary>只取类型名，不调 <c>ToString()</c> —— 控件的 ToString 可能很长或抛异常。</summary>
    private static string Describe(object? source)
        => source?.GetType().Name ?? "none";

    /// <summary>
    /// 级别映射。<b>Warning 及以上原样保留</b> —— 绑定错误正是 Warning，
    /// 必须在默认 <c>info</c> 音量下就能看到。
    ///
    /// ⚠️ <b>Avalonia 的 Information 刻意降级为 Debug。</b>
    /// 实测原因：<c>Avalonia.Layout</c> 在 Information 级别<b>每次布局记两条</b>
    /// （"Started layout pass" / "Layout pass finished"）—— 拖一次窗口就是几百条。
    /// 它记的是框架内部进展，不是产品事实，按本项目的日志预算不该占 info 这一档
    /// （见 03-conventions 8.2 ②「逐字节 / 逐块日志永远不能是 Information」，同一个道理）。
    ///
    /// 想看布局与属性系统的细节时把 <c>logLevel</c> 调到 <c>debug</c>，它们就回来了。
    /// </summary>
    private static MelLevel ToMel(AvaloniaLevel level) => level switch
    {
        AvaloniaLevel.Verbose => MelLevel.Trace,
        AvaloniaLevel.Debug => MelLevel.Trace,
        AvaloniaLevel.Information => MelLevel.Debug,
        AvaloniaLevel.Warning => MelLevel.Warning,
        AvaloniaLevel.Error => MelLevel.Error,
        AvaloniaLevel.Fatal => MelLevel.Critical,
        _ => MelLevel.Debug
    };
}
