namespace DiSerial.Core.Models;

/// <summary>
/// 一个已完成分帧的数据单元（C-07 的输出）。
///
/// 时间戳精度说明：Timestamp 由采集线程在 Read() 返回的第一时间打点，
/// 但受 USB 轮询周期与操作系统调度影响，属软件级时间戳，
/// 不等同于硬件级微秒时间戳。UI 需向用户标明这一点。
/// </summary>
public sealed class SerialFrame
{
    /// <summary>会话内单调递增序号，用于稳定排序与定位。</summary>
    public required long Sequence { get; init; }

    /// <summary>采集线程打点的时间戳。</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Offset from the start of the session.
    ///
    /// <para>⚠️ <b>A derived value, deliberately frozen into the model and persisted</b>
    /// (P2-34, written down 2026-08-05). It is recomputable from <see cref="Timestamp"/> and
    /// the session start, so by the letter of "the domain model holds facts, not
    /// projections" it does not belong here.</para>
    ///
    /// <para>⭐ <b>Why it stays</b>: the display recomputes nothing per row. At the frame rates
    /// this tool targets, the timeline is re-rendered far more often than frames arrive, and
    /// a value computed once at capture is read thousands of times. <see cref="Delta"/> has a
    /// second reason on top: it is <b>cross-channel</b> in a monitor session, so the value
    /// depends on the merged ordering at the moment of capture — recomputing it later from a
    /// filtered or re-sorted view would produce a <i>different</i> number, not the same one
    /// more cheaply.</para>
    ///
    /// <para>⛔ The cost, stated plainly: both are persisted, so a change to how either is
    /// computed does not reach rows already in the database. That is a defensible trade, not
    /// an oversight — but it is the reason to think twice before redefining them.</para>
    /// </summary>
    public required TimeSpan Elapsed { get; init; }

    /// <summary>
    /// Time since the previous frame; null for the first. In a monitor session this is the
    /// observed response latency — the product's headline number.
    ///
    /// <para>⚠️ Derived and persisted for the same reasons as <see cref="Elapsed"/>, plus one
    /// of its own: <b>it is computed across channels at merge time</b>, so it is a fact about
    /// the capture, not a projection of this row.</para>
    /// </summary>
    public TimeSpan? Delta { get; init; }

    /// <summary>数据来源通道。终端会话恒为 None。</summary>
    public ChannelId Channel { get; init; } = ChannelId.None;

    public required FrameDirection Direction { get; init; }

    public required ReadOnlyMemory<byte> Data { get; init; }

    public FrameFlags Flags { get; init; } = FrameFlags.None;

    /// <summary>
    /// 解码器附加的说明（V1.3）。V1.0 除桩数据外恒为 null。
    ///
    /// 刻意是 <see cref="LocalizableText"/> 而非 string：产出它的解码器位于
    /// Infrastructure 层，该层不得输出已本地化的成品文本，
    /// 否则英文界面上会出现中文（这个问题真实发生过）。
    /// 由 App 层的 <see cref="Abstractions.ILocalizedTextResolver"/> 负责落地成文字。
    /// </summary>
    public LocalizableText? DecodedSummary { get; init; }

    public int Length => Data.Length;
}
