using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using DiSerial.Infrastructure.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Sessions;

/// <summary>
/// 双串口监听采集会话 —— V1.0 的核心（P0-1）。
///
/// <b>设计的六条决定见 docs/02-architecture.md 第八节</b>，用户可见的承诺与局限见
/// docs/01-spec.md 4.9。本类只实现它们，不重复论证。三条最要紧的：
///
/// <list type="number">
///   <item><b>归并就在本类内，没有独立归并层</b> —— 不重排之后「归并」退化成
///         「两个读者共用同一个 <see cref="FrameCaptured"/> 事件」，没有算法可放进另一层。</item>
///   <item><b>不做跨通道重排</b> —— 两路帧按<b>到达顺序</b>发出。跨通道间隔小于约 5ms
///         （flush 周期）时相邻两行可能时间戳倒序、Δ 可能为负，<b>负数原样显示</b>。</item>
///   <item><b>Δ 与序号跨通道共享</b> —— 见 <see cref="CreateFrameLocked"/>。
///         这是全类最容易写错的地方。</item>
/// </list>
///
/// <b>刻意不复用 <see cref="TerminalCaptureSession"/> 的读循环</b>（决定 4）：
/// 终端是全产品唯一端到端验证过的链路，重构它是当时风险最高的动作。
/// 代价是本类与它结构同形、约 60–80 行重复；对冲是 <c>MonitorCaptureSessionTests</c>
/// 与 <c>TerminalCaptureSessionTests</c> 用同一套脚手架、钉同一批不变量。
///
/// ⚠️ <b>不复用 ≠ 决定永远重复</b>：抽公共 <c>ChannelReader</c> 推到「有两个能跑的
/// 实现之后」——那时有两个参照物可以互相对，比现在只有一个时抽更容易。
/// </summary>
public sealed class MonitorCaptureSession : ICaptureSession
{
    /// <summary>刷新定时器周期的下限。与终端同值，理由见 TerminalCaptureSession。</summary>
    private static readonly TimeSpan MinFlushPeriod = TimeSpan.FromMilliseconds(5);

    private static readonly TimeSpan MaxFlushPeriod = TimeSpan.FromMilliseconds(50);

    private readonly ISerialPort _portA;
    private readonly ISerialPort _portB;
    private readonly IFrameSplitter _splitterA;
    private readonly IFrameSplitter _splitterB;
    private readonly IMonotonicClock _clock;
    private readonly ILogger _logger;

    /// <summary>
    /// 保护下方三项共享状态。
    ///
    /// ⚠️ <b>两个通道共用同一把锁</b> —— 序号与 Δ 是跨通道的，分开锁就不成立了。
    /// 与终端一样：<b>只在锁内做分帧与组帧，事件在锁外触发</b>，
    /// 避免把订阅方（要切到 UI 线程）的耗时纳入临界区。
    /// </summary>
    private readonly Lock _sync = new();

    private CancellationTokenSource? _flushCts;
    private Task? _flushLoop;

    /// <summary>跨通道的单一序号 —— 归并后的时间轴上只有一个序列。</summary>
    private long _sequence;

    /// <summary>
    /// 归并后<b>上一帧</b>的结束时刻，<b>不分通道</b>。
    ///
    /// ⭐ <b>这一个字段就是「Δ 语义上移到归并层」的全部实现。</b>
    /// 若把它拆成每通道一份，Δ 就退化成「同通道帧间静默」——
    /// 而规格要的是「跨通道响应延迟」。**那样界面照常显示数字，只是数字错了**，
    /// 与 02-architecture 第六节那个缺陷同一形状。
    /// 由 <c>MonitorCaptureSessionTests</c> 的判别用例钉住。
    /// </summary>
    private DateTimeOffset? _previousFrameEndedAt;

    public MonitorCaptureSession(
        ISerialPort portA,
        ISerialPort portB,
        IFrameSplitter splitterA,
        IFrameSplitter splitterB,
        IMonotonicClock clock,
        ILogger? logger = null)
    {
        _portA = portA;
        _portB = portB;
        _splitterA = splitterA;
        _splitterB = splitterB;
        _clock = clock;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        _portA.DataReceived += OnDataReceivedA;
        _portB.DataReceived += OnDataReceivedB;
        _portA.ErrorReceived += OnErrorReceivedA;
        _portB.ErrorReceived += OnErrorReceivedB;
    }

    public SessionKind Kind => SessionKind.Monitor;

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    public DateTimeOffset? StartedAt { get; private set; }

    public event EventHandler<FrameCapturedEventArgs>? FrameCaptured;

    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    public event EventHandler<SerialErrorEventArgs>? LineErrorDetected;

    /// <summary>
    /// 打开两个端口。
    ///
    /// ⚠️ <b>任一路打不开即整体失败，且必须把已经打开的那一路关掉。</b>
    /// 不关掉的话端口被本进程占住，用户重试时撞「端口被占用」——
    /// <b>而占用者就是自己</b>，那是最难自查的一类错误。
    ///
    /// 失败时抛 <see cref="SerialPortOpenException"/>，**带上是哪一路**：
    /// 提示要出现在新建会话对话框里（「通道 B（COM7）打不开」），
    /// 光有底层异常说不出通道。
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (State == ConnectionState.Connected) return;

        SetState(ConnectionState.Connecting);

        await OpenOrThrowAsync(_portA, ChannelId.A, rollback: null, cancellationToken);
        await OpenOrThrowAsync(_portB, ChannelId.B, rollback: _portA, cancellationToken);

        lock (_sync)
        {
            // 两路断开前攒了一半的帧都不能与重连后的新数据接上。
            _splitterA.Reset();
            _splitterB.Reset();

            // 跨越一次断线的 Δ 没有意义（这里还是跨通道 Δ），首帧显示「无上一帧」。
            _previousFrameEndedAt = null;

            // ⚠️ **序号刻意不重置** —— 与终端会话同一条理由，见 P1-37。
        }

        // ⚠️ **原点只在首次连接时定** —— 与终端会话同一条理由，见 P1-37。
        StartedAt ??= _clock.Now;
        _flushCts = new CancellationTokenSource();
        _flushLoop = Task.Run(() => FlushLoopAsync(_flushCts.Token), CancellationToken.None);

        SetState(ConnectionState.Connected);
    }

    private async Task OpenOrThrowAsync(
        ISerialPort port, ChannelId channel, ISerialPort? rollback, CancellationToken cancellationToken)
    {
        try
        {
            await port.OpenAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            if (rollback is not null)
            {
                try
                {
                    await rollback.CloseAsync(CancellationToken.None);
                }
                catch (Exception rollbackFailure)
                {
                    // 回滚失败也要留痕，否则「端口被自己占住」将无从解释。
                    // 但不能向上抛 —— 那会盖掉真正的失败原因（本来那一路打不开）。
                    _logger.LogWarning(
                        rollbackFailure,
                        "Rolling back {PortName} after the other channel failed to open did not succeed; "
                        + "the port may stay busy in this process.",
                        rollback.PortName);
                }
            }

            SetState(ConnectionState.Faulted);
            throw new SerialPortOpenException(channel, port.PortName, ex);
        }
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

        // P2-18: close both ports concurrently.
        //
        // ⚠️ The original reason has since been dismantled, and the line stays for a smaller one.
        // In 2026-08-04 terms this saved ~2 seconds: closing a port that was not mid-read waited
        // out a 2-second read-loop stop timeout, twice. P2-62 removed that wait entirely on
        // 2026-08-05 (cancelling a pending serial read never worked; closing the port is what
        // releases it), so a single close is now 0.6-6.6 ms on a virtual pair and ~111 ms on real
        // FTDI hardware. Concurrency still halves the real-hardware case -- worth keeping, but it
        // is no longer the difference between "instant" and "the app hung".
        //
        // The two ports share no state (each has its own gate and read loop), so this is
        // safe as well as faster.
        //
        // ⭐ The local function is not decoration: it is `async`, so calling it cannot throw
        // synchronously. That is what makes the second port get closed even when the first
        // one fails -- with a plain `Task.WhenAll(_portA.CloseAsync(..), _portB.CloseAsync(..))`
        // a synchronous throw from the first argument would skip the second call entirely,
        // leaking that port. Structure, not a comment asking the next person to remember.
        await Task.WhenAll(Close(_portA), Close(_portB));

        // 关闭后把两个缓冲区里剩下的字节各作为最后一帧吐出，避免静默丢数据。
        EmitFrames(FlushRemaining());

        SetState(ConnectionState.Disconnected);

        async Task Close(ISerialPort port) => await port.CloseAsync(cancellationToken);
    }

    /// <summary>
    /// 向指定通道写入（M-09 注入）。
    ///
    /// ⚠️ 这是**真的往运行中的总线写数据**。是否允许由 App 层把关
    /// （发送默认禁用 + 注入风险确认对话框），本层只负责路由到正确的端口。
    /// </summary>
    public async Task SendAsync(
        ChannelId channel, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (State != ConnectionState.Connected || data.IsEmpty) return;

        var port = channel == ChannelId.B ? _portB : _portA;

        // 本地回显：注入的内容与观测到的内容进入同一条时间轴，
        // 否则用户看不出「刚才那一帧是我自己注入的」。
        //
        // ⛔ P2-59：与 TerminalCaptureSession.SendAsync 是同一条规则，理由见那里的长注释。
        // ⭐ **本处比终端那处更要紧**：监听会话的全部价值就是旁路观测总线时序，
        // 一个把注入显示在应答之后的时间轴，正好打在它存在的理由上。
        // ✅ 2026-08-04 在 COM5↔COM11 上实测复现过（Δ 显示 -1.9）。
        var now = _clock.Now;
        EmitFrames([CreateFrame(new FramedData(data.ToArray(), now, now), channel, FrameDirection.Tx)]);

        await port.WriteAsync(data, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _portA.DataReceived -= OnDataReceivedA;
        _portB.DataReceived -= OnDataReceivedB;
        _portA.ErrorReceived -= OnErrorReceivedA;
        _portB.ErrorReceived -= OnErrorReceivedB;

        await StopAsync();
        await _portA.DisposeAsync();
        await _portB.DisposeAsync();

        FrameCaptured = null;
        LineErrorDetected = null;
        StateChanged = null;
    }

    // ---- 两条读路径。刻意写成命名方法而非 lambda：要能退订。 ----

    private void OnDataReceivedA(object? sender, SerialChunkReceivedEventArgs e)
        => OnDataReceived(ChannelId.A, _splitterA, e);

    private void OnDataReceivedB(object? sender, SerialChunkReceivedEventArgs e)
        => OnDataReceived(ChannelId.B, _splitterB, e);

    private void OnDataReceived(ChannelId channel, IFrameSplitter splitter, SerialChunkReceivedEventArgs e)
    {
        List<SerialFrame>? frames = null;

        lock (_sync)
        {
            foreach (var framed in splitter.Append(e.Data, e.Timestamp))
            {
                (frames ??= []).Add(CreateFrameLocked(framed, channel, FrameDirection.Rx));
            }
        }

        EmitFrames(frames);
    }

    private void OnErrorReceivedA(object? sender, SerialErrorEventArgs e) => OnErrorReceived(e);

    private void OnErrorReceivedB(object? sender, SerialErrorEventArgs e) => OnErrorReceived(e);

    /// <summary>
    /// 任一路发生致命错误（最典型是设备被拔出）即整个会话 Faulted。
    ///
    /// <b>为什么不降级为单通道</b>：监听半条总线不是监听 ——
    /// 界面上另一路恒静默，用户分不清是设备没说话还是端口断了。
    /// </summary>
    private void OnErrorReceived(SerialErrorEventArgs e)
    {
        if (!e.IsFatal)
        {
            // P1-52: hardware line errors arrive here. They are not a state change -- the port
            // is open and the session keeps running -- so they go out on their own event.
            // The port has already throttled them to once per kind per connection.
            LineErrorDetected?.Invoke(this, e);
            return;
        }

        EmitFrames(FlushRemaining());

        // ⭐ P1-54 ring 5 -- same fix and same reasoning as TerminalCaptureSession: cancel the
        // flush loop at fault time, or StartAsync's unconditional `_flushCts = new ...` orphans
        // it on the next reconnect. Cancel only: no await (this is the read-loop thread), no
        // Dispose (the loop is still using the token).
        //
        // ⚠️ One monitor session has two ports but only ONE flush loop, so a fault on either
        // channel cancels it -- which is correct here, because either fault takes the whole
        // session to Faulted (see the summary above).
        _flushCts?.Cancel();

        SetState(ConnectionState.Faulted, e.Message, e.Kind);
    }

    /// <summary>
    /// 空闲刷新循环 —— <b>一个定时器轮询两个 splitter</b>。
    ///
    /// 空闲分帧只能在「下一块数据到达」时回溯判定上一帧结束，所以一段突发通讯的
    /// 最后一帧会滞留在缓冲区。两个通道各自都有这个问题，但**共用一个定时器**即可。
    ///
    /// ⚠️ 一次 tick 内若两路都有待收尾的帧，<b>先发 A 再发 B</b>，
    /// 与两者的时间戳无关 —— 这是「不做跨通道重排」的直接体现，不是疏漏。
    /// </summary>
    private async Task FlushLoopAsync(CancellationToken token)
    {
        // 两个通道的阈值通常相同（M-03 参数同步）；取较小者以免慢的那一路拖住快的。
        var gapMs = Math.Min(_splitterA.IdleGap.TotalMilliseconds, _splitterB.IdleGap.TotalMilliseconds);
        var period = TimeSpan.FromMilliseconds(Math.Clamp(
            gapMs, MinFlushPeriod.TotalMilliseconds, MaxFlushPeriod.TotalMilliseconds));

        using var timer = new PeriodicTimer(period);

        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                List<SerialFrame>? frames = null;

                lock (_sync)
                {
                    var now = _clock.Now;

                    if (_splitterA.FlushIfIdle(now) is { } a)
                    {
                        (frames ??= []).Add(CreateFrameLocked(a, ChannelId.A, FrameDirection.Rx));
                    }

                    if (_splitterB.FlushIfIdle(now) is { } b)
                    {
                        (frames ??= []).Add(CreateFrameLocked(b, ChannelId.B, FrameDirection.Rx));
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
        List<SerialFrame>? frames = null;

        lock (_sync)
        {
            if (_splitterA.Flush() is { } a)
            {
                (frames ??= []).Add(CreateFrameLocked(a, ChannelId.A, FrameDirection.Rx));
            }

            if (_splitterB.Flush() is { } b)
            {
                (frames ??= []).Add(CreateFrameLocked(b, ChannelId.B, FrameDirection.Rx));
            }
        }

        return frames;
    }

    private SerialFrame CreateFrame(FramedData framed, ChannelId channel, FrameDirection direction)
    {
        lock (_sync) return CreateFrameLocked(framed, channel, direction);
    }

    /// <summary>
    /// 组装一帧。调用方必须持有 <see cref="_sync"/>。
    ///
    /// ⭐ <b>Δ 与序号都取自跨通道共享的状态</b> —— 这就是「归并后的时间轴」。
    ///
    /// <b>Δ 用的是 C-1</b>：<c>本帧 StartedAt − 上一帧 EndedAt</c>，
    /// 即「上一帧发完 → 本帧开始」。跨通道时它就是**设备响应延迟**。
    /// <b>刻意不含帧自身的传输时间</b> —— 否则同一台设备在不同波特率、不同帧长下
    /// 读数会差好几倍，而设备行为根本没变（量化对照见 02-architecture 8.3）。
    ///
    /// ⚠️ <b>Δ 可能为负</b>（不重排，两路按到达顺序发出）。
    /// <see cref="TimeSpan"/> 天然支持负值，**刻意不钳零、不取绝对值** ——
    /// 负数本身就是「到达顺序与时间戳顺序不一致」的可观测信号。
    /// </summary>
    private SerialFrame CreateFrameLocked(FramedData framed, ChannelId channel, FrameDirection direction)
    {
        var origin = StartedAt ?? framed.StartedAt;

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
            Channel = channel,
            Direction = direction,
            Data = framed.Data
        };
    }

    /// <summary>
    /// The single funnel every emitted frame goes through. Deliberately <b>outside</b>
    /// <see cref="_sync"/> — see the class note on keeping subscriber cost out of the critical
    /// section. The negative-Δ log lives here for the same reason: it must not add file I/O to
    /// a lock two read loops contend for.
    /// </summary>
    private void EmitFrames(List<SerialFrame>? frames)
    {
        if (frames is null) return;

        foreach (var frame in frames)
        {
            // P2-15: make cross-channel reordering countable. Nothing is corrected here —
            // negative Δ is a deliberate, visible signal, see SessionLog.NegativeDelta.
            if (frame.Delta is { Ticks: < 0 } negative)
            {
                SessionLog.NegativeDelta(
                    _logger, frame.Sequence, frame.Channel, negative.TotalMilliseconds);
            }

            FrameCaptured?.Invoke(this, new FrameCapturedEventArgs(frame));
        }
    }

    private void SetState(
        ConnectionState state,
        string? message = null,
        SerialErrorKind errorKind = SerialErrorKind.Unknown)
    {
        if (State == state) return;

        // P1-11 -- same as TerminalCaptureSession. ⚠️ The target names BOTH ports: a monitor
        // session has one state for two channels, and a log line naming only one of them would
        // be read as "channel A faulted" when either channel takes the whole session down.
        var from = State;
        State = state;
        SessionLog.StateTransition(
            _logger, from, state, Kind, $"{_portA.PortName}+{_portB.PortName}");

        StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(state, message, errorKind));
    }
}
