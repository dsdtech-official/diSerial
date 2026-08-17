using DiSerial.Core.Models;

namespace DiSerial.Core.Abstractions;

/// <summary>
/// The serial read/write contract.
///
/// Everything above this line (ViewModels, session logic) depends on this interface and
/// must never reference System.IO.Ports directly, so that swapping the implementation
/// stays a one-file change.
///
/// <para>⚠️ <b>The reason originally given for this abstraction was wrong.</b> It read:
/// "System.IO.Ports officially supports Windows and Linux only, and throws
/// PlatformNotSupportedException on macOS, so V1.1 will add a P/Invoke termios
/// implementation." <b>Measured 2026-08-13 on a MacBook Air M4: it does not throw, and a
/// real port opens and round-trips.</b> macOS ships on <c>SystemIoSerialPort</c> like the
/// other platforms; see docs/04-platforms.md 2.1a.</para>
///
/// <para>⭐ <b>The abstraction is still worth having</b> -- just for a different reason
/// than the one on file. Non-standard baud rates need ioctl(IOSSIOSPEED), which
/// System.IO.Ports cannot reach, so a termios implementation remains the V1.1 answer for
/// that. The exception contract below is what such an implementation must satisfy.</para>
///
/// <para>⭐⭐ <b>EXCEPTION CONTRACT (P1-53, written 2026-08-12) — read this before writing a second
/// implementation.</b> The rules are stated per member below; this paragraph is why they exist.
/// <c>SerialErrorClassifier</c> turns whatever comes out of here into the sentence the user reads,
/// and its mapping was calibrated against System.IO.Ports — the class says so itself. A termios
/// implementation throws whatever a wrapper around errno throws, so <b>an implementation that
/// ignores these rules does not fail loudly: it classifies every failure as
/// <c>SerialErrorKind.Unknown</c></b>, and the six failure paths P0-2 was spent on start returning
/// one generic sentence forever. Nothing breaks the build and no test goes red.</para>
///
/// <para>⛔ <b>The requirement is the classification, not the exception type.</b> Do not copy
/// Windows' types: measured 2026-08-12, an unresolvable name gives <see cref="ArgumentException"/>
/// while a well-formed but absent port gives <see cref="FileNotFoundException"/> — those are Win32
/// error codes in BCL clothing, and reproducing them on macOS would be imitation, not compliance.
/// <b>What every implementation owes is that each failure mode below reaches a classification
/// other than Unknown</b>, and that the lifetime rules (disposed vs. not-open, close never
/// refusing) hold exactly.</para>
///
/// <para>✅ <b>Enforced by <c>SerialPortContractTests</c></b> (Infrastructure.Tests): derive from
/// it, return the new port from <c>CreatePort</c>, and the whole contract runs. ⚠️ The one claim
/// that needs real hardware — a port already held open must classify as
/// <c>AccessDenied</c>, measured 2026-08-12 on COM11 as
/// <see cref="UnauthorizedAccessException"/> — is recorded in 00-STATUS P1-53 instead, because
/// this project's test suite deliberately opens no ports.</para>
/// </summary>
public interface ISerialPort : IAsyncDisposable
{
    string PortName { get; }

    /// <summary>
    /// The parameters this port was created with. <b>Fixed for its lifetime</b> — changing
    /// them means closing the port and opening a new one.
    ///
    /// ⚠️ <c>ApplySettingsAsync</c> ("apply new parameters to an already-open port") sat next
    /// to this until 2026-08-02, when it was removed: <b>it had zero callers from the very
    /// first commit</b>, no document ever recorded why the capability was wanted, and the
    /// implementation's claim that System.IO.Ports tolerates the change in place had therefore
    /// never executed once. Keeping it would have obliged the future termios implementation to
    /// write it too, in the one area 04-platforms warns fails silently — real cost, for a
    /// requirement no specification has ever made. See 00-STATUS.
    ///
    /// If "change parameters while connected" is ever actually wanted, it is a new feature and
    /// goes through 01-spec first — not a restoration of this.
    /// </summary>
    SerialPortSettings Settings { get; }

    bool IsOpen { get; }

    /// <summary>收到数据时触发。实现方须在读线程内、Read 返回后立即打点时间戳。</summary>
    event EventHandler<SerialChunkReceivedEventArgs>? DataReceived;

    /// <summary>串口错误（校验错、帧错、溢出）。</summary>
    event EventHandler<SerialErrorEventArgs>? ErrorReceived;

    /// <summary>
    /// Opens the port.
    ///
    /// <para><b>Throws on failure, and the exception must classify.</b> Three failure modes have
    /// to reach a specific <c>SerialErrorKind</c> rather than <c>Unknown</c> — they are the ones
    /// the user meets: <b>the port is not there</b> (unplugged since the list was built, or a name
    /// that does not resolve), <b>the port is held by someone else or the device node is not
    /// permitted</b>, and <b>the parameters were refused</b>.</para>
    ///
    /// <para>⛔ <b>Do not wrap the exception here.</b> <see cref="SerialPortOpenException"/> is
    /// added by the <i>session</i>, which is the layer that knows which channel this port was —
    /// see the type's own remarks. An implementation that wraps early makes the classifier read
    /// through two layers and gains nothing.</para>
    ///
    /// <para>⚠️ <b>A failed open must leave the port closed and disposable.</b> The monitor
    /// session opens two ports and rolls the first one back when the second fails, so cleaning up
    /// after a failure is an ordinary path, not an edge case.</para>
    ///
    /// <para>Opening an already-open port is a no-op, not an error.</para>
    /// </summary>
    Task OpenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the port.
    ///
    /// <para>⛔⭐ <b>Must not throw. Closing is a request for a state, not an operation that can be
    /// refused</b> — on a port that was never opened, on one already closed, and after disposal it
    /// is a no-op. The shutdown path reaches it more than once (P2-30's duplicate-release chains)
    /// and the monitor rollback reaches it on a port that may never have opened; a close that can
    /// fail turns both into error handling for a situation that is not an error.</para>
    ///
    /// <para>⚠️ <b>This is a requirement, and the Windows implementation does not fully meet it
    /// today</b> — 00-STATUS <b>P2-106</b> records a measured race (1 in 5 on a cold thread pool)
    /// where a fault inside the read loop escapes through here. <b>It is written as the
    /// requirement on purpose</b>: a contract that describes current behaviour instead of
    /// promising future behaviour would make the defect permanent by definition.</para>
    ///
    /// <para>A device that has physically gone away is the normal case for this method, not a
    /// failure of it: log what the driver said and finish the teardown.</para>
    /// </summary>
    Task CloseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 写入数据。
    /// 注意：监听会话中调用本方法会把数据真实注入运行中的总线，
    /// 上层必须先经过用户显式确认（M-09）。
    ///
    /// <para>⭐ <b>"Disposed" and "not open" must stay distinguishable</b>, because the fix the
    /// user needs is different: <see cref="ObjectDisposedException"/> after
    /// <see cref="IAsyncDisposable.DisposeAsync"/>, and <see cref="InvalidOperationException"/>
    /// when the port was simply never opened. ⚠️ The first derives from the second, so an
    /// implementation that reports only the base type is <i>technically</i> right and actively
    /// misleading — it points the reader at the connection state when the object itself is
    /// gone.</para>
    ///
    /// <para>⛔ <b>A write failure must reach the caller.</b> This is the one path where swallowing
    /// costs bytes: the caller believes it sent something the bus never saw.</para>
    /// </summary>
    Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the three input control lines in one pass (T-07, spec 4.15).
    ///
    /// <para><b>Never throws.</b> A closed port, a disposed handle or a driver that refuses the
    /// read all return <see cref="SerialControlLines.Unknown"/> — the caller polls this every
    /// 250 ms from the UI, and an exception on that path would be a crash on a cable being
    /// unplugged.</para>
    ///
    /// <para>⛔ <b>Callers must poll it; they may not wait for an event instead.</b> Measured
    /// 2026-08-06 on the HHD virtual driver: DCD's level toggled six times and
    /// <c>PinChanged</c> raised <c>CDChanged</c> <b>zero</b> times (only <c>DsrChanged</c>, six
    /// times, control experiment included so "no event" was not merely "no subscription"). An
    /// event-driven indicator therefore sits on a stale value indefinitely, which is the exact
    /// failure this project ranks worse than a crash. Probe: <c>tools\signals.ps1</c>.</para>
    ///
    /// <para>⚠️ <b>macOS (V1.1) has to implement this too.</b> Keep it to one call returning one
    /// value: termios exposes the same lines through <c>TIOCMGET</c>, so the shape carries over,
    /// but 04-platforms warns that this is the area where a mistake fails <i>silently</i>.</para>
    /// </summary>
    SerialControlLines ReadControlLines();

    /// <summary>
    /// Drives one of the two output control lines (T-07, spec 4.15).
    ///
    /// <para><b>Both default to not asserted</b> (user decision 2026-08-06) — see
    /// <c>SystemIoSerialPort.Apply</c> for what that reverses and what it costs.</para>
    ///
    /// <para>⚠️ <b>Asynchronous because it must serialise against the port's other mutators</b>,
    /// not because the underlying set is slow. P2-28 was exactly this: a writer that did not take
    /// the same gate as open/close.</para>
    ///
    /// <para>Setting a line on a port that is not open is a no-op, not an error: the requested
    /// level is remembered and applied when the port next opens, so the checkbox the user ticked
    /// before connecting still means what it said.</para>
    ///
    /// <para>⛔ <b>What it does and does not swallow</b> (P2-86, 2026-08-08). A driver-level
    /// failure while applying the level is caught and logged, because "I ticked the box and
    /// nothing happened" is a question the log has to be able to answer (01-spec 4.7). ⚠️ <b>The
    /// two argument/lifecycle failures are not caught and do reach the caller</b>:
    /// <see cref="ObjectDisposedException"/> after the port has been disposed, and
    /// <see cref="ArgumentOutOfRangeException"/> for a <paramref name="line"/> outside the
    /// enum.</para>
    ///
    /// <para>⭐ <b>Written down because a caller got it wrong.</b> The signal panel discards this
    /// task (<c>_ = SetOutputLineAsync(...)</c>) on the strength of a comment claiming the method
    /// swallows everything. It does not, and a throw on a discarded task is dropped silently.
    /// Anyone fire-and-forgetting this call owns the two exceptions above.</para>
    /// </summary>
    Task SetOutputLineAsync(
        SerialOutputLine line, bool asserted, CancellationToken cancellationToken = default);
}

/// <summary>
/// One chunk as the driver handed it over, with the moment it arrived.
///
/// <para>⚠️ <b>Renamed from <c>SerialDataReceivedEventArgs</c> on 2026-08-05 (P2-37).</b> That
/// name collided exactly with <c>System.IO.Ports.SerialDataReceivedEventArgs</c>, so the one
/// file that has to see both — <c>SystemIoSerialPort</c> — carried a
/// <c>using CoreDataReceivedEventArgs = …</c> alias and a comment explaining it. ⛔ <b>An
/// abstraction that borrows its reference implementation's type names is a signal</b>: the
/// alias existed because this layer had copied a name from the very library it exists to
/// hide. The alias is now gone.</para>
///
/// <para>⭐ <b>"Chunk" is not a new word</b>: it is what the rest of the project already calls
/// this — the <c>ReadChunk</c> log event, <c>ReplayChunk</c>, and the C-07 invariant "frame
/// boundaries are a subset of chunk boundaries". <b>A chunk is not a frame</b>, and that
/// distinction is load-bearing enough that the type should say which one it carries.</para>
///
/// <para>The event that raises this is still called <c>DataReceived</c>: an event name
/// cannot collide with a type name, so it never had the problem, and it is the conventional
/// .NET spelling.</para>
/// </summary>
public sealed class SerialChunkReceivedEventArgs(
    ReadOnlyMemory<byte> data, DateTimeOffset timestamp) : EventArgs
{
    /// <summary>The raw bytes of this read.</summary>
    public ReadOnlyMemory<byte> Data { get; } = data;

    /// <summary>Stamped by the read thread, immediately after Read() returned.</summary>
    public DateTimeOffset Timestamp { get; } = timestamp;
}

/// <summary>
/// An open failed, <b>and this says which of the two ports it was</b>.
///
/// <para><b>Why it exists.</b> A monitor session opens two ports at once, and the message has to
/// be able to say "channel B (COM7) could not be opened" — the underlying exception cannot name a
/// channel. The message appears <b>in the new-session dialog</b>, which stays open so the user can
/// change a port and retry, so this information has to survive the whole way from Infrastructure
/// to App. Spec: docs/01-spec.md 4.7. A terminal session has one port and leaves
/// <see cref="Channel"/> at <see cref="ChannelId.None"/>.</para>
///
/// <para>⛔⭐ <b>Who wraps: the session, not the port</b> (corrected 2026-08-12, P1-53). This
/// comment used to say implementations "must" wrap their own failures in this type. ⚠️ <b>No
/// implementation does, and none should</b>: a port object does not know which channel it was
/// handed to. <c>MonitorCaptureSession.OpenOrThrowAsync</c> is the only place that throws this,
/// which is right — <b>it is the layer that knows the channel</b>, and it is also the layer that
/// has to roll the other port back first. <c>TerminalCaptureSession</c> deliberately rethrows the
/// original exception unwrapped: with one port there is no channel to add.</para>
///
/// <para>⭐ <b>The sentence was wrong in a way that mattered.</b> Read literally it obliged the
/// future termios implementation to wrap — adding a layer the classifier would then have to read
/// through, in the one area 04-platforms warns fails silently.</para>
///
/// <para><see cref="Exception.InnerException"/> keeps the original; the App layer's
/// <c>SerialErrorClassifier</c> classifies from that inner exception.</para>
/// </summary>
public sealed class SerialPortOpenException(
    ChannelId channel, string portName, Exception innerException)
    : Exception($"Failed to open {portName} (channel {channel}).", innerException)
{
    public ChannelId Channel { get; } = channel;

    public string PortName { get; } = portName;
}

public sealed class SerialErrorEventArgs(
    FrameFlags error,
    string message,
    bool isFatal = false,
    SerialErrorKind kind = SerialErrorKind.Unknown)
    : EventArgs
{
    public FrameFlags Error { get; } = error;

    /// <summary>面向开发者的诊断信息，不参与本地化。</summary>
    public string Message { get; } = message;

    /// <summary>
    /// 面向用户的错误分类。
    ///
    /// ⚠️ <b>与 <see cref="Message"/> 分工不同，不可互相替代</b>：
    /// <see cref="Message"/> 进日志（英文、含具体细节，是排查的唯一线索），
    /// 本属性进界面（由 App 层映射成当前语言）。见 01-spec 4.7。
    /// </summary>
    public SerialErrorKind Kind { get; } = kind;

    /// <summary>
    /// 端口已不可用（设备拔出、句柄失效等），读循环已终止。
    /// 上层据此把会话置为 Faulted，而不是继续等待永远不会到来的数据。
    /// </summary>
    public bool IsFatal { get; } = isFatal;
}
