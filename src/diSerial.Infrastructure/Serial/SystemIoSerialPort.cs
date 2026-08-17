using System.Diagnostics;
using System.IO.Ports;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using DiSerial.Infrastructure.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using CoreParity = DiSerial.Core.Models.SerialParity;
using CoreStopBits = DiSerial.Core.Models.SerialStopBits;
using CoreFlowControl = DiSerial.Core.Models.SerialFlowControl;
// A fourth alias, SerialChunkReceivedEventArgs, used to sit here: Core's chunk event args were
// named SerialDataReceivedEventArgs, colliding exactly with the System.IO.Ports type of that
// name imported above. Core's type is now SerialChunkReceivedEventArgs (P2-37), so the
// collision -- and the alias -- are gone. The three remaining aliases are a different case:
// Parity / StopBits / FlowControl genuinely exist on both sides as distinct enums.

namespace DiSerial.Infrastructure.Serial;

/// <summary>
/// The <c>System.IO.Ports</c> based serial implementation. <b>diSerial ships this on
/// Windows and macOS</b> -- the BCL type also runs on Linux, but that is not a platform we
/// release or verify.
///
/// <para>⭐ <b>macOS was measured on 2026-08-13 and works unmodified</b>: GetPortNames
/// returns both /dev/cu.* and /dev/tty.*, a cu.* port opens, and an FTDI loopback
/// round-trips byte for byte at 9600 and 115200 with the baud rate confirmed by timing.
/// This class needed no macOS-specific code at all. The claim it previously carried --
/// "System.IO.Ports throws PlatformNotSupportedException there" -- was never measured and
/// is false. See docs/04-platforms.md 2.1a.</para>
///
/// <para>⛔ <b>One caller-visible difference, not yet fixed.</b> On Windows an absent port
/// throws ArgumentException or FileNotFoundException depending on the flavour; on macOS
/// <b>all shapes collapse to UnauthorizedAccessException</b>, which SerialErrorClassifier
/// maps to AccessDenied. The user is therefore told "access denied" for a port that simply
/// is not there. Tracked as 00-STATUS P2-108.</para>
///
/// <b>线程模型</b>：不使用 <c>SerialPort.DataReceived</c> 事件，
/// 而是自建读循环调用 <c>BaseStream.ReadAsync</c>。原因有二：
/// 一是 DataReceived 在各平台上的触发行为不一致且不可靠；
/// 二是时间戳必须在 Read 返回的第一时间打点，自建循环才能保证这一点 ——
/// 事件模型下数据已在框架内部排队，误差不可控。
/// </summary>
public sealed class SystemIoSerialPort : ISerialPort
{
    private const int ReadBufferSize = 4096;

    private readonly IMonotonicClock _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger _logger;
    private readonly bool _includePayload;

    private SerialPort? _port;
    private CancellationTokenSource? _readCts;
    private Task? _readLoop;

    /// <summary>
    /// What the user has asked the two output lines to be (T-07, spec 4.15).
    ///
    /// <para><b>Both start not asserted</b> — user decision 2026-08-06. See <see cref="Apply"/>
    /// for what that reverses.</para>
    ///
    /// <para>⭐ <b>They live here, not on the <see cref="SerialPort"/>, because they outlive
    /// it.</b> The port object is created in <see cref="OpenAsync"/> and thrown away in
    /// <see cref="CloseAsync"/>; a checkbox ticked while disconnected — or before the first
    /// connect — has to survive that, or the panel would silently forget what the user set.
    /// <see cref="Apply"/> is the single place that pushes them onto a real port.</para>
    ///
    /// <para>⚠️ <c>volatile</c>: <see cref="ReadControlLines"/> is polled from the UI thread
    /// while <see cref="SetOutputLineAsync"/> writes under <see cref="_gate"/>.</para>
    /// </summary>
    private volatile bool _dtrAsserted;

    private volatile bool _rtsAsserted;

    /// <summary>
    /// 0 until <see cref="DisposeAsync"/> has been entered (P2-28).
    ///
    /// <para><b>An <c>int</c> driven by <see cref="Interlocked"/> rather than a <c>bool</c></b>:
    /// two callers racing on dispose would both read <c>false</c> and both proceed, which is the
    /// very thing the guard exists to stop. It cannot live under <see cref="_gate"/> either —
    /// the gate is a thing dispose has to reason about, not a thing it can rely on.</para>
    /// </summary>
    private int _disposed;

    /// <param name="logger">
    /// 可选 —— 未提供时退化为 <see cref="NullLogger"/>。
    /// 留默认值是为了让现有单元测试与不关心诊断的调用方无需改动。
    /// </param>
    /// <param name="includePayload">
    /// 是否把报文内容以十六进制记入日志。由 <see cref="LoggingOptions.IncludePayload"/> 决定 ——
    /// 需要 <c>diserial.dev.json</c> 里 <c>logLevel</c> 为 trace 且 <c>logPayload</c> 为 true，
    /// 两道门同时成立。
    /// </param>
    public SystemIoSerialPort(
        string portName,
        SerialPortSettings settings,
        IMonotonicClock clock,
        ILogger? logger = null,
        bool includePayload = false)
    {
        PortName = portName;
        Settings = settings;
        _clock = clock;
        _logger = logger ?? NullLogger.Instance;
        _includePayload = includePayload;
    }

    public string PortName { get; }

    /// <summary>
    /// ⚠️ <b>Read-only since 2026-08-02</b>, when <c>ApplySettingsAsync</c> was deleted — it was
    /// the only other writer. "The parameters are fixed for the port's lifetime" was already
    /// true in practice; now the type says so.
    /// </summary>
    public SerialPortSettings Settings { get; }

    public bool IsOpen => _port?.IsOpen == true;

    public event EventHandler<SerialChunkReceivedEventArgs>? DataReceived;

    public event EventHandler<SerialErrorEventArgs>? ErrorReceived;

    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsOpen) return;

            LogOpening();
            var started = Stopwatch.GetTimestamp();

            var port = new SerialPort(PortName)
            {
                // 读超时设为无限：读循环靠关闭端口来中断，而不是靠超时轮询。
                ReadTimeout = SerialPort.InfiniteTimeout,
                WriteTimeout = 2000,
                ReadBufferSize = 1 << 16
            };

            try
            {
                Apply(port, Settings);
                ApplyOutputLines(port);
                port.Open();
            }
            catch (Exception ex)
            {
                // 打开失败是 Linux/macOS 上的高频现场问题（设备节点权限、端口被占用），
                // 而 ErrorMessage 目前没有任何 View 绑定（P0-2）——
                // 日志是唯一能留下原因的地方。记完照常抛给上层。
                SerialPortLog.OpenFailed(
                    _logger, ex, PortName,
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds, ex.GetType().Name);
                port.Dispose();

                if (ex is UnauthorizedAccessException
                    && ProbeDeviceNodeOnOpenFailure
                    && IsAbsentDeviceNode(PortName, File.Exists))
                {
                    SerialPortLog.AbsentDeviceNodeOnOpen(_logger, PortName, ex.GetType().Name);

                    // ⭐ FileNotFoundException, because SerialErrorClassifier already maps it to
                    // PortNotFound and its wording ("may have been unplugged, or the name may be
                    // wrong") is exactly this situation. The classifier is not touched.
                    throw new FileNotFoundException(
                        $"The serial port '{PortName}' does not exist.", PortName, ex);
                }

                throw;
            }

            // 丢弃打开瞬间可能残留在驱动缓冲里的历史数据，
            // 否则首帧会带上一段来路不明的字节。
            port.DiscardInBuffer();
            port.DiscardOutBuffer();

            // ⭐ P1-52: 硬件级线路错误（校验 / 帧 / 溢出）从这里进来。
            // 在此之前**全项目零生产者** —— FrameFlags 里那三个值、ISerialPort 契约里
            // 「串口错误（校验错、帧错、溢出）」那句话、显示层的异常着色，
            // 三处都为它留了位置，而**数据永远不来**。
            // 对一个诊断工具，那是缺了一整类观测：波特率或校验位配错时，
            // 用户看到的是乱码，而不是「这条线上有校验错」。
            _lineErrorsSeen = FrameFlags.None;
            port.ErrorReceived += OnPortErrorReceived;

            // ⭐ P2-65: collect the previous round before overwriting the fields.
            //
            // ⛔ There is a path that reaches here with _readCts still set: a fatal fault
            // (DisposeDeadPort clears _port but nothing else, and IsOpen therefore goes false)
            // followed by the user clicking Connect without closing the session. CloseAsync never
            // ran, so an unconditional overwrite drops a CancellationTokenSource that nobody ever
            // disposed. ⚠️ Same shape as P1-54 ring 5 one layer up, found by looking for it there
            // after that one was fixed.
            //
            // ⭐ Awaiting the stale loop rather than just dropping it is what makes disposing the
            // CTS safe: the loop reads token.IsCancellationRequested every iteration, and that
            // throws ObjectDisposedException once its source is gone. After a fault the loop has
            // already returned, so this costs nothing on the path that actually reaches it.
            await CollectStaleReadLoopAsync();

            _port = port;
            _readCts = new CancellationTokenSource();
            _readLoop = Task.Run(() => ReadLoopAsync(port, _readCts.Token), CancellationToken.None);

            SerialPortLog.Opened(_logger, PortName, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// ⛔ <b>P2-108.</b> Whether an open failure should be checked against the filesystem before
    /// being handed up. <b>Windows must never take this path.</b>
    ///
    /// <para>⭐ <b>Split from <see cref="IsAbsentDeviceNode"/> on purpose</b>, the same way
    /// <c>SystemPortEnumerator.DropCallinNodes</c> is split from its predicate: the predicate is
    /// pure and therefore testable on <b>both</b> machines, while only this flag depends on where
    /// we are running. Folding the platform check into the predicate would leave the Windows
    /// machine unable to exercise the branch at all.</para>
    ///
    /// <para>⛔ <b>Why Windows is excluded and not merely "not needed".</b> Windows port names are
    /// device names (<c>COM3</c>), not paths, and <c>File.Exists("COM3")</c> is <b>false for a port
    /// that is present and busy</b>. Letting this run there would turn a correct "access denied"
    /// into a wrong "port not found" -- the exact defect this fixes, pointed the other way.</para>
    /// </summary>
    private static bool ProbeDeviceNodeOnOpenFailure => !OperatingSystem.IsWindows();

    /// <summary>
    /// ⛔⭐ <b>P2-108.</b> True when <paramref name="portName"/> names a device node that is not
    /// there, so the caller may report "port not found" instead of "access denied".
    ///
    /// <para><b>Why this exists.</b> Measured 2026-08-14 on a MacBook Air M4, six cases: a missing
    /// node, a busy node and a permission-denied node <b>all</b> throw
    /// <c>UnauthorizedAccessException</c>. The type cannot tell them apart, so the user was told
    /// "the port is in use by another program, or you do not have permission" for a cable that had
    /// simply been unplugged -- two named causes, both false. See 00-STATUS P2-108.</para>
    ///
    /// <para>⛔ <b>The inner errno is NOT usable and was measured, not assumed.</b> The
    /// <c>IOException</c> nested inside says "No such file or directory" for a node that <b>is</b>
    /// there and merely busy, and again for one that exists with mode 000 -- while the OS itself
    /// reports <c>EACCES</c> for the latter. It lies in precisely the two cases this predicate has
    /// to get right. <c>File.Exists</c> was correct in all six.</para>
    ///
    /// <para>⭐ <b>The leading slash is load-bearing.</b> Only a name that IS a filesystem path may
    /// be asked about; anything else is left alone. That keeps the predicate correct on its own
    /// terms even though <see cref="ProbeDeviceNodeOnOpenFailure"/> currently only lets it run off
    /// Windows.</para>
    ///
    /// <para>⚠️ <b>Absence is the only thing claimed.</b> A node that exists returns false here and
    /// keeps its original exception -- <c>AccessDenied</c> stays reachable and stays correct for
    /// the port that really is busy.</para>
    /// </summary>
    /// <param name="nodeExists">
    /// Injected so the predicate can be exercised without touching a real filesystem.
    /// Production passes <see cref="File.Exists(string)"/>.
    /// </param>
    public static bool IsAbsentDeviceNode(string portName, Func<string, bool> nodeExists)
    {
        ArgumentNullException.ThrowIfNull(nodeExists);

        if (string.IsNullOrEmpty(portName)) return false;
        if (portName[0] != '/') return false;

        return !nodeExists(portName);
    }

    /// <summary>
    /// ⭐ <b>The order of the three steps below is the whole of P2-62, and it is not obvious.</b>
    ///
    /// <para><b>What was measured (2026-08-05, <c>unattended-probe read-cancel</c>, on both an HHD
    /// virtual pair and a real FTDI port):</b> <c>SerialPort.BaseStream.ReadAsync(buffer, token)</c>
    /// on Windows <b>does not answer the token at all</b> — cancellation never reaches a
    /// <c>ReadFile</c> that is already pending. Both drivers behave identically, so this is a BCL
    /// fact, not a driver quirk. <c>Close()</c> <i>does</i> release it: 7.5 ms on the virtual pair,
    /// ~125 ms on the FTDI port.</para>
    ///
    /// <para>⛔ <b>So the previous order was backwards</b>: cancel (no effect) → wait out the full
    /// 2-second cap → only then close. Closing a healthy port therefore cost <b>2113 ms</b>, every
    /// millisecond of it spent waiting for something that was never going to happen.</para>
    ///
    /// <para>⚠️ <b>Cancel is still first, and it still matters — for a different reason.</b> It is
    /// not what unblocks the read; it is what tells the read loop that the exception it is about to
    /// get came from us. <see cref="SerialErrorClassifier.ClassifyReadLoopStop"/> keys on
    /// <c>cancellationRequested</c> and deliberately ignores the exception type (P1-36). ⛔ Close
    /// before cancel and the loop reports a device removal that never happened.</para>
    ///
    /// <para>⚠️ <b><c>Dispose</c> stays after the wait.</b> The loop is still holding
    /// <c>BaseStream</c> until it unwinds; <c>Close()</c> is what it needs to see, disposing the
    /// object underneath it buys nothing and is the more hostile of the two.</para>
    /// </summary>
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_port is null && _readCts is null) return;

            SerialPortLog.Closing(_logger, PortName);
            var started = Stopwatch.GetTimestamp();

            // 1. Signal intent. Does NOT unblock the read -- see the remarks above.
            _readCts?.Cancel();

            // ⚠️ Interlocked, even though we hold the gate: DisposeDeadPort runs on the read-loop
            // thread and deliberately does not take the gate (it would be waiting for itself).
            // Exactly one of the two ends up with the port object.
            var port = Interlocked.Exchange(ref _port, null);

            // 2. Close. THIS is what releases the pending read.
            if (port is not null)
            {
                // Registered in OpenAsync; removed here so a closed port cannot keep raising
                // into a session that is already gone (P2-44's shape).
                port.ErrorReceived -= OnPortErrorReceived;

                try
                {
                    if (port.IsOpen) port.Close();
                }
                catch (IOException ex)
                {
                    // 设备已拔出，关闭失败不影响清理 —— 但**不能静默**
                    // （01-spec 4.7 共有第 1 条：不许空 catch）。
                    SerialPortLog.CloseFailed(_logger, ex, PortName, ex.GetType().Name);
                }
            }

            // 3. Now the wait is short, because step 2 already did the work.
            var timedOut = await AwaitReadLoopAsync();

            port?.Dispose();

            // readLoopTimedOut is the observation point for "did the read loop unwind after the
            // close" -- true now means something is genuinely wrong, not merely that the token
            // was ignored (which is the normal case and no longer costs anything).
            SerialPortLog.Closed(
                _logger, PortName, Stopwatch.GetElapsedTime(started).TotalMilliseconds, timedOut);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Releases the port object the moment the read loop hits a fatal fault (P1-54).
    ///
    /// <para>⛔ <b>Read-loop thread only, and it deliberately does NOT take <c>_gate</c>.</b>
    /// <see cref="CloseAsync"/> holds the gate while it awaits the read loop, so a read-loop
    /// thread queuing on that same gate would be waiting for itself. The measured symptom is
    /// not a hang but a stall: <see cref="AwaitReadLoopAsync"/> caps the wait and moves on,
    /// which is far harder to spot than a deadlock.</para>
    ///
    /// <para>⭐ <b>Why CompareExchange rather than Exchange.</b> A concurrent
    /// <see cref="OpenAsync"/> may already have put a freshly opened port in the field — after
    /// an unplug <c>IsOpen</c> goes false on its own (measured 2026-08-05 on real hardware), so
    /// its <c>if (IsOpen) return;</c> guard does not hold the reconnect back. Clearing the field
    /// unconditionally would then dispose a port that had just been opened successfully. The
    /// dead port is disposed either way; only the field assignment is conditional.</para>
    ///
    /// <para>⚠️ <b>Dispose, not Close.</b> <c>SerialStream.Dispose</c> closes the handle in a
    /// <c>finally</c>, so it releases even when cancelling pending I/O throws — and on a device
    /// that is physically gone, it throws. Measured: a post-unplug release takes ~5 ms and
    /// reports nothing, which is why there is no retry here.</para>
    /// </summary>
    private void DisposeDeadPort(SerialPort dead)
    {
        Interlocked.CompareExchange(ref _port, null, dead);

        // Registered in OpenAsync. Removed here for the same reason CloseAsync removes it:
        // a dead port must not keep raising into a session that has moved on (P2-44's shape).
        dead.ErrorReceived -= OnPortErrorReceived;

        try
        {
            dead.Dispose();
        }
        catch (Exception ex)
        {
            // 01-spec 4.7, shared rule 1: no silent catch. Releasing a vanished device is
            // expected to be noisy; what matters is that it is on the record.
            SerialPortLog.CloseFailed(_logger, ex, PortName, ex.GetType().Name);
        }
    }

    /// <summary>
    /// Which line-error kinds have already been reported on the current open (P1-52).
    ///
    /// <para>⛔ <b>Throttling is not an optimisation here, it is the feature.</b> A wrong baud
    /// rate produces a framing error <b>per byte</b>: reporting every one would repaint the
    /// top banner several times a second and the user would never get to read a stable
    /// reason — the exact failure 01-spec 4.14 promise 7 records for timed sends
    /// ("提示条同时只存在一条，新的替换旧的"). Once per kind per connection says everything
    /// a user can act on; the rest are counted in the log.</para>
    /// </summary>
    private FrameFlags _lineErrorsSeen = FrameFlags.None;

    /// <summary>
    /// Hardware line errors from the driver (P1-52).
    ///
    /// <para>⚠️ <b>These are reported, not attached to a frame.</b>
    /// <c>SerialPort.ErrorReceived</c> says a line error happened; it does <b>not</b> say which
    /// bytes it happened to. Marking a specific frame would require inventing a correlation the
    /// driver never provided — and a diagnostic tool pointing at the wrong byte is worse than
    /// one saying "there were framing errors on this line". <b>Frame-level flags stay
    /// unimplemented on purpose</b> (00-STATUS P1-52).</para>
    ///
    /// <para>⚠️ <b>Never fatal.</b> A parity error means the data is suspect, not that the port
    /// is gone — the session keeps running and the user decides whether the settings are wrong.</para>
    /// </summary>
    private void OnPortErrorReceived(object sender, SerialErrorReceivedEventArgs e)
    {
        var flags = MapLineError(e.EventType);
        if (flags == FrameFlags.None) return;   // TXFull etc. -- not a line-integrity problem

        var firstOfItsKind = (_lineErrorsSeen & flags) == FrameFlags.None;
        _lineErrorsSeen |= flags;

        SerialPortLog.LineError(_logger, PortName, e.EventType.ToString(), firstOfItsKind);

        if (!firstOfItsKind) return;

        ErrorReceived?.Invoke(this, new SerialErrorEventArgs(
            flags,
            $"Serial line error on {PortName}: {e.EventType}",
            isFatal: false,
            kind: SerialErrorKind.LineError));
    }

    /// <summary>
    /// <c>SerialError</c> → <see cref="FrameFlags"/>.
    ///
    /// <para>⚠️ <c>TXFull</c> is deliberately not mapped: it says <b>our own</b> write buffer is
    /// full, which is a local flow-control condition, not a statement about the integrity of
    /// the data on the line. Folding it in would make "there were line errors" fire on a
    /// perfectly clean bus.</para>
    /// </summary>
    private static FrameFlags MapLineError(SerialError error) => error switch
    {
        SerialError.RXParity => FrameFlags.ParityError,
        SerialError.Frame => FrameFlags.FramingError,

        // Both are "bytes were lost": Overrun is the UART's own shift register being
        // overwritten, RXOver is the driver's buffer filling up.
        SerialError.Overrun => FrameFlags.BufferOverrun,
        SerialError.RXOver => FrameFlags.BufferOverrun,

        _ => FrameFlags.None
    };

    /// <summary>
    /// ⚠️ <b>Runs under <see cref="_gate"/>, same as open and close (P2-28).</b>
    ///
    /// <para>It used to read <see cref="_port"/> and write to <c>BaseStream</c> with no
    /// synchronisation at all, so a write racing a close could reach a stream that had already
    /// been disposed. The failure was survivable — logged and rethrown — but "survivable race"
    /// is not a design, and on a monitor session the write is a <b>bus injection</b>: the one
    /// operation in this product where the outcome has to be unambiguous.</para>
    ///
    /// <para>⚠️ <b>The cost is real and worth stating</b>: a write issued while a close is in
    /// progress now waits for that close, which can take the full read-loop stop timeout
    /// (~2 seconds). That is the correct answer — the alternative is writing to a port that is
    /// being torn down — but it means writes are not unconditionally prompt.</para>
    /// </summary>
    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Re-read inside the gate. Outside it this was only ever a hint.
            var port = _port;
            if (port is null || !port.IsOpen)
            {
                throw new InvalidOperationException($"Port {PortName} is not open.");
            }

            try
            {
                await port.BaseStream.WriteAsync(data, cancellationToken);
                await port.BaseStream.FlushAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                SerialPortLog.WriteFailed(_logger, ex, PortName, data.Length, ex.GetType().Name);
                throw;
            }

            SerialPortLog.Wrote(_logger, PortName, data.Length);

            if (_includePayload && _logger.IsEnabled(LogLevel.Trace))
            {
                SerialPortLog.ReadPayload(_logger, PortName, SerialPortLog.ToHex(data.Span));
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// ⭐ <b>Idempotent, as <see cref="IAsyncDisposable"/> requires</b> (P2-28).
    ///
    /// <para>A second call used to reach <c>CloseAsync</c>'s <c>_gate.WaitAsync</c> on a
    /// semaphore that had already been disposed and throw <see cref="ObjectDisposedException"/>.
    /// ⚠️ <b>That is easy to hit without doing anything odd</b>: ownership of these ports was
    /// itself doubled up in the shutdown path (P2-30), so "disposed twice" is the normal case
    /// here, not a misuse.</para>
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        await CloseAsync();

        DataReceived = null;
        ErrorReceived = null;

        // ⛔ _gate is deliberately NOT disposed, and that is not an oversight.
        //
        // Disposing it opens a window nothing can close: an operation already waiting on the
        // semaphore acquires it after CloseAsync releases, and then its own Release() throws
        // ObjectDisposedException from a finally block. Guarding every caller against that race
        // is strictly harder than not creating it.
        //
        // SemaphoreSlim only holds an unmanaged resource once AvailableWaitHandle has been
        // read, and this class never reads it -- so there is nothing here for Dispose to
        // reclaim. See docs/00-STATUS.md P2-28.
    }

    /// <summary>
    /// The read loop's single exit report, shared by <b>both</b> places that can end it
    /// (P2-106): taking the stream, and the read itself.
    ///
    /// <para>⛔ <b>Extracted rather than duplicated on purpose.</b> The two call sites must
    /// classify identically -- <see cref="SerialErrorClassifier.ClassifyReadLoopStop"/> keys
    /// off <paramref name="token"/> alone (P1-36), and a second hand-written copy of this
    /// decision is exactly how the two would drift apart.</para>
    /// </summary>
    private void ReportReadLoopStop(
        Exception ex, SerialPort port, CancellationToken token, long reads, long bytes)
    {
        var kind = SerialErrorClassifier.ClassifyReadLoopStop(ex, token.IsCancellationRequested);

        if (kind is null)
        {
            // 我们自己要求停的 —— 正常退出。
            SerialPortLog.ReadLoopExited(_logger, PortName, reads, bytes);
            return;
        }

        // 设备被拔出或句柄失效。上报为致命错误后退出循环 ——
        // 继续等待一个永远不会到来的读取毫无意义。
        SerialPortLog.ReadLoopFaulted(_logger, ex, PortName, reads, bytes, ex.GetType().Name);

        // ⭐ P1-54: release the port object before telling anyone, so the fault is
        // already a settled state by the time a listener looks at IsOpen.
        DisposeDeadPort(port);

        ErrorReceived?.Invoke(this,
            new SerialErrorEventArgs(
                FrameFlags.None, ex.Message, isFatal: true, kind: kind.Value));
    }

    private async Task ReadLoopAsync(SerialPort port, CancellationToken token)
    {
        var buffer = new byte[ReadBufferSize];

        // 读次数与字节数只在循环退出时上报一次，不进热路径的每一轮。
        long reads = 0, bytes = 0;
        DateTimeOffset? previous = null;

        Stream stream;
        try
        {
            // ⛔⭐ P2-106: this line used to sit OUTSIDE any try -- directly above the
            // catch-all that P1-46 widened to "catch everything". The promise in that
            // comment was true; this one statement simply sat above it.
            //
            // **It throws in an ordinary race**: this task is queued by Task.Run in
            // OpenAsync, and a close arriving before the thread pool gets to it leaves
            // `port` already closed, so BaseStream answers with InvalidOperationException
            // ("The BaseStream is only available when the port is open."). The faulted task
            // was then awaited by AwaitReadLoopAsync and escaped out of CloseAsync and
            // DisposeAsync -- measured 1 in 5 on a cold thread pool, 0 in 30 once warm.
            //
            // ⭐ During a close the token is already cancelled (CloseAsync cancels before
            // it closes), so this routes to the "our own stop" branch and is logged as a
            // normal exit -- not reported to the user as a device fault.
            stream = port.BaseStream;
        }
        catch (Exception ex)
        {
            ReportReadLoopStop(ex, port, token, reads, bytes);
            return;
        }

        while (!token.IsCancellationRequested)
        {
            int count;
            try
            {
                count = await stream.ReadAsync(buffer, token);
            }
            // ⚠️ There is deliberately **no dedicated catch for OperationCanceledException**.
            //
            // The original code had one, and it logged an unplugged device as "exited
            // normally": unplugging ends ReadAsync in a cancellation, that catch read it
            // unconditionally as "we asked it to stop", and so ErrorReceived never fired
            // -- no banner, no fault log, the status bar stuck on "connected". P1-36,
            // found 2026-07-31 by unplugging a real USB adapter.
            //
            // **The only criterion is whether the token was cancelled. The exception
            // type takes no part in that decision.**
            //
            // ⭐ P1-46: the filter used to be an exception whitelist -- which contradicted
            // the paragraph directly above it. Anything outside the list (a driver-level
            // Win32Exception, say) escaped, left the Task.Run task silently faulted, and
            // the session went on claiming "connected" exactly as in P1-36. The comment
            // had been promising more than the code delivered; the catch-all is what
            // makes the promise true rather than aspirational.
            //
            // ⚠️ Unclassifiable exceptions come back as SerialErrorKind.Unknown, which is
            // the classifier's documented "never guess" answer -- a vague reason the user
            // can act on beats a precise one that might be wrong, and both beat silence.
            catch (Exception ex)
            {
                ReportReadLoopStop(ex, port, token, reads, bytes);
                return;
            }

            // 时间戳必须紧跟 ReadAsync 返回，任何额外处理都会引入误差。
            // ⚠️ 日志调用一律排在这一行之后，绝不能插到前面。
            var timestamp = _clock.Now;

            if (count <= 0) continue;

            reads++;
            bytes += count;

            // Q-1 的核心观测点：间隔成簇出现在某个固定值附近（如 16ms）
            // 且单次字节数跨越多帧，即说明驱动在攒帧。
            var gapMs = previous is { } p ? (timestamp - p).TotalMilliseconds : 0d;
            previous = timestamp;
            SerialPortLog.ReadChunk(_logger, PortName, count, gapMs, timestamp);

            if (_includePayload && _logger.IsEnabled(LogLevel.Trace))
            {
                SerialPortLog.ReadPayload(
                    _logger, PortName, SerialPortLog.ToHex(buffer.AsSpan(0, count)));
            }

            // A fresh array per read, deliberately (P2-37, 2026-08-05 — the trade-off was
            // undocumented until then).
            //
            // The cost is real: at a high frame rate this is one gen-0 allocation per read,
            // and the read loop is the hottest path in the program.
            //
            // ⛔ ArrayPool is NOT the fix here, and the reason is a correctness one rather
            // than a measurement: `payload` is handed to DataReceived subscribers and
            // outlives this iteration. The frame splitter keeps the memory in its pending
            // buffer, the capture session hands it on, and C-09 writes it to disk on a
            // different thread. Returning the array to a pool would require every one of
            // those consumers to signal "done with it" — that is an ownership contract
            // across three layers, on ISerialPort, to save one copy. `buffer` above is the
            // reused one; this copy is what makes the escape safe.
            //
            // If this ever needs to change, the shape that works is handing out a
            // pooled-and-owned type (IMemoryOwner<byte>) through the event args, so the
            // ownership transfer is in the contract instead of in a comment.
            var payload = new byte[count];
            buffer.AsSpan(0, count).CopyTo(payload);
            DataReceived?.Invoke(this, new SerialChunkReceivedEventArgs(payload, timestamp));
        }

        SerialPortLog.ReadLoopExited(_logger, PortName, reads, bytes);
    }

    /// <summary>
    /// Waits for the read loop to unwind, <b>after the caller has already closed the port</b>.
    ///
    /// <para>⭐ <b>Renamed from <c>StopReadLoopAsync</c> on 2026-08-05 (P2-62), and the rename is
    /// the point</b>: the old name promised that calling it stopped the loop, and for two months
    /// nothing did — cancelling a pending serial read is a no-op on Windows (measured, see
    /// <see cref="CloseAsync"/>). It only ever waited. Now it says so, and the thing that actually
    /// stops the loop sits in the caller where you can see it.</para>
    ///
    /// <para>⚠️ <b>Two seconds became 500 ms with it.</b> The old cap was sized for "the loop might
    /// never answer", which was the normal case; the measured cost of unwinding after a close is
    /// 7.5 ms on a virtual pair and ~125 ms on a real FTDI port, so 500 ms is a wide margin over
    /// the worst number ever recorded. ⛔ A timeout here now means something is genuinely wrong —
    /// which is the whole reason the field is logged.</para>
    /// </summary>
    /// <returns>
    /// Whether the read loop <b>failed</b> to unwind within <see cref="ReadLoopUnwindTimeout"/>.
    /// The value goes into the close log as <c>readLoopTimedOut</c>. ⚠️ Before P2-62 it was the
    /// observation point for "does cancellation reach a pending read" — it always did not, so the
    /// field was true on every ordinary close. Now that the close comes first, a true here is a
    /// real signal rather than the expected case.
    /// </returns>
    private async Task<bool> AwaitReadLoopAsync()
    {
        if (_readCts is null) return false;

        var timedOut = false;

        if (_readLoop is not null)
        {
            try
            {
                await _readLoop.WaitAsync(ReadLoopUnwindTimeout);
            }
            catch (Exception e) when (e is TimeoutException or OperationCanceledException)
            {
                // timedOut 会一路带到 SerialPortLog.Closed 的 readLoopTimedOut 字段（Information 级），
                // 那才是要看的判据。这里再记一条 Debug 只为留下异常原文 ——
                // 01-spec 4.7 共有第 1 条要求 catch 处自己就留痕，不依赖调用方。
                SerialPortLog.ReadLoopStopTimedOut(_logger, e, PortName);
                timedOut = true;
            }
            catch (Exception e)
            {
                // ⛔⭐⭐ P2-106: the read loop task itself faulted. **Close must not pass that on.**
                //
                // ⚠️ The filter above used to be the only catch here, so anything else escaped
                // through CloseAsync and out of DisposeAsync -- and "dispose twice" is the
                // ordinary shutdown path (P2-30). ISerialPort.CloseAsync documents closing as a
                // request for a state rather than an operation that can be refused; this is the
                // half of that promise which lives on this side.
                //
                // ⭐ Swallowing here is not a silent catch (01-spec 4.7): it is logged at Error,
                // and a fault the loop understood was already reported through ErrorReceived by
                // ReportReadLoopStop. What reaches this line is the loop dying in a way its own
                // handler did not cover -- which is a defect in this class, not news for the user
                // mid-shutdown.
                //
                // ⛔ It is deliberately NOT folded into timedOut: that field answers "did the loop
                // unwind in time", and a faulted loop did unwind -- badly. Merging them would put
                // a wrong answer into the close log's readLoopTimedOut.
                SerialPortLog.ReadLoopStopFaulted(_logger, e, PortName, e.GetType().Name);
            }
        }

        _readCts.Dispose();
        _readCts = null;
        _readLoop = null;

        return timedOut;
    }

    /// <summary>
    /// How long to wait for the read loop to unwind after the port has been closed (P2-62).
    /// Measured worst case is ~125 ms on real FTDI hardware; this is a deliberate wide margin.
    /// </summary>
    private static readonly TimeSpan ReadLoopUnwindTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Disposes the previous round's read-loop state, if any survived (P2-65).
    /// ⛔ Caller must hold <see cref="_gate"/>.
    ///
    /// <para>Reached only after a fatal fault that was followed by a reconnect rather than a
    /// close — every other path goes through <see cref="CloseAsync"/>, which leaves both fields
    /// null. On that path the loop has already returned, so the await completes immediately.</para>
    /// </summary>
    private async Task CollectStaleReadLoopAsync()
    {
        if (Interlocked.Exchange(ref _readCts, null) is not { } stale) return;

        var staleLoop = _readLoop;
        _readLoop = null;

        if (staleLoop is not null)
        {
            try
            {
                await staleLoop.WaitAsync(ReadLoopUnwindTimeout);
            }
            catch (Exception e) when (e is TimeoutException or OperationCanceledException)
            {
                // ⛔ Not silent (01-spec 4.7 shared rule 1). Reaching this means the previous
                // loop is still running while we are about to start a second one, which is the
                // one thing this method exists to prevent -- so it is worth a line even though
                // the reconnect goes ahead regardless.
                SerialPortLog.ReadLoopStopTimedOut(_logger, e, PortName);
            }
        }

        stale.Dispose();
    }

    private void LogOpening() => SerialPortLog.Opening(
        _logger, PortName, Settings.BaudRate, Settings.DataBits,
        Settings.Parity.ToString(), Settings.StopBits.ToString(), Settings.FlowControl.ToString());

    private static void Apply(SerialPort port, SerialPortSettings settings)
    {
        port.BaudRate = settings.BaudRate;
        port.DataBits = settings.DataBits;
        port.Parity = ToParity(settings.Parity);
        port.StopBits = ToStopBits(settings.StopBits);
        port.Handshake = ToHandshake(settings.FlowControl);

        // ⚠️ The two output lines are NOT set here. Apply stays a pure function of its two
        // arguments; the lines depend on instance state that outlives any one port object.
        // OpenAsync calls ApplyOutputLines immediately after this, once Handshake is set --
        // ApplyOutputLines reads Handshake to decide whether RTS is the driver's to own.
    }

    /// <summary>
    /// Pushes the two output lines onto a real port (T-07, spec 4.15).
    ///
    /// <para>⛔ <b>2026-08-06 (T-07): BOTH LINES REVERSED, BY EXPLICIT USER DECISION.</b> This
    /// used to be <c>DtrEnable = true</c> unconditionally and
    /// <c>RtsEnable = FlowControl != RequestToSend</c> — i.e. both asserted whenever there was no
    /// hardware flow control. They are now whatever the user's two checkboxes say, and
    /// <b>both start unticked</b>.</para>
    ///
    /// <para>⛔ <b>WHAT THAT COSTS, and why it is not a defect.</b> Devices that gate
    /// transmission on DTR or on RTS now send nothing until the user ticks the matching box —
    /// and the panel holding those boxes is <b>collapsed by default</b>. The symptom is
    /// "connected, and not one byte arrives", with the fix off-screen. Nothing goes red and
    /// nothing is logged, because the program is doing exactly what it was told.</para>
    ///
    /// <para>⚠️ <b>This is the product of two decisions, not one</b> — DTR default false and RTS
    /// default false were chosen separately, and neither one alone has this shape. The user was
    /// told once and chose it; spec 4.15 carries the explicit clause. <b>Do not "fix" the
    /// defaults back without asking.</b></para>
    ///
    /// <para>⭐ <b>Flow control still wins for RTS.</b> With <c>Handshake.RequestToSend</c> the
    /// driver owns that line, so the checkbox is disabled in the view and this method leaves it
    /// alone: writing it here would either be silently overwritten or would break the handshake.
    /// DTR has no such conflict.</para>
    /// </summary>
    private void ApplyOutputLines(SerialPort port)
    {
        port.DtrEnable = _dtrAsserted;

        if (port.Handshake is not Handshake.RequestToSend)
        {
            port.RtsEnable = _rtsAsserted;
        }
    }

    /// <inheritdoc />
    public SerialControlLines ReadControlLines()
    {
        // Read the field once. Between this line and the reads below, CloseAsync may swap the
        // field to null and dispose the object -- which is precisely why the catch is here and
        // not a null check.
        var port = _port;
        if (port is null || !port.IsOpen) return SerialControlLines.Unknown;

        try
        {
            // ⭐ One pass, three lines: see SerialControlLines. Reading them one at a time from
            // three call sites would let the panel show a mix of two different moments.
            var lines = new SerialControlLines(
                ToState(port.CtsHolding), ToState(port.DsrHolding), ToState(port.CDHolding));

            _controlLineReadFailing = false;
            return lines;
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                       or ObjectDisposedException
                                       or IOException
                                       or UnauthorizedAccessException)
        {
            // ⛔ Unknown, NOT Low. The level was never observed, and a grey dot meaning "not
            // asserted" would be a confident statement about something we did not measure.

            // ⭐ EDGE-TRIGGERED, and that is the whole design of this catch. The naive choices
            // are both wrong here:
            //
            //   * log every time  -> this runs every 250 ms from a UI timer, so one unplugged
            //     cable writes the same line four times a second and buries whatever the user
            //     is actually trying to diagnose.
            //   * log never       -> a silent catch, which 01-spec 4.7 shared rule 1 forbids
            //     outright, and the guardrail (SourceConventionTests) fails the build for it.
            //
            // ⚠️ The allowlist was NOT the way out: it is per-FILE, so exempting this method
            // would disarm the rule for every other catch in this class -- several of which are
            // load-bearing (CloseFailed, WriteFailed).
            //
            // So: report the transition into failure once, and clear the flag on the next
            // success. The edge is the informative part; the repetition never was.
            if (!_controlLineReadFailing)
            {
                _controlLineReadFailing = true;
                SerialPortLog.ControlLineReadFailed(_logger, ex, PortName, ex.GetType().Name);
            }

            return SerialControlLines.Unknown;
        }
    }

    /// <summary>
    /// True once <see cref="ReadControlLines"/> has failed and not yet succeeded again.
    ///
    /// <para>Exists solely so the failure is logged on its <b>edge</b> rather than on every one
    /// of the four polls a second. <c>volatile</c> for the same reason as the two line fields:
    /// the poll comes from the UI thread.</para>
    /// </summary>
    private volatile bool _controlLineReadFailing;

    private static ControlLineState ToState(bool asserted) =>
        asserted ? ControlLineState.High : ControlLineState.Low;

    /// <inheritdoc />
    public async Task SetOutputLineAsync(
        SerialOutputLine line, bool asserted, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Remember it first, unconditionally. A checkbox ticked while disconnected still
            // means what it said -- Apply picks it up on the next open.
            switch (line)
            {
                case SerialOutputLine.Dtr: _dtrAsserted = asserted; break;
                case SerialOutputLine.Rts: _rtsAsserted = asserted; break;
                default: throw new ArgumentOutOfRangeException(nameof(line), line, null);
            }

            var port = _port;
            if (port is null || !port.IsOpen) return;

            try
            {
                ApplyOutputLines(port);
            }
            catch (Exception ex) when (ex is InvalidOperationException
                                           or ObjectDisposedException
                                           or IOException
                                           or UnauthorizedAccessException)
            {
                // ⚠️ This one IS logged, unlike the read above: it happens once per user click,
                // not four times a second, and "I ticked the box and nothing happened" is a
                // question the log has to be able to answer. 01-spec 4.7: no silent catch.
                SerialPortLog.ControlLineFailed(_logger, ex, PortName, line.ToString(), ex.GetType().Name);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static Parity ToParity(CoreParity value) => value switch
    {
        CoreParity.Odd => Parity.Odd,
        CoreParity.Even => Parity.Even,
        CoreParity.Mark => Parity.Mark,
        CoreParity.Space => Parity.Space,
        _ => Parity.None
    };

    private static StopBits ToStopBits(CoreStopBits value) => value switch
    {
        CoreStopBits.OnePointFive => StopBits.OnePointFive,
        CoreStopBits.Two => StopBits.Two,
        _ => StopBits.One
    };

    private static Handshake ToHandshake(CoreFlowControl value) => value switch
    {
        CoreFlowControl.RequestToSend => Handshake.RequestToSend,
        CoreFlowControl.XOnXOff => Handshake.XOnXOff,
        _ => Handshake.None
    };
}
