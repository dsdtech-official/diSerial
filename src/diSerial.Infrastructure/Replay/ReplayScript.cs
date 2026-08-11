using DiSerial.Core.Models;

namespace DiSerial.Infrastructure.Replay;

/// <summary>
/// A line condition raised from a script instead of arriving from hardware (P2-36).
///
/// <para><b>Why this exists</b>: <see cref="ReplaySerialPort"/> could produce bytes but never
/// an error, so every fault path downstream — <c>P1-54</c>'s reconnect above all — could only
/// be reached by physically unplugging a cable. That needs a person present, and the scripts
/// that drive the UI steal focus from the message asking them to unplug (03-conventions
/// 0.6 ①). Fault paths were therefore the most expensive thing in this project to verify.</para>
///
/// <para>⛔ <b>The boundary, and it matters more than the feature</b>: a scripted fault
/// exercises <b>what happens after a fault</b> — state transitions, the red banner, what
/// reconnect does, how a recording batch is closed. It says <b>nothing</b> about
/// <i>when real hardware raises one</i>; that is driver and cable behaviour a fake cannot
/// supply. ⚠️ Nor does it reproduce physical disappearance — the port vanishing from
/// enumeration, a handle going invalid — which lives below this class entirely.</para>
///
/// <para>⭐ So: this makes P1-54's <b>fix</b> regressable. It does not make P1-54's
/// <b>reproduction</b> hardware-free. Treating a green replay run as "fault paths verified"
/// is the one use that would make this a liability rather than a tool.</para>
/// </summary>
/// <param name="Kind">User-facing classification, mapped to text by the App layer.</param>
/// <param name="Message">Developer diagnostics; goes to the log, not the UI.</param>
/// <param name="IsFatal">
/// True means the port is gone and the read loop stops — the replay loop ends here too,
/// exactly as <c>SystemIoSerialPort</c> returns from its read loop. A scripted fatal fault is
/// therefore always the last thing a script does.
/// </param>
/// <param name="Flags">Frame-level flags (parity / framing / overrun) for non-fatal faults.</param>
public sealed record ReplayFault(
    SerialErrorKind Kind,
    string Message,
    bool IsFatal = false,
    FrameFlags Flags = FrameFlags.None);

/// <summary>
/// 回放脚本中的一步：等待 <see cref="DelayBefore"/> 后送出 <see cref="Data"/>。
/// 一步代表协议层面的<b>一帧</b>；它实际被拆成几个数据块送出，由
/// <see cref="ReplayDelivery"/> 决定。
/// </summary>
public sealed record ReplayStep(TimeSpan DelayBefore, byte[] Data, string? Note = null)
{
    /// <summary>
    /// When set, this step raises <c>ErrorReceived</c> rather than being ordinary traffic
    /// (P2-36). <see cref="Data"/> is normally empty on such a step, but may carry bytes that
    /// arrive together with the condition.
    ///
    /// <para>⚠️ <b>A fault step is never coalesced or fragmented</b> — see
    /// <see cref="ReplayChunkPlanner"/>. Delivery modes reshape <i>byte boundaries</i>, and an
    /// event has none; merging one into a byte buffer would silently drop it.</para>
    /// </summary>
    public ReplayFault? Fault { get; init; }
}

/// <summary>一段可回放的串口流量。</summary>
public sealed record ReplayScript
{
    public required string Id { get; init; }

    /// <summary>端口列表中显示的说明文字。</summary>
    public required string Description { get; init; }

    public required IReadOnlyList<ReplayStep> Steps { get; init; }

    /// <summary>播完后是否从头循环。</summary>
    public bool Loop { get; init; } = true;
}

/// <summary>
/// 数据块的投递方式 —— 本工具最有价值的一个维度。
///
/// 真实驱动交付字节的<b>块边界与协议帧无关</b>，而分帧算法的正确性恰恰
/// 取决于它如何应对各种块边界。用假端口把这几种情况都造出来，
/// 就能在没有硬件的情况下检验分帧逻辑。
/// </summary>
public enum ReplayDelivery
{
    /// <summary>每帧一个数据块。理想驱动的行为，也是分帧最容易的情况。</summary>
    PerFrame,

    /// <summary>
    /// 模拟驱动的延迟计时器（FTDI 默认 16ms）：窗口期内的多帧被攒成一块交付。
    ///
    /// 这直接复现了路线图里标注的 PL2523 风险 —— 若驱动攒帧，
    /// 空闲间隔在块内不可观测，多个协议帧会被合并成一帧。
    /// 用这个模式可以在拿到硬件之前就看到后果。
    /// </summary>
    Coalesced,

    /// <summary>一帧被拆成多个小块交付。检验分帧器能否正确拼接。</summary>
    Fragmented
}

/// <summary>
/// 规划后的一次投递：等待 <see cref="DelayBefore"/> 后交付 <see cref="Data"/>。
/// <paramref name="Fault"/> 非 null 时，本次投递是一个错误事件而不是数据（P2-36）。
/// </summary>
public readonly record struct ReplayChunk(
    TimeSpan DelayBefore, byte[] Data, ReplayFault? Fault = null);

/// <summary>回放参数。</summary>
public sealed record ReplayOptions
{
    public ReplayDelivery Delivery { get; init; } = ReplayDelivery.PerFrame;

    /// <summary>Coalesced 模式的攒帧窗口。默认 16ms —— FTDI 延迟计时器的出厂值。</summary>
    public TimeSpan CoalesceWindow { get; init; } = TimeSpan.FromMilliseconds(16);

    /// <summary>Fragmented 模式下每块的字节数。</summary>
    public int FragmentSize { get; init; } = 3;

    /// <summary>Fragmented 模式下块与块之间的间隔。必须远小于分帧阈值，否则会被误判为帧边界。</summary>
    public TimeSpan FragmentGap { get; init; } = TimeSpan.FromMicroseconds(200);
}
