using System.Diagnostics;
using DiSerial.Core.Abstractions;

namespace DiSerial.Infrastructure.Time;

// Measure monotonic elapsed time.
public sealed class MonotonicClock : IMonotonicClock
{
    private readonly DateTimeOffset _origin = DateTimeOffset.Now;
    private readonly long _originTimestamp = Stopwatch.GetTimestamp();

    public DateTimeOffset Now => _origin + Stopwatch.GetElapsedTime(_originTimestamp);

    public TimeSpan Resolution { get; } = ResolutionFor(Stopwatch.Frequency);

    // Read clock resolution.
    public static TimeSpan ResolutionFor(long frequency)
    {
        if (frequency <= 0)
        {
            return TimeSpan.FromTicks(1);
        }

        var ticks = (long)(TimeSpan.TicksPerSecond / (double)frequency);
        return TimeSpan.FromTicks(Math.Max(1, ticks));
    }
}
