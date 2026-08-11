using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using DiSerial.Infrastructure.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiSerial.Infrastructure.Sessions;

/// <summary>
/// 单串口终端采集会话 —— 串口字节流到 <see cref="SerialFrame"/> 的完整通路。
///
/// 职责：打开端口 → 读取字节 → 分帧 → 组装成帧 → 推送给上层。
/// ViewModel 只订阅 <see cref="FrameCaptured"/>，不接触任何 I/O。
///
/// <b>两处关键设计</b>：
///
/// 1. <b>空闲刷新定时器</b>。空闲分帧只能在下一块数据到达时回溯判定上一帧结束，
///    因此一段突发通讯的最后一帧会滞留在缓冲区。对 Modbus 这种一问一答的协议，
///    表现就是应答帧迟迟不显示。本类用一个定时器调用
///    <see cref="IFrameSplitter.FlushIfIdle"/> 补上该触发。
///
/// 2. <b>发送即回显</b>。写出数据的同时产生一条 Tx 方向的帧，
///    这样发送内容与接收内容出现在同一条时间轴上（T-02 本地回显）。
/// </summary>
public sealed class TerminalCaptureSession : ICaptureSession, IControlLineSession
{
    /// <summary>刷新定时器周期的下限，避免高波特率下过于频繁地唤醒。</summary>
    private static readonly TimeSpan MinFlushPeriod = TimeSpan.FromMilliseconds(5);

    /// <summary>刷新定时器周期的上限，保证最后一帧的显示延迟可接受。</summary>
    private static readonly TimeSpan MaxFlushPeriod = TimeSpan.FromMilliseconds(50);

    private readonly ISerialPort _port;
    private readonly IFrameSplitter _splitter;
    private readonly IMonotonicClock _clock;
    private readonly ILogger _logger;
    private readonly Lock _sync = new();

    private CancellationTokenSource? _flushCts;
    private Task? _flushLoop;
    private long _sequence;
    private DateTimeOffset? _previousFrameEndedAt;

    /// <summary>
    /// ⭐ <b><paramref name="logger"/> arrived with P1-11 (2026-08-05)</b>, which is the change
    /// that gave this class something to record — state transitions. Until then it was the one
    /// session type with no logger at all, and <see cref="CaptureSessionFactory"/> said so
    /// explicitly rather than passing an unused one (P2-37).
    ///
    /// <para>It is optional and defaults to <see cref="NullLogger"/> for the same reason
    /// <see cref="MonitorCaptureSession"/>'s is: test doubles constructing a session are not
    /// interested in diagnostics, and none of them had to change.</para>
    /// </summary>
    public TerminalCaptureSession(
        ISerialPort port,
        IFrameSplitter splitter,
        IMonotonicClock clock,
        ILogger? logger = null)
    {
        _port = port;
        _splitter = splitter;
        _clock = clock;
        _logger = logger ?? NullLogger.Instance;

        _port.DataReceived += OnDataReceived;
        _port.ErrorReceived += OnErrorReceived;
    }

    public SessionKind Kind => SessionKind.Terminal;

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    public DateTimeOffset? StartedAt { get; private set; }

    public event EventHandler<FrameCapturedEventArgs>? FrameCaptured;

    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    public event EventHandler<SerialErrorEventArgs>? LineErrorDetected;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (State == ConnectionState.Connected) return;

        SetState(ConnectionState.Connecting);
        try
        {
            await _port.OpenAsync(cancellationToken);
        }
        catch
        {
            SetState(ConnectionState.Faulted);
            throw;
        }

        lock (_sync)
        {
            // 断开前攒了一半的帧不能与重连后的新数据接上 —— 必须清。
            _splitter.Reset();

            // 跨越一次断线的 Δ 没有意义，让重连后的首帧显示「无上一帧」。
            _previousFrameEndedAt = null;

            // ⚠️ **序号刻意不重置**（P1-37，2026-07-31）。
            // 显示缓冲与记录批次都跨重连保留，归零会让同一屏、
            // 同一个导出文件里出现**重复的 Seq**（1,2,3,1,2,3…）——
            // 而 Seq 是导出文件里唯一的行标识。
        }

        // ⚠️ **原点只在首次连接时定**（P1-37，2026-07-31）。
        // 重连时重置会让相对时间戳**倒退**：显示缓冲不清空，
        // 于是新行的相对时间比屏幕上方的旧行还小，同一屏里时间戳非单调。
        // **在一个价值全在时序的工具里，那是在对时序撒谎。**
        //
        // 代价是明知的：「相对时间」从此指「相对于**会话建立**」而非
        // 「相对于本次连接」，断线期间的时间也计在内。
        StartedAt ??= _clock.Now;
        _flushCts = new CancellationTokenSource();
        _flushLoop = Task.Run(() => FlushLoopAsync(_flushCts.Token), CancellationToken.None);

        SetState(ConnectionState.Connected);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_flushCts is { } cts)
        {
            await cts.CancelAsync();
            if (_flushLoop is not null)
            {
                try { await _flushLoop; }
                catch (OperationCanceledException) { }
            }
            cts.Dispose();
            _flushCts = null;
            _flushLoop = null;
        }

        await _port.CloseAsync(cancellationToken);

        // 端口关闭后把缓冲区里剩下的字节作为最后一帧吐出，避免静默丢数据。
        EmitFrames(FlushRemaining());

        SetState(ConnectionState.Disconnected);
    }

    public async Task SendAsync(
        ChannelId channel, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (State != ConnectionState.Connected || data.IsEmpty) return;

        // 本地回显（T-02）：发送内容与接收内容进入同一条时间轴。
        //
        // ⛔ P2-59：这一段必须在 WriteAsync **之前**，不能挪到后面。
        // 原先是先写、写完再取时刻建帧，于是「对端应答比写调用返回还快」时，
        // 界面把**发送显示在它自己引发的接收之后** —— 真实 FTDI 自环上 4 次里中 2 次，
        // Δ 实测到 -5.5ms。放在前面同时修好两件事：
        //   · 时间戳 —— 取的是「开始写」而不是「写完成」；
        //   · ⭐ 行序 —— 序号也在这里分配，所以 TX 帧的 Sequence 必定小于
        //     写期间到达的任何 RX 帧。只挪时间戳修不了行序（那是 P2-59 里
        //     「两个问题别当成一个」那张表）。
        //
        // ⚠️ 代价，明知接受：**写失败时这条 TX 帧已经显示出去了**。
        // 保留它是刻意的 —— 写失败时字节可能已经上了线（部分写入），
        // 抹掉它才是撒谎，与 4.9.2「不能对时序撒谎」同一取向。
        // 用户看到的是「帧 + 顶部红条」，而不是「什么都没有 + 红条」。
        var now = _clock.Now;
        EmitFrames([CreateFrame(new FramedData(data.ToArray(), now, now), FrameDirection.Tx)]);

        await _port.WriteAsync(data, cancellationToken);
    }

    /// <summary>
    /// Straight through to the one port a terminal session has (T-07, spec 4.15).
    ///
    /// <para>⭐ <b>No guard on session state here.</b> <c>SystemIoSerialPort</c> already answers
    /// <see cref="SerialControlLines.Unknown"/> for a port that is not open, and duplicating
    /// that check would give the question two owners that could drift apart.</para>
    ///
    /// <para>⚠️ <b>This class implements <see cref="IControlLineSession"/>;
    /// <c>MonitorCaptureSession</c> deliberately does not</b> — see that interface for why the
    /// "no panel in monitor sessions" promise is expressed as a type fact rather than a view
    /// rule.</para>
    /// </summary>
    public SerialControlLines ReadControlLines() => _port.ReadControlLines();

    /// <inheritdoc />
    public bool IsRtsOwnedByFlowControl =>
        _port.Settings.FlowControl == SerialFlowControl.RequestToSend;

    /// <inheritdoc />
    public Task SetOutputLineAsync(
        SerialOutputLine line, bool asserted, CancellationToken cancellationToken = default)
        => _port.SetOutputLineAsync(line, asserted, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        _port.DataReceived -= OnDataReceived;
        _port.ErrorReceived -= OnErrorReceived;

        await StopAsync();
        await _port.DisposeAsync();

        FrameCaptured = null;
        LineErrorDetected = null;
        StateChanged = null;
    }

    private void OnDataReceived(object? sender, SerialChunkReceivedEventArgs e)
    {
        List<SerialFrame>? frames = null;

        // 只在锁内做分帧与序号分配；事件在锁外触发，
        // 避免把订阅方（可能要切到 UI 线程）的耗时纳入临界区。
        lock (_sync)
        {
            foreach (var framed in _splitter.Append(e.Data, e.Timestamp))
            {
                (frames ??= []).Add(CreateFrameLocked(framed, FrameDirection.Rx));
            }
        }

        EmitFrames(frames);
    }

    private void OnErrorReceived(object? sender, SerialErrorEventArgs e)
    {
        if (!e.IsFatal)
        {
            // P1-52: hardware line errors arrive here. They are not a state change -- the port
            // is open and the session keeps running -- so they go out on their own event.
            // The port has already throttled them to once per kind per connection.
            LineErrorDetected?.Invoke(this, e);
            return;
        }

        // The device is gone: flush what is left in the buffer, then go Faulted, rather than
        // leaving the UI on "connected" with nothing ever updating again.
        //
        // ⚠️ Kind has to travel with it: Message is the English diagnostic (it goes to the log),
        // while the reason shown on screen is mapped from Kind by the App layer
        // (01-spec 4.7, error path 3).
        EmitFrames(FlushRemaining());

        // ⭐ P1-54 ring 5: cancel the flush loop right here, at the moment the fault happens.
        //
        // Without this, StartAsync's unconditional `_flushCts = new ...` on the next reconnect
        // orphans this one -- a PeriodicTimer, a Task and a CTS leaked per reconnect, never
        // reclaimed until the session is disposed. ⭐ Measured 2026-08-05 on real hardware
        // (COM5, cable pulled): both the old and the new loop sat in WaitingForActivation.
        //
        // ⛔ Cancel only -- deliberately no await and no Dispose. This runs on the read-loop
        // thread, and anything that waits for that loop to finish would be waiting for itself.
        // The loop notices within one tick (≤50 ms) and exits on its own; the CTS is then
        // unreferenced and collectable. Disposing it here would instead make the still-running
        // loop throw ObjectDisposedException, which its catch does not cover.
        _flushCts?.Cancel();

        SetState(ConnectionState.Faulted, e.Message, e.Kind);
    }

    /// <summary>
    /// 空闲刷新循环 —— 修复「突发通讯的最后一帧永不显示」这一缺陷。
    ///
    /// 周期取分帧阈值本身，并钳制在 5–50ms：过密会空耗 CPU，
    /// 过疏会让最后一帧的显示延迟变得可感知。
    /// 注意这只影响<b>显示时机</b>，不影响帧自身的时间戳 ——
    /// 后者来自数据实际到达的时刻，与本定时器无关。
    /// </summary>
    private async Task FlushLoopAsync(CancellationToken token)
    {
        var period = TimeSpan.FromMilliseconds(Math.Clamp(
            _splitter.IdleGap.TotalMilliseconds,
            MinFlushPeriod.TotalMilliseconds,
            MaxFlushPeriod.TotalMilliseconds));

        using var timer = new PeriodicTimer(period);

        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                List<SerialFrame>? frames = null;
                lock (_sync)
                {
                    if (_splitter.FlushIfIdle(_clock.Now) is { } framed)
                    {
                        frames = [CreateFrameLocked(framed, FrameDirection.Rx)];
                    }
                }
                EmitFrames(frames);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private List<SerialFrame>? FlushRemaining()
    {
        lock (_sync)
        {
            return _splitter.Flush() is { } framed
                ? [CreateFrameLocked(framed, FrameDirection.Rx)]
                : null;
        }
    }

    private SerialFrame CreateFrame(FramedData framed, FrameDirection direction)
    {
        lock (_sync) return CreateFrameLocked(framed, direction);
    }

    /// <summary>调用方必须持有 <see cref="_sync"/>。</summary>
    private SerialFrame CreateFrameLocked(FramedData framed, FrameDirection direction)
    {
        var origin = StartedAt ?? framed.StartedAt;

        // Delta 取「上一帧结束 → 本帧开始」的真实空闲间隔，
        // 而非两帧时间戳之差 —— 后者会把帧自身的传输耗时算进去。
        // 该语义与监听会话的响应延迟一致，将来可直接复用。
        var delta = _previousFrameEndedAt is { } previous
            ? framed.StartedAt - previous
            : (TimeSpan?)null;

        _previousFrameEndedAt = framed.EndedAt;

        return new SerialFrame
        {
            Sequence = ++_sequence,
            Timestamp = framed.StartedAt,
            Elapsed = framed.StartedAt - origin,
            Delta = delta,
            Channel = ChannelId.None,
            Direction = direction,
            Data = framed.Data
        };
    }

    private void EmitFrames(List<SerialFrame>? frames)
    {
        if (frames is null) return;

        foreach (var frame in frames)
        {
            FrameCaptured?.Invoke(this, new FrameCapturedEventArgs(frame));
        }
    }

    private void SetState(
        ConnectionState state,
        string? message = null,
        SerialErrorKind errorKind = SerialErrorKind.Unknown)
    {
        if (State == state) return;

        // P1-11: the transition goes on the record before anyone reacts to it, so a support log
        // reads in causal order. The early return above means this only ever logs real changes.
        var from = State;
        State = state;
        SessionLog.StateTransition(_logger, from, state, Kind, _port.PortName);

        StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(state, message, errorKind));
    }
}
