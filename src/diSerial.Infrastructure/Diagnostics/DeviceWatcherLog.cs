using DiSerial.Core.Models;
using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Diagnostics;

/// <summary>
/// 端口插拔监视的日志事件定义（C-03a）。
///
/// 兑现 <see href="../../../docs/03-conventions.md">03-conventions</see> 8.4 诊断契约里的那一行：
/// <b>「每轮端口轮询：完整列表 + 与上轮的增减 diff」，Debug 级，服务 <c>Q-7</c>。</b>
///
/// 事件号 <b>13xx</b>，与 <see cref="SerialPortLog"/> 的 10xx / 11xx / 12xx 并列 ——
/// 那一份管的是**某一个端口**的生命周期与收发，本份管的是**端口列表**本身。
/// 类别名 <c>Devices</c>，与 <c>Serial.{portName}</c> / <c>Session</c> / <c>Diagnostics.*</c>
/// 同一风格（短、可作为过滤维度）。
///
/// <b>两档的分工</b>：
/// <list type="bullet">
///   <item><b><c>Debug</c></b> —— 只在**有增减**时记，带 diff 与完整列表。日常排查看这一档</item>
///   <item><b><c>Trace</c></b> —— **每轮**都记一条，用来回答「轮询到底还在跑吗」。
///         1.5 秒一轮，40 行/分钟，故压在 Trace</item>
/// </list>
///
/// 契约原文写的是「每轮」，本实现把它拆成两档而不是一档全记 ——
/// 依据是 8.2 ②「逐块日志永远不能是 Information」的同一个道理：
/// 无变化的那 40 行/分钟对**读日志的人**是纯噪音，但对**「轮询死了没有」**这个问题是唯一证据。
/// 用现成的 <c>logLevel</c> 旋钮把两者分开，不新增开关。
///
/// ⚠️ <c>trace</c> 这一档现在可用了 —— P1-24 把 Avalonia 封顶在 Warning 之后，
/// 调到 trace 不再会被布局日志淹掉。
/// </summary>
public static partial class DeviceWatcherLog
{
    [LoggerMessage(EventId = 1301, Level = LogLevel.Debug,
        Message = "Ports changed: +{AddedCount} -{RemovedCount}, total {Total}; added=[{Added}] removed=[{Removed}] current=[{Current}]")]
    public static partial void PortsChanged(
        ILogger logger, int addedCount, int removedCount, int total,
        string added, string removed, string current);

    /// <summary>无增减的那些轮次。压在 Trace，见类注释。</summary>
    [LoggerMessage(EventId = 1302, Level = LogLevel.Trace,
        Message = "Polled {Total} ports, no change")]
    public static partial void PolledNoChange(ILogger logger, int total);

    /// <summary>
    /// 监视启动，带基线快照。
    ///
    /// <b>基线要记下来</b>：启动时已存在的端口刻意**不**报成「刚插入」，
    /// 所以第一条 <see cref="PortsChanged"/> 之前发生了什么，只有这条能说明。
    /// 这是 8.4.5「关键动作要在开始也留痕」的直接要求。
    /// </summary>
    [LoggerMessage(EventId = 1303, Level = LogLevel.Debug,
        Message = "Port watch started, baseline {Total} ports: [{Baseline}]")]
    public static partial void Started(ILogger logger, int total, string baseline);

    [LoggerMessage(EventId = 1304, Level = LogLevel.Debug,
        Message = "Port watch stopped")]
    public static partial void Stopped(ILogger logger);

    /// <summary>
    /// The poll loop died on an unexpected exception (P1-46).
    ///
    /// <b>Error, and deliberately not Debug</b> -- the opposite call from the per-round
    /// enumeration failures, which repeat every 1.5s and are therefore pressed down to
    /// Debug. This one fires <b>at most once per watcher</b> and it means hot-plug
    /// detection is gone until the app restarts. A rule that says "anything repeatable
    /// goes to Debug" only works if the non-repeatable things actually stay loud.
    ///
    /// <b>What it is evidence of</b>: before P1-46 the loop could die here with nothing
    /// written anywhere, while <c>IsRunning</c> kept reporting true. This line and that
    /// flag are now the two halves of the same statement.
    /// </summary>
    [LoggerMessage(EventId = 1305, Level = LogLevel.Error,
        Message = "Port watch loop faulted and stopped; hot-plug detection is no longer running ({ExceptionType})")]
    public static partial void Faulted(ILogger logger, Exception exception, string exceptionType);

    /// <summary>
    /// A <c>PortsChanged</c> subscriber threw, and the poll loop carried on regardless (P2-64).
    ///
    /// <para>⚠️ <b>Read together with <see cref="Faulted"/> above — the two are deliberately
    /// different.</b> That one says hot-plug detection has <b>stopped</b>; this one says one
    /// notification was lost and the watcher is still running. Before 2026-08-05 a throwing
    /// subscriber produced the first message, because it escaped into the loop's outer catch.
    /// Distinguishing them matters to whoever reads a support log: "the dropdown stopped
    /// updating forever" and "one refresh was dropped" call for very different responses.</para>
    ///
    /// <para><b>Error, not Warning</b>: nothing here can repeat on a timer — a subscriber that
    /// throws every time will produce one line per actual port change, which is as often as the
    /// user plugs something in. ⛔ The handler is our own code, so this firing at all means a
    /// defect upstream, never a condition of the environment.</para>
    /// </summary>
    [LoggerMessage(EventId = 1306, Level = LogLevel.Error,
        Message = "A PortsChanged subscriber threw ({ExceptionType}); that notification was lost "
            + "but hot-plug detection is still running")]
    public static partial void SubscriberFailed(
        ILogger logger, Exception exception, string exceptionType);

    /// <summary>
    /// The baseline enumeration inside <c>StartAsync</c> failed, and the watch started
    /// anyway with no baseline (P2-80).
    ///
    /// <para><b>Error, like <see cref="Faulted"/> and for the same reason</b>: it fires at
    /// most once per watcher, and it is the only record that the first round's comparison
    /// had nothing to compare against. Everything about the run that follows is normal, so
    /// no other line would ever hint at it.</para>
    ///
    /// <para>⛔ <b>What used to happen instead was nothing at all.</b> The enumeration sat
    /// outside the try, so a failure threw out of <c>StartAsync</c> before the loop was ever
    /// created; the App layer discarded that task with <c>_ =</c>, and hot-plug detection was
    /// gone for the rest of the process without a single line anywhere. The fix is that the
    /// loop starts regardless; this line is how anyone finds out the start was degraded.</para>
    /// </summary>
    [LoggerMessage(EventId = 1307, Level = LogLevel.Error,
        Message = "Baseline port enumeration failed ({ExceptionType}); the watch is starting "
            + "without one, and the first poll will adopt whatever it finds")]
    public static partial void BaselineFailed(
        ILogger logger, Exception exception, string exceptionType);

    /// <summary>
    /// The first poll after a failed baseline supplied one (P2-80).
    ///
    /// <para>⭐ <b>That round deliberately reports no diff.</b> With no baseline every port
    /// present would otherwise be announced as freshly inserted, which is precisely the
    /// "the tool is lying" shape (00-STATUS tier 1) that the baseline exists to prevent
    /// (see <see cref="Started"/>). Adopting silently and diffing from the next round on is
    /// the honest reading: nothing is known to have changed, because nothing was known.</para>
    ///
    /// <para><b>Debug, not Error</b>: by the time this fires the watch is healthy again, and
    /// the failure itself was already recorded loudly by <see cref="BaselineFailed"/>.</para>
    /// </summary>
    [LoggerMessage(EventId = 1308, Level = LogLevel.Debug,
        Message = "Baseline recovered on the first poll: {Total} ports [{Baseline}]; "
            + "no change is reported for this round")]
    public static partial void BaselineRecovered(ILogger logger, int total, string baseline);

    /// <summary>
    /// 端口列表转成一行可读文本。空列表给出 <c>-</c> 而不是空字符串 ——
    /// 否则日志里 <c>added=[]</c> 与 <c>added=[ ]</c> 看不出区别。
    /// </summary>
    public static string Describe(IReadOnlyList<SerialPortInfo> ports) =>
        ports.Count == 0 ? "-" : string.Join(", ", ports.Select(p => p.PortName));
}
