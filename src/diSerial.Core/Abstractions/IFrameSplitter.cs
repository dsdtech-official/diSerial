using DiSerial.Core.Models;

namespace DiSerial.Core.Abstractions;

/// <summary>
/// 一个已完成分帧的字节块，带自身的起止时刻。
///
/// ⚠️ 为什么必须带时间戳：早期版本的 Append 只返回裸字节，调用方拿到帧时
/// 手上只有「当前这块数据」的时间戳，而该帧实际结束于更早的时刻 ——
/// 结果是每一帧的时间戳都被系统性地记晚了一个空闲间隔。
/// 由于监听会话的 Δms（响应延迟）正是基于这个时间戳计算，
/// 该偏差会直接污染产品的核心卖点。
/// </summary>
public readonly record struct FramedData(
    ReadOnlyMemory<byte> Data,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt)
{
    /// <summary>本帧自身的传输耗时。</summary>
    public TimeSpan Duration => EndedAt - StartedAt;

    public int Length => Data.Length;
}

/// <summary>
/// 从连续字节流中切分出协议帧（C-07）。
///
/// 串口数据按驱动缓冲块到达，边界与协议帧无关。若不分帧，显示区会粘连成
/// 一整片十六进制，无法阅读。
///
/// 已知风险：USB 转串口驱动的延迟计时器（FTDI 默认 16ms）可能把多个帧攒
/// 在一次上报中，使空闲间隔在块内不可观测。实测 PL2523 的上报颗粒度后，
/// 若确认存在攒帧，需改用「按长度 + 协议特征」的分帧策略 ——
/// 届时只需新增一个本接口的实现，调用方不受影响。
/// </summary>
public interface IFrameSplitter
{
    /// <summary>
    /// Idle threshold. A gap longer than this ends the previous frame.
    ///
    /// <para>⚠️ <b>Construction-time only (<c>init</c>; was <c>set</c> until 2026-08-05, P2-37).</b>
    /// A session reads this <b>once, at start</b>, to compute the <see cref="FlushIfIdle"/>
    /// timer period. Writing it later changed the framing rule while leaving that period
    /// alone: the two silently disagreed from then on, and <b>nothing anywhere reported
    /// it</b>.</para>
    ///
    /// <para>⭐ <b>Why <c>init</c> rather than a contract note saying "restart the session
    /// after changing this"</b>: nobody writes it at run time. Every assignment in the
    /// repository — <c>IdleGapFrameSplitterFactory.Create</c> and the unit tests — is an
    /// object initializer, so <c>init</c> costs no call site and the compiler now enforces
    /// what a note would only have asked for (03-conventions section 1).</para>
    /// </summary>
    TimeSpan IdleGap { get; init; }

    /// <summary>缓冲区中是否有尚未成帧的数据。</summary>
    bool HasPending { get; }

    /// <summary>
    /// 喂入一次读取到的数据块。
    ///
    /// 返回值是列表而非单个帧：按空闲间隔分帧时一次最多完成一帧，
    /// 但 V1.2 计划增加的「按行结束符」策略可在一块数据中切出多帧。
    /// </summary>
    IReadOnlyList<FramedData> Append(ReadOnlyMemory<byte> data, DateTimeOffset timestamp);

    /// <summary>
    /// 若缓冲区已静默超过 <see cref="IdleGap"/>，结束并返回当前帧；否则返回 null。
    ///
    /// ⚠️ 这是必须由外部定时调用的方法。空闲分帧只能在「下一块数据到达」时
    /// 回溯地判定上一帧结束，因此一段突发通讯的<b>最后一帧</b>会一直滞留在
    /// 缓冲区里 —— 对 Modbus 这种一问一答的协议，表现就是应答帧不显示，
    /// 直到下一次请求到来。调用方必须以定时器补上这一触发。
    /// </summary>
    FramedData? FlushIfIdle(DateTimeOffset now);

    /// <summary>无条件结束当前帧。用于停止捕获时清空缓冲。</summary>
    FramedData? Flush();

    void Reset();
}

/// <summary>分帧策略工厂，便于 V1.2 增加「按行结束符」「固定长度」等策略。</summary>
public interface IFrameSplitterFactory
{
    IFrameSplitter Create(SerialPortSettings settings);
}
