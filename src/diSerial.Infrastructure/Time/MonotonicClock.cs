using System.Diagnostics;
using DiSerial.Core.Abstractions;

namespace DiSerial.Infrastructure.Time;

/// <summary>
/// 基于 <see cref="Stopwatch"/>（Windows 上为 QPC）的单调时钟实现。
///
/// 原理：进程启动时记录一次墙钟时刻与一次高精度计数值作为原点，
/// 之后所有取值都用「原点墙钟 + 高精度流逝量」计算。
/// 这样既拿到了亚微秒级分辨率，又不会因 NTP 校时导致时间回跳。
///
/// 典型分辨率：现代 x64 上 Stopwatch.Frequency 通常为 10MHz，即 0.1μs。
/// 但这只是<b>计时器</b>分辨率 —— 实际可观测的串口事件精度还受
/// USB 轮询周期与操作系统调度限制，通常在毫秒量级。
/// </summary>
public sealed class MonotonicClock : IMonotonicClock
{
    private readonly DateTimeOffset _origin = DateTimeOffset.Now;
    private readonly long _originTimestamp = Stopwatch.GetTimestamp();

    public DateTimeOffset Now => _origin + Stopwatch.GetElapsedTime(_originTimestamp);

    /// <summary>
    /// The timer's period, floored at one <see cref="TimeSpan"/> tick (100 ns).
    ///
    /// <b>Why the floor (P2-109, 2026-08-15)</b>: on macOS arm64
    /// <c>Stopwatch.Frequency</c> is 1 GHz, so one period is 1 ns -- ten times finer than
    /// a tick. <c>TimeSpan.FromSeconds(1e-9)</c> truncates to <b>zero</b>, and the startup
    /// banner then printed <c>0</c>, which reads as "infinitely precise". That is the exact
    /// misreading this value exists to prevent, so zero is the one answer it must never give.
    ///
    /// <b>⚠️ Same entry, second half</b>: the banner key was <c>clock.resolutionMs</c> and is
    /// now <c>clock.timerResolutionMs</c>. This number is the TIMER's period; what a user
    /// observes is set by their USB bridge and is orders of magnitude coarser. See
    /// <c>PlatformDiagnosticsBase.Collect</c> for why no observable figure is printed.
    ///
    /// <b>⚠️ The floor makes this pessimistic, on purpose.</b> On a 1 GHz timer the real
    /// period is 100x finer than what we report. Under-stating precision is safe here;
    /// over-stating it is not. Windows (10 MHz) is unaffected -- one period is exactly
    /// one tick, and the reported value is unchanged.
    ///
    /// <b>⛔ Not a macOS special case.</b> Any platform with
    /// <c>Stopwatch.Frequency &gt; 10 MHz</c> hits this; the code is shared and has no
    /// platform branch.
    /// </summary>
    public TimeSpan Resolution { get; } = ResolutionFor(Stopwatch.Frequency);

    /// <summary>
    /// The floor rule as a pure function of the timer frequency.
    ///
    /// <b>Public on purpose</b>: <c>Stopwatch.Frequency</c> is whatever the machine running
    /// the tests happens to have, so a guardrail cannot conjure a 1 GHz timer. Taking the
    /// frequency as an argument is what makes the macOS case testable on Windows -- the same
    /// reason <c>DisplayScalingCheck.DescribeMismatch</c> takes two doubles.
    /// </summary>
    public static TimeSpan ResolutionFor(long frequency)
    {
        if (frequency <= 0)
        {
            // Cannot happen on any supported platform, but returning TimeSpan.Zero here would
            // reintroduce the exact defect this method exists to prevent.
            return TimeSpan.FromTicks(1);
        }

        var ticks = (long)(TimeSpan.TicksPerSecond / (double)frequency);
        return TimeSpan.FromTicks(Math.Max(1, ticks));
    }
}
