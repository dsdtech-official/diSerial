namespace DiSerial.Infrastructure.Replay;

/// <summary>
/// 把回放脚本按指定投递方式展开成一串数据块。
///
/// 刻意做成<b>纯函数</b>：不涉及时间、线程与 I/O，因此可以直接单元测试。
/// <see cref="ReplaySerialPort"/> 只负责按规划结果定时送出，不含任何分块逻辑。
///
/// <para>⛔ <b>Fault steps pass through every delivery mode unchanged</b> (P2-36). The three
/// modes exist to reshape <b>byte boundaries</b> — coalescing merges buffers, fragmenting
/// slices them — and an <c>ErrorReceived</c> event has no byte boundary to reshape. Letting a
/// fault into a coalesce buffer would <b>silently delete it</b> (the buffer only holds bytes);
/// slicing one would raise it several times. So a fault step always becomes exactly one chunk,
/// in order, and in Coalesced mode it <b>flushes</b> whatever was pending first — the bytes
/// before a line error genuinely did arrive before it.</para>
/// </summary>
public static class ReplayChunkPlanner
{
    public static IReadOnlyList<ReplayChunk> Plan(ReplayScript script, ReplayOptions options) =>
        options.Delivery switch
        {
            ReplayDelivery.Coalesced => PlanCoalesced(script, options),
            ReplayDelivery.Fragmented => PlanFragmented(script, options),
            _ => PlanPerFrame(script)
        };

    private static List<ReplayChunk> PlanPerFrame(ReplayScript script) =>
        [.. script.Steps.Select(s => new ReplayChunk(s.DelayBefore, s.Data, s.Fault))];

    /// <summary>
    /// 攒帧：从窗口内第一帧开始计时，窗口期内到达的后续帧被并入同一块，
    /// 整块在最后一帧的时刻一次性交付。
    ///
    /// 这正是驱动延迟计时器的行为 —— 也正因如此，被并入同一块的几帧之间的
    /// 空闲间隔在接收端<b>不可观测</b>。
    /// </summary>
    private static List<ReplayChunk> PlanCoalesced(ReplayScript script, ReplayOptions options)
    {
        var chunks = new List<ReplayChunk>();
        var buffer = new List<byte>();

        // 本块第一帧之前需要等待的时间（相对上一块交付时刻）。
        var pendingDelay = TimeSpan.Zero;

        // 自本块第一帧起已累计的时间，用于判断是否仍在攒帧窗口内。
        var windowElapsed = TimeSpan.Zero;

        foreach (var step in script.Steps)
        {
            if (step.Fault is not null)
            {
                // Flush first: bytes buffered before a line error really did arrive before
                // it, and the buffer has nowhere to put an event. Then the fault stands
                // alone, carrying whatever wait the whole group had accumulated.
                if (buffer.Count > 0)
                {
                    chunks.Add(new ReplayChunk(pendingDelay + windowElapsed, buffer.ToArray()));
                    buffer.Clear();
                    pendingDelay = TimeSpan.Zero;
                    windowElapsed = TimeSpan.Zero;
                }

                chunks.Add(new ReplayChunk(pendingDelay + step.DelayBefore, step.Data, step.Fault));
                pendingDelay = TimeSpan.Zero;
                continue;
            }

            if (buffer.Count == 0)
            {
                pendingDelay = step.DelayBefore;
                windowElapsed = TimeSpan.Zero;
                buffer.AddRange(step.Data);
                continue;
            }

            var candidate = windowElapsed + step.DelayBefore;
            if (candidate <= options.CoalesceWindow)
            {
                // 仍在窗口内 —— 并入当前块。间隔信息就是在这一步丢失的。
                windowElapsed = candidate;
                buffer.AddRange(step.Data);
                continue;
            }

            // 超出窗口：先交付已攒的一块，本帧作为下一块的起点。
            chunks.Add(new ReplayChunk(pendingDelay + windowElapsed, buffer.ToArray()));
            buffer.Clear();
            pendingDelay = step.DelayBefore;
            windowElapsed = TimeSpan.Zero;
            buffer.AddRange(step.Data);
        }

        if (buffer.Count > 0)
        {
            chunks.Add(new ReplayChunk(pendingDelay + windowElapsed, buffer.ToArray()));
        }

        return chunks;
    }

    /// <summary>
    /// 分片：每帧拆成若干小块，块间只隔极短时间（远小于分帧阈值），
    /// 因此分帧器应当把它们重新拼回一帧。
    /// </summary>
    private static List<ReplayChunk> PlanFragmented(ReplayScript script, ReplayOptions options)
    {
        var size = Math.Max(1, options.FragmentSize);
        var chunks = new List<ReplayChunk>();

        foreach (var step in script.Steps)
        {
            if (step.Fault is not null)
            {
                // One chunk, never sliced -- slicing an event would raise it N times.
                chunks.Add(new ReplayChunk(step.DelayBefore, step.Data, step.Fault));
                continue;
            }

            for (var offset = 0; offset < step.Data.Length; offset += size)
            {
                var length = Math.Min(size, step.Data.Length - offset);
                var slice = new byte[length];
                Array.Copy(step.Data, offset, slice, 0, length);

                // 第一片承担该帧原本的等待时间；后续片之间只隔极小间隔。
                chunks.Add(new ReplayChunk(
                    offset == 0 ? step.DelayBefore : options.FragmentGap,
                    slice));
            }
        }

        return chunks;
    }
}
