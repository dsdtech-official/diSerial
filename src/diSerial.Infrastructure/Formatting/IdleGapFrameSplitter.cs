using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;

namespace DiSerial.Infrastructure.Formatting;

/// <summary>
/// 基于空闲间隔的分帧实现（C-07）。纯逻辑，无平台依赖，可直接单元测试。
///
/// <b>完整的产品规格在 docs/01-spec.md 4.8</b> —— 本类只实现它，不重复定义它。
/// 判定规则：两次数据到达之间的间隔超过 <see cref="IdleGap"/>，
/// 即认为前一帧已结束。这正是 Modbus RTU 的 3.5 字符时间规则。
///
/// ⚠️ <b>本实现不能在一块数据内部再分帧</b> —— 一次 Append 只带一个时间戳，
/// 块内各字节的到达时刻不可知。若驱动存在攒帧行为（FTDI 默认延迟计时器
/// 16ms），多个协议帧会被合并成一帧。这是空闲分帧策略的固有限制（`Q-1`）。
///
/// <b>这条限制反过来成了一条不变量</b>（01-spec 4.8.3）：
/// <b>帧边界永远是块边界的子集 —— 每一帧都是若干完整块的连接。</b>
/// C-09 的两层落盘（Binary 记块 / Text 记帧）靠它才自洽：
/// Text 里的帧总能从 Binary 的块序列重建出来。<b>包括下面那个长度上限也不得破坏它。</b>
/// </summary>
public sealed class IdleGapFrameSplitter : IFrameSplitter
{
    /// <summary>
    /// 单帧字节数的<b>软上限</b>：攒到这么多就收尾，<b>但绝不切开一块</b>。
    /// 所以单帧最大约为本值 + 单块大小。
    ///
    /// <b>为什么需要它</b>：连续满速流下，块与块的间隔一直小于阈值，
    /// <see cref="Append"/> 不收尾；数据一直不停，<see cref="FlushIfIdle"/> 也不收尾 ——
    /// 缓冲区会<b>无界增长</b>。115200 连续吐十分钟约 7MB 攒在一个帧里，
    /// 而 <c>FrameFormatter</c> 会把它整个格式化成字符串。
    ///
    /// <b>为什么是软上限而不是硬上限</b>：硬上限会迫使在块内部切分，
    /// 而一次读返回只有一个时间戳 —— 同块内的多帧就得共享时间戳、Δ 之间恒为 0，
    /// 并且破坏上面那条「帧边界 ⊆ 块边界」的不变量。
    ///
    /// <b>4096 的理由</b>：远大于任何实际协议帧（Modbus RTU 上限 256 字节），
    /// 所以对真实协议几乎永不触发，只在连续流时起保护作用。
    /// 取舍是「误切风险」对「单行可读性」——<b>误切是错误，行太长只是难看。</b>
    ///
    /// ⚠️ <b>它不是分帧判据，是安全网。</b> 策略是「用户声明怎么解释数据」，
    /// 上限是「软件保证自己不失控」。别把它写成第二条策略。
    /// </summary>
    public const int MaxFrameLength = 4096;

    private readonly List<byte> _buffer = [];

    /// <summary>缓冲区中首字节所属数据块的到达时刻。</summary>
    private DateTimeOffset _pendingStartedAt;

    /// <summary>缓冲区中末字节所属数据块的到达时刻。</summary>
    private DateTimeOffset _pendingEndedAt;

    /// <summary>
    /// Idle threshold. <b>Deliberately without a default</b> — it falls to
    /// <see cref="TimeSpan.Zero"/>.
    ///
    /// <para>Per 01-spec 4.8.5, <c>Zero</c> is a <b>defined value</b>, not a missing one: the
    /// test is always true, so every chunk closes a frame immediately — "one row per chunk /
    /// no framing". That is the most conservative behaviour for a forgotten configuration:
    /// nothing is merged and the buffer cannot grow without bound.</para>
    ///
    /// <para>⚠️ This used to default to 4ms. That value matched no baud-rate derivation, and
    /// both <see cref="IdleGapFrameSplitterFactory"/> and the unit tests always assign
    /// explicitly — <b>measured: nobody depended on it</b> — so it was removed
    /// 2026-07-30.</para>
    ///
    /// <para>⚠️ <b>Changed from <c>set</c> to <c>init</c> on 2026-08-05 (P2-37)</b>; the
    /// reasoning is on <see cref="IFrameSplitter.IdleGap"/> — a session reads it once at
    /// start to size its flush timer.</para>
    /// </summary>
    public TimeSpan IdleGap { get; init; }

    public bool HasPending => _buffer.Count > 0;

    public IReadOnlyList<FramedData> Append(ReadOnlyMemory<byte> data, DateTimeOffset timestamp)
    {
        if (data.IsEmpty) return [];

        List<FramedData>? completed = null;

        // 收尾路径 1（01-spec 4.8.2）：与上一块的间隔超过阈值 → 缓冲区里的内容构成一个完整帧。
        // 注意结束时刻取 _pendingEndedAt（上一块的时刻），而不是当前 timestamp ——
        // 该帧确实在那个时刻就结束了，用当前时刻会把它记晚一个空闲间隔。
        if (_buffer.Count > 0 && timestamp - _pendingEndedAt >= IdleGap)
        {
            (completed ??= []).Add(TakeBuffered());
        }

        if (_buffer.Count == 0) _pendingStartedAt = timestamp;
        _buffer.AddRange(data.Span);
        _pendingEndedAt = timestamp;

        // 收尾路径 3：长度达标。刻意放在 AddRange 之后 ——
        // 先把整块收进来再判断，才不会切开一块（见 MaxFrameLength 的注释）。
        if (_buffer.Count >= MaxFrameLength)
        {
            (completed ??= []).Add(TakeBuffered());
        }

        return completed ?? (IReadOnlyList<FramedData>)[];
    }

    /// <summary>收尾路径 2：由外部定时器驱动，突发通讯的最后一帧靠它才出得来。</summary>
    public FramedData? FlushIfIdle(DateTimeOffset now)
    {
        if (_buffer.Count == 0) return null;
        if (now - _pendingEndedAt < IdleGap) return null;

        return Flush();
    }

    /// <summary>收尾路径 4：强制收尾（断开 / 设备拔出），把残留吐出，不静默丢数据。</summary>
    public FramedData? Flush()
    {
        if (_buffer.Count == 0) return null;

        return TakeBuffered();
    }

    public void Reset()
    {
        _buffer.Clear();
        _pendingStartedAt = default;
        _pendingEndedAt = default;
    }

    /// <summary>把缓冲区整个取走成一帧并清空。四条收尾路径共用，保证时间戳取法一致。</summary>
    private FramedData TakeBuffered()
    {
        var frame = new FramedData(_buffer.ToArray(), _pendingStartedAt, _pendingEndedAt);
        _buffer.Clear();
        return frame;
    }
}

/// <summary>
/// 按串口参数推算默认空闲阈值（Modbus RTU 的 3.5 字符时间）。
///
/// <paramref name="idleGapOverride"/> 非 null 时改用它 —— 那是 <c>diserial.dev.json</c>
/// 的 <c>idleGapMs</c> 键（01-spec 4.8.5 的诊断出口，**不是用户设置**）。
/// 解析在注册处完成，本类不依赖 <c>Diagnostics</c> —— 与
/// <c>ReplayConfiguration.From(developer)</c> 同一套路。
/// </summary>
public sealed class IdleGapFrameSplitterFactory(TimeSpan? idleGapOverride = null) : IFrameSplitterFactory
{
    public IFrameSplitter Create(SerialPortSettings settings) =>
        new IdleGapFrameSplitter { IdleGap = idleGapOverride ?? settings.DefaultIdleGap };
}
