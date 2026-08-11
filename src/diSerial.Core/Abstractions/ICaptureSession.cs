using DiSerial.Core.Models;

namespace DiSerial.Core.Abstractions;

/// <summary>
/// 一次数据采集会话 —— ViewModel 与串口之间的隔离层。
///
/// 职责划分：
///   ICaptureSession — 打开端口、读取、打时间戳、分帧、（监听会话）合并双通道
///   SessionViewModel — 只负责展示状态与用户交互，不碰任何 I/O
///
/// 这样划分使得：采集逻辑可脱离 UI 单元测试；UI 可脱离硬件开发（用桩实现）。
/// </summary>
public interface ICaptureSession : IAsyncDisposable
{
    SessionKind Kind { get; }

    ConnectionState State { get; }

    /// <summary>会话开始时刻，用于计算相对时间戳。</summary>
    DateTimeOffset? StartedAt { get; }

    /// <summary>
    /// 产生一个已完成的帧。
    ///
    /// <para><b>实现方保证的是<u>序号的分配顺序</u>，不是<u>事件的到达顺序</u></b>
    /// （P1-50，2026-08-04 改准）：<see cref="SerialFrame.Sequence"/> 在锁内单调递增，
    /// 而事件在锁外发布 —— 于是**相邻两次回调的 `Sequence` 可能是倒的**。</para>
    ///
    /// <para>⛔ <b>原文写的是「实现方负责保证按时间顺序推送」，那句话不成立</b>：
    /// 监听会话有**四个并发生产者**（读 A / 读 B / flush 定时器 / UI 注入），
    /// 各自「锁内组帧 → 锁外发布」，线程 A 组好 seq=N 后被抢占，
    /// 线程 B 的 seq=N+1 完全可以先到订阅方。</para>
    ///
    /// <para>⚠️ <b>为什么不改成真的有序，而是改这句话</b>：有序发布要么把发布收进锁
    /// （**订阅方的耗时就进了临界区** —— 那正是当初刻意避开的），要么加一条单一派发队列
    /// （多一层缓冲与一个线程）。⭐ **代价落在采集热路径上，而收益只是显示缓冲里
    /// 极小概率的相邻两行顺序** —— 不值得。**所以这里声明它，而不是消灭它。**</para>
    ///
    /// <para><b>订阅方要知道的</b>：需要顺序时**按 <see cref="SerialFrame.Sequence"/> 排**，
    /// 不要依赖回调顺序。⭐ **批次导出本来就不受影响**
    /// （<c>SqliteRecordingReader</c> 读回时 <c>ORDER BY seq</c>）；
    /// 受影响的只有显示缓冲的行序，以及「导出显示缓冲」那条路径。</para>
    ///
    /// <para>⚠️ <b>与 01-spec 4.9.2「不做跨通道重排、Δ 可为负」不是同一条</b>：
    /// 那条说的是**时间戳**可以倒序（刻意、有规格）；本条说的是**发布顺序与 `Sequence`**
    /// 可以倒序。两者都已声明，但成因和影响面不同。</para>
    /// </summary>
    event EventHandler<FrameCapturedEventArgs>? FrameCaptured;

    event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// 驱动报告的**非致命**线路错误：校验错 / 帧错 / 溢出（P1-52）。
    ///
    /// <para>⛔ <b>为什么它不能并进 <see cref="StateChanged"/></b>：状态没有变。
    /// 端口还开着、会话还在跑，**存疑的是数据不是连接** ——
    /// 把它报成一次状态变更，等于说了一件没发生的事。</para>
    ///
    /// <para>⚠️ <b>它不指向任何一帧</b>：底层的 <c>ErrorReceived</c> 只说「发生了线路错误」，
    /// <b>不说是哪几个字节</b>。要给某一帧标红，就得凭空发明一个驱动从没给过的对应关系 ——
    /// <b>而一个诊断工具指错字节，比它说「这条线上有校验错」更糟。</b>
    /// 所以这是**会话级提示**，帧级标记仍未实现（00-STATUS P1-52）。</para>
    ///
    /// <para>⭐ <b>实现方负责节流</b>：波特率配错会**每个字节**来一次，
    /// 而顶部提示条同时只存在一条（01-spec 4.7）—— 不节流的话用户永远读不完一条完整的提示。</para>
    /// </summary>
    event EventHandler<SerialErrorEventArgs>? LineErrorDetected;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 向指定通道写入数据。
    /// 监听会话中，调用方必须已获得用户对总线注入的显式确认（M-09）。
    /// </summary>
    Task SendAsync(ChannelId channel, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
}

/// <summary>
/// A capture session that has <b>one</b> serial port, and can therefore report and drive its
/// modem-control lines (T-07, spec 4.15).
///
/// <para>⭐ <b>Why this is a separate interface rather than two more members on
/// <see cref="ICaptureSession"/>.</b> The first attempt widened <c>ICaptureSession</c>, and the
/// compiler answered with eleven test doubles that suddenly had to implement lines they have
/// nothing to do with. ⚠️ <b>That churn was the signal, not the obstacle</b>: control lines are
/// not part of "capture a session", they are part of "be a single-port session".</para>
///
/// <para>⛔ <b>What the split buys, and it is not tidiness.</b> Spec 4.15 promise 2 says the
/// panel does not exist in monitor sessions — because a monitor session holds two ports wired
/// to opposite sides of somebody else's bus, so an unqualified "CTS is high" has no referent,
/// and driving DTR/RTS there would assert onto a live third-party bus with none of M-09's
/// confirmation. <b><c>MonitorCaptureSession</c> simply does not implement this interface</b>,
/// so that promise is now a type fact rather than a rule the view has to keep remembering.</para>
///
/// <para>The caller detects the capability (<c>capture as IControlLineSession</c>) and shows the
/// panel only when it is there.</para>
/// </summary>
public interface IControlLineSession
{
    /// <summary>
    /// Reads the three input control lines (T-07, spec 4.15).
    ///
    /// <para><b>Never throws</b>, and returns <see cref="SerialControlLines.Unknown"/> whenever
    /// the level was not observed — the session is not running, the source is a replay script,
    /// or the driver refused the read.</para>
    ///
    /// <para>⛔ <b>The caller polls this.</b> Reasoning and the measurement behind it are on
    /// <see cref="ISerialPort.ReadControlLines"/>; in short, a driver was observed changing
    /// DCD's level six times while raising zero <c>CDChanged</c> events.</para>
    /// </summary>
    SerialControlLines ReadControlLines();

    /// <summary>
    /// True when hardware flow control owns RTS, so the user may not drive it (spec 4.15,
    /// promise 7).
    ///
    /// <para>⭐ <b>It is here rather than derived by the caller from the port settings</b>: the
    /// question is "may I drive this line", and the session is what knows. Handing the caller a
    /// settings object to draw its own conclusion would put the same rule in two places, and
    /// only one of them next to the code that actually honours it
    /// (<c>SystemIoSerialPort.ApplyOutputLines</c>).</para>
    /// </summary>
    bool IsRtsOwnedByFlowControl { get; }

    /// <summary>
    /// Drives one of the two output control lines (T-07, spec 4.15).
    ///
    /// <para>See <see cref="ISerialPort.SetOutputLineAsync"/> for the level semantics, for what
    /// the defaults reverse, and — ⛔ <b>if you are about to discard this task</b> — for which
    /// exceptions it does not swallow (P2-86).</para>
    /// </summary>
    Task SetOutputLineAsync(
        SerialOutputLine line, bool asserted, CancellationToken cancellationToken = default);
}

/// <summary>
/// 会话工厂 —— 主要扩展点之一。
/// 新增会话类型（V1.1 的 TCP/UDP、后续 SSH）时实现新的 ICaptureSession，
/// 并在此工厂注册，UI 层与 DI 配置之外的代码无需改动。
/// </summary>
public interface ICaptureSessionFactory
{
    ICaptureSession CreateTerminal(string portName, SerialPortSettings settings);

    ICaptureSession CreateMonitor(SerialChannelPair pair, SerialPortSettings settings);
}

public sealed class FrameCapturedEventArgs(SerialFrame frame) : EventArgs
{
    public SerialFrame Frame { get; } = frame;
}

public sealed class ConnectionStateChangedEventArgs(
    ConnectionState state,
    string? message = null,
    SerialErrorKind errorKind = SerialErrorKind.Unknown) : EventArgs
{
    public ConnectionState State { get; } = state;

    /// <summary>面向开发者的诊断信息，不参与本地化。进日志，不进界面。</summary>
    public string? Message { get; } = message;

    /// <summary>
    /// 转入 <see cref="ConnectionState.Faulted"/> 的原因分类，供界面呈现（01-spec 4.7 第 3 条路径）。
    ///
    /// ⚠️ <b>此前这里只有 <see cref="Message"/>，而 App 层
    /// 只取了 <see cref="State"/>，原因被整条丢掉</b>（原 P0-2 的第 3 条路径）。
    /// </summary>
    public SerialErrorKind ErrorKind { get; } = errorKind;
}
