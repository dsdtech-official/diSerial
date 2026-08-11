using DiSerial.Core.Models;

namespace DiSerial.Core.Abstractions;

/// <summary>
/// 串口读写契约。
///
/// 上层（ViewModel、会话逻辑）一律依赖本接口，禁止直接引用 System.IO.Ports。
/// 原因：System.IO.Ports 官方仅支持 Windows 与 Linux，在 macOS 上抛
/// PlatformNotSupportedException。V1.1 增加 macOS 支持时，只需新增一个
/// 基于 P/Invoke termios 的实现，上层代码不受影响。
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

    Task OpenAsync(CancellationToken cancellationToken = default);

    Task CloseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 写入数据。
    /// 注意：监听会话中调用本方法会把数据真实注入运行中的总线，
    /// 上层必须先经过用户显式确认（M-09）。
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
/// 打开端口失败，<b>并携带是哪一路失败的</b>。
///
/// <b>为什么需要它</b>：监听会话要同时打开两个端口，而失败提示必须说清
/// 「通道 B（COM7）打不开」—— 光有底层异常说不出通道。
/// 提示出现在<b>新建会话对话框里</b>（对话框保持打开，用户改个端口直接重试），
/// 所以这条信息要从 Infrastructure 一路传到 App，中间不能丢。
/// 规格见 docs/01-spec.md 4.7。
///
/// 终端会话只有一个端口，<see cref="Channel"/> 为 <see cref="ChannelId.None"/>。
///
/// ⚠️ <b>放在本文件而不是 Models/</b>：它是「打开端口」这个契约的一部分 ——
/// 实现方 <b>必须</b> 用它包装底层异常，否则上层无从得知失败的是哪一路。
/// <see cref="Exception.InnerException"/> 保住原始异常，
/// 分类由 App 层的 SerialErrorClassifier 从 inner 得出。
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
