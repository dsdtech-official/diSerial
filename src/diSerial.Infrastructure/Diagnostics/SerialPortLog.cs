using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Diagnostics;

/// <summary>
/// 串口链路的日志事件定义。
///
/// <b>全部走 <c>[LoggerMessage]</c> 源生成器</b>，不用字符串插值 ——
/// 读循环是热路径，插值会在级别关闭时仍然产生装箱与字符串分配。
/// 源生成的版本在级别未启用时直接返回，零分配。
///
/// 事件号分段：
/// <code>
///   10xx  端口生命周期（打开 / 关闭 / 参数）
///   11xx  读路径
///   12xx  写路径
/// </code>
///
/// 消息一律英文 —— 日志属于开发者诊断信息，按项目约定不进资源文件
/// （<c>SourceConventionTests</c> 会扫出非 ASCII 字面量并让构建失败）。
/// </summary>
public static partial class SerialPortLog
{
    // ---- 10xx 端口生命周期 ----

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "Opening port {PortName} at {BaudRate} {DataBits}{Parity}{StopBits} flow={FlowControl}")]
    public static partial void Opening(
        ILogger logger, string portName, int baudRate,
        int dataBits, string parity, string stopBits, string flowControl);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information,
        Message = "Port {PortName} opened in {ElapsedMs:F1} ms")]
    public static partial void Opened(ILogger logger, string portName, double elapsedMs);

    /// <summary>
    /// 打开失败。异常类型与消息都要留下 —— 这是 P0-2（错误信息不暴露到界面）
    /// 目前唯一的现场取证途径，也是 Linux 上 dialout 权限被拒时的判据。
    /// </summary>
    [LoggerMessage(EventId = 1003, Level = LogLevel.Error,
        Message = "Failed to open port {PortName} after {ElapsedMs:F1} ms: {ExceptionType}")]
    public static partial void OpenFailed(
        ILogger logger, Exception exception, string portName, double elapsedMs, string exceptionType);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information,
        Message = "Closing port {PortName}")]
    public static partial void Closing(ILogger logger, string portName);

    /// <summary>
    /// Close completed. <paramref name="readLoopTimedOut"/> is the observation point for
    /// how <c>BaseStream.ReadAsync</c> responds to cancellation: true means the read loop
    /// did not answer within 2 seconds and only the subsequent Close broke it out.
    /// Known to happen on Windows.
    /// </summary>
    [LoggerMessage(EventId = 1005, Level = LogLevel.Information,
        Message = "Port {PortName} closed in {ElapsedMs:F1} ms, readLoopTimedOut={ReadLoopTimedOut}")]
    public static partial void Closed(
        ILogger logger, string portName, double elapsedMs, bool readLoopTimedOut);

    /// <summary>
    /// 关闭端口时抛异常 —— 最常见的成因是设备已被拔出。
    ///
    /// <b>Warning 而非 Debug</b>：关闭是一次性动作，不会重复触发，
    /// 而「拔出」本身是值得知道的事实。级别选择的判据见 01-spec 4.7 共有第 1 条。
    /// </summary>
    [LoggerMessage(EventId = 1007, Level = LogLevel.Warning,
        Message = "Closing port {PortName} failed ({ExceptionType}); the device was likely unplugged. Cleanup continues.")]
    public static partial void CloseFailed(
        ILogger logger, Exception exception, string portName, string exceptionType);

    // ⚠️ EventId 1006 was SettingsApplied, removed 2026-08-02 together with
    // ISerialPort.ApplySettingsAsync — its only caller, which itself never had one.
    // **The id is not reused**: event ids are coordinates in old log files, same rule as
    // the P0-*/P1-* numbering. See 00-STATUS.

    /// <summary>
    /// Driving DTR or RTS threw (T-07, spec 4.15).
    ///
    /// <para><b>Warning, and it IS logged</b> — unlike the matching read path in
    /// <c>SystemIoSerialPort.ReadControlLines</c>, which swallows the same exception types
    /// silently. The asymmetry is deliberate and is the whole reason this event exists: a set
    /// happens <b>once per user click</b>, while the read runs four times a second from a UI
    /// timer. Logging both would bury the click that failed under a thousand identical polls,
    /// and "I ticked the box and nothing happened" is exactly the question the log must be able
    /// to answer. 01-spec 4.7, shared rule 1: no silent catch.</para>
    /// </summary>
    [LoggerMessage(EventId = 1008, Level = LogLevel.Warning,
        Message = "Setting control line {Line} on {PortName} failed ({ExceptionType}); the requested level is remembered and will be applied on the next open.")]
    public static partial void ControlLineFailed(
        ILogger logger, Exception exception, string portName, string line, string exceptionType);

    /// <summary>
    /// Reading CTS / DSR / DCD started failing (T-07, spec 4.15).
    ///
    /// <para>⛔ <b>Debug, and raised on the EDGE only</b> — the caller polls four times a second,
    /// so a per-occurrence Warning would write the same line 240 times a minute for one unplugged
    /// cable and bury the entry that matters. <c>SystemIoSerialPort</c> holds a flag and calls
    /// this once per transition into failure, clearing it on the next success.</para>
    ///
    /// <para>⚠️ <b>The absence of this event does not mean the lines are readable</b> — it means
    /// no transition has been observed. The indicators themselves report <c>Unknown</c>, and that
    /// is the user-facing statement; this is only the diagnostic trail.</para>
    /// </summary>
    [LoggerMessage(EventId = 1009, Level = LogLevel.Debug,
        Message = "Reading control lines on {PortName} began failing ({ExceptionType}); the indicators now report Unknown. Logged once per transition, not per poll.")]
    public static partial void ControlLineReadFailed(
        ILogger logger, Exception exception, string portName, string exceptionType);

    // ---- 11xx 读路径 ----

    /// <summary>
    /// 每次 <c>ReadAsync</c> 返回都记一条。<b>这是 Q-1 的核心观测点。</b>
    ///
    /// <paramref name="gapMs"/> 是距上一次读返回的间隔。若这些间隔成簇出现在
    /// 某个固定值附近（如 FTDI 默认的 16ms），且单次字节数跨越多个协议帧，
    /// 即可判定驱动在攒帧 —— 那样空闲分帧（C-07）的实现方式必须改。
    ///
    /// <paramref name="timestamp"/> 用的是 <c>IMonotonicClock</c>，
    /// 与 <c>SerialFrame.Timestamp</c> 同源，因此日志行可与数据帧逐条对齐。
    /// </summary>
    [LoggerMessage(EventId = 1101, Level = LogLevel.Debug,
        Message = "Read {Count} bytes from {PortName}, gap={GapMs:F3} ms at {Timestamp:O}")]
    public static partial void ReadChunk(
        ILogger logger, string portName, int count, double gapMs, DateTimeOffset timestamp);

    /// <summary>
    /// 报文内容。<b>两道门都开才会走到这里</b>：<c>diserial.dev.json</c> 里
    /// <c>logLevel</c> 为 trace <b>且</b> <c>logPayload</c> 为 true ——
    /// 串口载荷是客户现场的总线数据。<b>开发形态不会自动打开这两道门。</b>
    /// </summary>
    [LoggerMessage(EventId = 1102, Level = LogLevel.Trace,
        Message = "Payload from {PortName}: {Hex}")]
    public static partial void ReadPayload(ILogger logger, string portName, string hex);

    [LoggerMessage(EventId = 1103, Level = LogLevel.Error,
        Message = "Read loop on {PortName} faulted after {Reads} reads / {Bytes} bytes: {ExceptionType}")]
    public static partial void ReadLoopFaulted(
        ILogger logger, Exception exception, string portName,
        long reads, long bytes, string exceptionType);

    [LoggerMessage(EventId = 1104, Level = LogLevel.Debug,
        Message = "Read loop on {PortName} exited normally after {Reads} reads / {Bytes} bytes")]
    public static partial void ReadLoopExited(
        ILogger logger, string portName, long reads, long bytes);

    /// <summary>
    /// 停止读循环时等待超时。<b>结果本身已由 <see cref="Closed"/> 的
    /// <c>readLoopTimedOut</c> 字段记在 Information 级</b> —— 本条只补 catch 处的异常原文。
    ///
    /// <b>Debug 而非 Warning</b>：每次关闭端口都可能走到，且真正要看的判据在 <see cref="Closed"/> 那条上。
    /// 级别判据见 01-spec 4.7 共有第 1 条。
    /// </summary>
    [LoggerMessage(EventId = 1105, Level = LogLevel.Debug,
        Message = "Read loop on {PortName} did not exit within the stop timeout; Close will break it")]
    public static partial void ReadLoopStopTimedOut(ILogger logger, Exception exception, string portName);

    /// <summary>
    /// 硬件线路错误（P1-52）。<c>FirstOfItsKind</c> 为 <c>true</c> 的那一条**同时也上了界面**。
    ///
    /// ⚠️ <b>Debug 而非 Warning，尽管它听起来很严重</b>：波特率配错时**每个字节**都会来一条，
    /// 记 Warning 会把日志淹掉、反而削弱它（01-spec 4.7 共有第 1 条：会重复触发的记 Debug）。
    /// ⭐ **要紧的那一条已经上了界面**，日志这边负责的是「到底来了多少条」——
    /// 排查时把 logLevel 调到 debug，数量本身就是判据（**偶发一条 vs 每字节一条**是两种毛病）。
    /// </summary>
    [LoggerMessage(EventId = 1106, Level = LogLevel.Debug,
        Message = "Serial line error on {PortName}: {ErrorType} (firstOfItsKind={FirstOfItsKind})")]
    public static partial void LineError(
        ILogger logger, string portName, string errorType, bool firstOfItsKind);

    // ---- 12xx 写路径 ----

    [LoggerMessage(EventId = 1201, Level = LogLevel.Debug,
        Message = "Wrote {Count} bytes to {PortName}")]
    public static partial void Wrote(ILogger logger, string portName, int count);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Error,
        Message = "Write of {Count} bytes to {PortName} failed: {ExceptionType}")]
    public static partial void WriteFailed(
        ILogger logger, Exception exception, string portName, int count, string exceptionType);

    /// <summary>
    /// 十六进制转储，带长度上限 —— 高波特率下整包转储会把日志淹掉。
    /// 截断时在末尾标出实际总长，避免看日志的人误以为帧就这么短。
    /// </summary>
    public static string ToHex(ReadOnlySpan<byte> data, int maxBytes = 64)
    {
        if (data.IsEmpty) return string.Empty;

        var shown = Math.Min(data.Length, maxBytes);
        var builder = new System.Text.StringBuilder(shown * 3 + 16);

        for (var i = 0; i < shown; i++)
        {
            if (i > 0) builder.Append(' ');
            builder.Append(data[i].ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }

        if (data.Length > shown)
        {
            builder.Append(System.Globalization.CultureInfo.InvariantCulture, $" ... ({data.Length} bytes total)");
        }

        return builder.ToString();
    }
}
