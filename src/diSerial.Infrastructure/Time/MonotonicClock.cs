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

    public TimeSpan Resolution { get; } =
        TimeSpan.FromSeconds(1.0 / Stopwatch.Frequency);
}
