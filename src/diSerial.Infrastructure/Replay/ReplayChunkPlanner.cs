namespace DiSerial.Infrastructure.Replay;

// Plan synthetic byte chunks.
public static class ReplayChunkPlanner
{
    // Plan replay byte delivery.
    public static IReadOnlyList<ReplayChunk> Plan(ReplayScript script, ReplayOptions options) =>
        options.Delivery switch
        {
            ReplayDelivery.Coalesced => PlanCoalesced(script, options),
            ReplayDelivery.Fragmented => PlanFragmented(script, options),
            _ => PlanPerFrame(script)
        };

    // Plan one chunk per replay frame.
    private static List<ReplayChunk> PlanPerFrame(ReplayScript script) =>
        [.. script.Steps.Select(s => new ReplayChunk(s.DelayBefore, s.Data, s.Fault))];

    // Plan merged replay chunks.
    private static List<ReplayChunk> PlanCoalesced(ReplayScript script, ReplayOptions options)
    {
        var chunks = new List<ReplayChunk>();
        var buffer = new List<byte>();

        var pendingDelay = TimeSpan.Zero;

        var windowElapsed = TimeSpan.Zero;

        foreach (var step in script.Steps)
        {
            if (step.Fault is not null)
            {
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
                windowElapsed = candidate;
                buffer.AddRange(step.Data);
                continue;
            }

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

    // Plan split replay chunks.
    private static List<ReplayChunk> PlanFragmented(ReplayScript script, ReplayOptions options)
    {
        var size = Math.Max(1, options.FragmentSize);
        var chunks = new List<ReplayChunk>();

        foreach (var step in script.Steps)
        {
            if (step.Fault is not null)
            {
                chunks.Add(new ReplayChunk(step.DelayBefore, step.Data, step.Fault));
                continue;
            }

            for (var offset = 0; offset < step.Data.Length; offset += size)
            {
                var length = Math.Min(size, step.Data.Length - offset);
                var slice = new byte[length];
                Array.Copy(step.Data, offset, slice, 0, length);

                chunks.Add(new ReplayChunk(
                    offset == 0 ? step.DelayBefore : options.FragmentGap,
                    slice));
            }
        }

        return chunks;
    }
}
