using System.Diagnostics;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;

namespace DiSerial.Infrastructure.Replay;

/// <summary>
/// A fake port that replays serial traffic from a script — no hardware, no virtual serial
/// port driver.
///
/// <para><b>Why it exists</b>: everything above <see cref="ISerialPort"/> (framing → capture
/// session → idle flush → batched push → the display) is the real implementation, so
/// swapping the bottom-most byte source exercises all of it through real calls. The only
/// thing left uncovered is <see cref="Serial.SystemIoSerialPort"/> itself, and that needs
/// real hardware regardless — virtual-serial-port software cannot reproduce a target chip
/// driver's reporting granularity either. This is one of the uses ISerialPort was abstracted
/// for in the first place.</para>
///
/// <para>⛔ <b>What replay CANNOT exercise</b> (P2-36, written down 2026-08-05; true since
/// this class was written, and stated nowhere until then — in a file whose comments are
/// otherwise dense, which is exactly how a limit goes unnoticed):</para>
///
/// <list type="number">
///   <item><b>The send path.</b> <see cref="WriteAsync"/> discards its data and returns
///   success. Nothing observes what was written, so "the right bytes went out" and "nothing
///   went out when it shouldn't have" are both unverifiable here. The tool for those is
///   <c>tools/watch-port.ps1</c> against a real or virtual port.</item>
///
///   <item><b>When a fault actually occurs.</b> Faults can now be <i>scripted</i> — see
///   below — but a script says nothing about the conditions under which real hardware raises
///   one. ⚠️ Nor does it cover <b>physical disappearance</b>: the port leaving enumeration,
///   a handle going invalid. That happens below this class and still needs a real cable.</item>
/// </list>
///
/// <para>✅ <b>Faults are scriptable as of 2026-08-05</b> (P2-36's second half): a
/// <see cref="ReplayStep"/> may carry a <see cref="ReplayFault"/>, and this port raises
/// <see cref="ErrorReceived"/> for it — a fatal one ends the loop, exactly as
/// <c>SystemIoSerialPort</c> returns from its read loop. <see cref="ReplayScenarios.LineFaults"/>
/// is the built-in one. ⭐ <b>What that buys</b>: everything downstream of the event —
/// state transitions, the red banner, reconnect ([P1-54]), how a recording batch closes —
/// becomes reproducible and automatable without hardware.</para>
///
/// <para>⛔ <b>And the line that must not be crossed</b>: this makes a fix <b>regressable</b>;
/// it does not make a defect's <b>reproduction</b> hardware-free. A green replay run is not
/// evidence that fault handling works against real hardware — see <see cref="ReplayFault"/>,
/// where the full boundary is written out.</para>
///
/// <para>⭐ The practical consequence: replay verifies <b>the receive path, plus post-fault
/// behaviour, under a script</b>. Reading it as "the session works" still over-claims by the
/// two areas above.</para>
/// </summary>
public sealed class ReplaySerialPort : ISerialPort
{
    /// <summary>
    /// 小于该阈值的等待改用自旋。
    ///
    /// Windows 上 Task.Delay 的实际精度约 15ms，而 Modbus 的请求→应答间隔
    /// 只有几毫秒 —— 直接用 Task.Delay 会把 4.2ms 拉成 15ms，
    /// Δms 列失真，这个工具也就失去了意义。
    /// 代价是回放期间占用部分 CPU；对开发期工具是可接受的取舍。
    /// </summary>
    private static readonly TimeSpan SpinThreshold = TimeSpan.FromMilliseconds(20);

    private readonly ReplayScript _script;
    private readonly ReplayOptions _options;
    private readonly IMonotonicClock _clock;
    private readonly IReadOnlyList<ReplayChunk> _chunks;

    private CancellationTokenSource? _cts;
    private Task? _loop;

    public ReplaySerialPort(
        string portName,
        SerialPortSettings settings,
        ReplayScript script,
        ReplayOptions options,
        IMonotonicClock clock)
    {
        PortName = portName;
        Settings = settings;
        _script = script;
        _options = options;
        _clock = clock;
        _chunks = ReplayChunkPlanner.Plan(script, options);
    }

    public string PortName { get; }

    public SerialPortSettings Settings { get; }

    public bool IsOpen { get; private set; }

    public event EventHandler<SerialChunkReceivedEventArgs>? DataReceived;

    /// <summary>
    /// Raised for any <see cref="ReplayStep"/> carrying a <see cref="ReplayFault"/>.
    ///
    /// <para>⚠️ <b>This had no producer at all until 2026-08-05</b> and sat under a
    /// <c>#pragma warning disable CS0067</c>. ⭐ That suppression is worth remembering: it is
    /// what let "replay cannot reach any fault path" stay invisible for months — the compiler
    /// noticed the event was never raised and was told to be quiet, which is precisely the
    /// shape 03-conventions section 1 is about.</para>
    /// </summary>
    public event EventHandler<SerialErrorEventArgs>? ErrorReceived;

    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        if (IsOpen) return Task.CompletedTask;

        IsOpen = true;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => ReplayLoopAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_cts is not { } cts) { IsOpen = false; return; }

        await cts.CancelAsync();
        if (_loop is not null)
        {
            try { await _loop; }
            catch (OperationCanceledException) { }
        }

        cts.Dispose();
        _cts = null;
        _loop = null;
        IsOpen = false;
    }

    /// <summary>
    /// A replay port is not wired to anything, so written data is <b>discarded</b>.
    ///
    /// <para>Deliberately not looped back: a terminal session's local echo already comes from
    /// <c>TerminalCaptureSession</c> emitting its own Tx frame, so echoing here would show
    /// the same content twice.</para>
    ///
    /// <para>⛔ <b>Verification limit</b> (P2-36): this succeeds unconditionally and records
    /// nothing, so no replay-based test can tell "the right bytes were sent" from "nothing
    /// was sent". Use a real or virtual port with <c>tools/watch-port.ps1</c> for that. See
    /// the class summary.</para>
    /// </summary>
    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <summary>
    /// ⭐ <b>Always <see cref="SerialControlLines.Unknown"/> — and that is the honest answer,
    /// not a stub</b> (T-07, spec 4.15).
    ///
    /// <para>A replay port has no wires. Returning <c>Low</c> for all three would put three
    /// confident grey dots on screen asserting that CTS, DSR and DCD are not asserted, about a
    /// cable that does not exist. <b>That is the class of defect this project ranks above a
    /// crash</b> (00-STATUS, advancement layer 1) — and it is precisely why
    /// <see cref="ControlLineState"/> has a third state instead of being a <c>bool</c>.</para>
    ///
    /// <para>⚠️ <b>Verification limit, same family as <see cref="WriteAsync"/>'s</b> (P2-36):
    /// no replay-based test can exercise a control line changing level. The tool for that is a
    /// virtual pair — <c>tools\signals.ps1</c> drives DTR/RTS on one end and reads CTS/DSR/DCD
    /// on the other, which is how T-07's three states were produced without hardware.</para>
    /// </summary>
    public SerialControlLines ReadControlLines() => SerialControlLines.Unknown;

    /// <summary>
    /// Accepted and discarded — there is no line to drive (T-07, spec 4.15).
    ///
    /// <para><b>Not a throw.</b> The panel is bound to whatever port the session holds, and a
    /// replay session is a legitimate session; making the checkbox raise would turn a supported
    /// configuration into an error dialog. The checkbox reflects what the user asked for, and
    /// the three indicators next to it already say <c>Unknown</c> — together those state the
    /// situation correctly without inventing a failure.</para>
    /// </summary>
    public Task SetOutputLineAsync(
        SerialOutputLine line, bool asserted, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;


    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        DataReceived = null;
    }

    private async Task ReplayLoopAsync(CancellationToken token)
    {
        try
        {
            do
            {
                foreach (var chunk in _chunks)
                {
                    await WaitAsync(chunk.DelayBefore, token);
                    if (token.IsCancellationRequested) return;

                    // 与真实实现保持一致：时间戳在数据「到达」的第一时间打点。
                    if (chunk.Data.Length > 0)
                    {
                        DataReceived?.Invoke(
                            this, new SerialChunkReceivedEventArgs(chunk.Data, _clock.Now));
                    }

                    if (chunk.Fault is not { } fault) continue;

                    // Bytes first, then the condition -- a line error is reported after the
                    // data that arrived with it, which is the order the real port produces.
                    ErrorReceived?.Invoke(this, new SerialErrorEventArgs(
                        fault.Flags, fault.Message, fault.IsFatal, fault.Kind));

                    if (!fault.IsFatal) continue;

                    // Fatal means the port is gone: SystemIoSerialPort returns from its read
                    // loop here rather than waiting for data that will never come, and a
                    // replay that kept going -- or looped back to the top -- would model a
                    // port that recovers by itself. No real one does.
                    IsOpen = false;
                    return;
                }
            }
            while (_script.Loop && !token.IsCancellationRequested);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// 混合等待：长间隔先用 Task.Delay 消耗掉大部分，剩余部分自旋。
    /// 这样既保住了毫秒级精度，又不至于把整段静默都用来烧 CPU。
    /// </summary>
    private async Task WaitAsync(TimeSpan delay, CancellationToken token)
    {
        if (delay <= TimeSpan.Zero) return;

        var start = Stopwatch.GetTimestamp();

        if (delay > SpinThreshold)
        {
            try
            {
                await Task.Delay(delay - SpinThreshold, token);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }

        while (Stopwatch.GetElapsedTime(start) < delay)
        {
            if (token.IsCancellationRequested) return;
            Thread.SpinWait(60);
        }
    }
}
