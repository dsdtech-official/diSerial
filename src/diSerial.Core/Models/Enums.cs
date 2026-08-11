namespace DiSerial.Core.Models;

/// <summary>
/// 会话类型。新增会话类型（V1.1 的 TCP/UDP、后续的 SSH）时在此扩展，
/// 并在 ISessionFactory 中注册对应的创建逻辑。
/// </summary>
public enum SessionKind
{
    /// <summary>单串口终端会话。</summary>
    Terminal,

    /// <summary>双串口监听会话（diDatatracker）。</summary>
    Monitor
}

/// <summary>监听会话中的通道标识。V2.0 扩展到 N 通道时在此追加。</summary>
public enum ChannelId
{
    /// <summary>终端会话或未分配通道。</summary>
    None = 0,
    A = 1,
    B = 2
}

/// <summary>数据帧方向。终端会话用 Tx/Rx，监听会话用 ChannelId 区分来源。</summary>
public enum FrameDirection
{
    /// <summary>从设备接收。</summary>
    Rx,

    /// <summary>发送到设备。</summary>
    Tx
}

/// <summary>数据显示格式（C-05）。</summary>
public enum DisplayFormat
{
    Ascii,
    Hex,
    HexAndAscii
}

/// <summary>
/// 时间戳显示模式（C-06）。
///
/// <para>⛔ <b>2026-08-06 删掉了第四个值 <c>Delta</c>（与上一帧的增量），用户定。</b>
/// 它与 <b>Δms 列</b>不是「相似」而是<b>同一个表达式</b> ——
/// <c>FrameFormatter.FormatTimestamp</c> 的 Delta 分支与
/// <c>FrameViewModel.DeltaText</c> 都是 <c>frame.Delta</c> 过
/// <c>DurationText.Milliseconds</c>，逐字符相同。两者同时打开时，
/// 屏幕上是两列一模一样的数字，<b>而导出的文件里也是两列一模一样的数据</b>。</para>
///
/// <para>⭐ <b>删的是它而不是 Δms 列，理由是它被严格支配</b>：选 <c>Delta</c> 当时间戳，
/// 代价是失去绝对/相对时间；而 Δms 是独立一列，<b>能与绝对时间并存</b>。
/// 于是「绝对 + Δms 列」给得出 <c>Delta</c> 模式的全部信息，还多给一样。
/// ⚠️ Δms 列本身在监听会话里是响应延迟的观测值（C-1），删不得。</para>
///
/// <para><c>SerialFrame.Delta</c> 这个模型属性<b>没有动</b> —— Δms 列正是读它。</para>
/// </summary>
public enum TimestampMode
{
    /// <summary>不显示时间戳。</summary>
    None,

    /// <summary>绝对时间 HH:mm:ss.fff。</summary>
    Absolute,

    /// <summary>相对会话起点。</summary>
    Relative
}

/// <summary>监听会话的视图模式。V1.0 仅实现 Merged，其余为 V1.2 预留。</summary>
public enum MonitorViewMode
{
    /// <summary>合并时间轴（V1.0 唯一实现）。</summary>
    Merged,

    /// <summary>上下分屏（V1.2）。</summary>
    Split,

    /// <summary>仅通道 A（V1.2）。</summary>
    ChannelAOnly,

    /// <summary>仅通道 B（V1.2）。</summary>
    ChannelBOnly
}

/// <summary>会话连接状态。</summary>
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Faulted
}

public enum SerialParity
{
    None,
    Odd,
    Even,
    Mark,
    Space
}

public enum SerialStopBits
{
    One,
    OnePointFive,
    Two
}

public enum SerialFlowControl
{
    None,
    RequestToSend,
    XOnXOff
}

/// <summary>
/// One of the two modem-control lines diSerial can <b>drive</b> (T-07, spec 4.15).
///
/// <para>⛔ <b>Only two, and that is a hardware fact, not a scope decision.</b> RS-232 gives the
/// DTE two outputs; everything else on the connector is an input. The readable inputs are in
/// <see cref="SerialControlLines"/>.</para>
/// </summary>
public enum SerialOutputLine
{
    /// <summary>Data Terminal Ready.</summary>
    Dtr,

    /// <summary>Request To Send.</summary>
    Rts
}

/// <summary>
/// The level of one readable input line — <b>three states, not two</b> (T-07, spec 4.15).
///
/// <para>⛔ <b><see cref="Unknown"/> is the whole reason this is not a <c>bool</c>.</b> A closed
/// port, a replay session, and a driver that refuses the read all produce "we do not know" — and
/// rendering that as <see cref="Low"/> would put a confident grey dot on screen for a line whose
/// level was never observed. This project ranks "the tool is lying" above a crash (00-STATUS,
/// advancement layer 1), so the third state is load-bearing rather than tidy.</para>
///
/// <para>⚠️ <b>The view may not distinguish these by colour alone</b> — spec 4.15 promise 5
/// requires the dot's shape and the state word to change together.</para>
/// </summary>
public enum ControlLineState
{
    /// <summary>Not observed. The port is not open, or the read failed.</summary>
    Unknown,

    /// <summary>Observed, and the line is not asserted.</summary>
    Low,

    /// <summary>Observed, and the line is asserted.</summary>
    High
}

/// <summary>
/// A single observation of the three readable input lines (T-07, spec 4.15).
///
/// <para>⭐ <b>All three in one value, taken in one pass.</b> Three separate property reads would
/// let the UI show a mix of two different moments — and the whole point of the panel is that what
/// is on screen is what is on the wire.</para>
///
/// <para>⛔ <b>RI is deliberately absent.</b> <c>System.IO.Ports.SerialPort</c> exposes
/// <c>CtsHolding</c>, <c>DsrHolding</c> and <c>CDHolding</c> but has <b>no ring-indicator level
/// property</b> — the only RI signal is the edge event <c>SerialPinChange.Ring</c>. There is no
/// level to report, so there is no field for one. User decision 2026-08-06: RI is not done at
/// all. See spec 4.15.</para>
/// </summary>
/// <param name="Cts">Clear To Send.</param>
/// <param name="Dsr">Data Set Ready.</param>
/// <param name="Dcd">Data Carrier Detect.</param>
public readonly record struct SerialControlLines(
    ControlLineState Cts,
    ControlLineState Dsr,
    ControlLineState Dcd)
{
    /// <summary>Nothing observed — what a closed port reports.</summary>
    public static SerialControlLines Unknown => new(
        ControlLineState.Unknown, ControlLineState.Unknown, ControlLineState.Unknown);
}

/// <summary>
/// 当前平台的串口支持状态。
///
/// 刻意用枚举而非字符串：Core / Infrastructure 层不得产出用户可见的本地化文本，
/// 否则领域层会依赖 UI 语言状态，并破坏「Core 可脱离 UI 单元测试」这条纪律。
/// 由 App 层负责把状态码映射为当前语言的提示文本。
/// </summary>
public enum PlatformSupportStatus
{
    /// <summary>当前平台已支持。</summary>
    Supported,

    /// <summary>macOS 尚未实现（System.IO.Ports 不支持，需 P/Invoke termios，排期 V1.1）。</summary>
    MacOsNotImplemented,

    /// <summary>未知平台。</summary>
    UnknownPlatform
}

/// <summary>
/// 可恢复的串口错误分类（01-spec 4.7「可恢复异常」的五条路径）。
///
/// 与 <see cref="PlatformSupportStatus"/> 同一取向：<b>下层只回答「是哪一类错」，
/// 不产出用户可见文本</b>。框架异常的 <c>Message</c> 是英文，直接显示会让中文界面
/// 半英半中，且违反「界面上的字都能切语言」这条规则（03-conventions 2.1 / 2.3）。
/// 由 App 层的 SessionErrorPresenter 映射为当前语言的文本。
///
/// ⚠️ <b>原始异常消息仍要进日志</b> —— 那是排查现场问题的唯一线索，
/// 只是不进界面。两者用途不同，不可互相替代。
/// </summary>
public enum SerialErrorKind
{
    /// <summary>无法归类。界面上退回一句通用说明，细节靠日志。</summary>
    Unknown,

    /// <summary>端口名不存在或无法解析（拔掉后仍按旧名连接、合成端口名等）。</summary>
    PortNotFound,

    /// <summary>端口被其他程序占用，或当前用户无权访问该设备节点。</summary>
    AccessDenied,

    /// <summary>连接过程中设备被拔出，或句柄已失效。</summary>
    DeviceRemoved,

    /// <summary>读写超时。</summary>
    Timeout,

    /// <summary>端口尚未连接就发起了发送。</summary>
    NotConnected,

    /// <summary>发送区输入无法解析为字节（HEX 含非法字符、长度为奇数等）。</summary>
    InvalidInput,

    /// <summary>
    /// The timed-send interval is below the allowed minimum (T-06, 01-spec 4.14 promise 5).
    ///
    /// <para>Raised by the App layer, never by a driver -- same as
    /// <see cref="InvalidInput"/> and <see cref="NotConnected"/>. It gets its own value rather
    /// than reusing <see cref="InvalidInput"/> because that one's wording is about parsing HEX
    /// digits; showing it here would state a wrong reason, and a diagnostic tool stating a wrong
    /// reason is the failure mode this project ranks above crashing.</para>
    /// </summary>
    IntervalTooSmall,

    /// <summary>
    /// A timed send was started with an empty input box (P2-50 ②, 01-spec 4.14).
    ///
    /// <para>Raised by the App layer, never by a driver -- same as <see cref="InvalidInput"/>,
    /// <see cref="NotConnected"/> and <see cref="IntervalTooSmall"/>. It gets its own value for
    /// the same reason <see cref="IntervalTooSmall"/> does: <see cref="InvalidInput"/>'s wording
    /// is about parsing HEX digits, and there is nothing to parse here.</para>
    ///
    /// <para>⭐ <b>Why this path became worth a banner.</b> "Send once, like it, then start
    /// repeating" clears the box on the successful send, so reaching "start timed send" with an
    /// empty box is now the <b>end of a normal workflow</b> rather than a corner case -- a direct
    /// consequence of answering P2-50 ① with "keep the clearing".</para>
    /// </summary>
    NothingToSend,

    /// <summary>
    /// Writing a frame to the recording database failed, so recording stopped (P2-53,
    /// 01-spec 4.7 path 6).
    ///
    /// <para>⛔ <b>Path 6 was the only one of the six without its own value</b>, so it fell back
    /// to <see cref="Unknown"/> and the banner said "see the log" -- while the spec requires each
    /// path to state <i>why</i>. The underlying text (e.g. <c>attempt to write a readonly
    /// database</c>) only ever reached the log file, and the user form has no way to open
    /// that.</para>
    ///
    /// <para>⚠️ <b>It does not distinguish disk-full from read-only from corrupt.</b> Telling
    /// those apart means classifying SQLite result codes, which is a much larger job and is not
    /// decided yet (P2-53). One honest cause beats a wrong specific one.</para>
    /// </summary>
    RecordingFailed,

    /// <summary>
    /// A hardware line error reported by the driver — parity, framing, or an overrun
    /// (P1-52, 01-spec 4.7).
    ///
    /// <para>⛔ <b>Not fatal.</b> The port is still open and the session keeps running; what is
    /// in doubt is the <i>data</i>. Reusing <see cref="DeviceRemoved"/> or
    /// <see cref="Unknown"/> would either claim the device is gone or say nothing at all, and
    /// the whole point is that <b>this one has an action attached</b>: check the baud rate and
    /// parity.</para>
    ///
    /// <para>⚠️ <b>It does not say which bytes were affected.</b> <c>SerialPort.ErrorReceived</c>
    /// carries no position, so the report is about the line, not about a frame. Marking a
    /// particular frame would mean inventing a correlation the driver never gave us.</para>
    ///
    /// <para>⭐ <b>Raised at most once per kind per connection.</b> A wrong baud rate produces
    /// one framing error <i>per byte</i>; a banner that repaints several times a second never
    /// gets read.</para>
    /// </summary>
    LineError
}

/// <summary>帧级别的异常标记，供显示层着色。</summary>
[Flags]
public enum FrameFlags
{
    None = 0,
    ParityError = 1 << 0,
    FramingError = 1 << 1,
    BufferOverrun = 1 << 2,

    /// <summary>由解码器标记的协议异常（V1.3）。</summary>
    ProtocolError = 1 << 3
}
