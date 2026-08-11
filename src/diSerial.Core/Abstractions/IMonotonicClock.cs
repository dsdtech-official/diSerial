namespace DiSerial.Core.Abstractions;

/// <summary>
/// 高分辨率单调时钟。
///
/// ⚠️ 为什么不能直接用 <c>DateTimeOffset.Now</c>：Windows 上系统时钟的
/// 更新周期约为 15.6ms。而 Modbus RTU 的典型响应延迟只有几毫秒 ——
/// 用系统时钟打点，Δms 列会大面积显示为 0 或 15.6 的倍数，完全不可用。
///
/// 本接口的实现以 <c>Stopwatch</c>（QPC）为基准计算流逝时间，再叠加到一个
/// 起始墙钟时刻上，既保证亚毫秒分辨率，又保证不受系统时间调整（NTP 校时、
/// 夏令时）影响而回跳。
///
/// <see cref="Resolution"/> 供 UI 如实标注实际可测精度 —— 产品文档已声明
/// 本软件提供的是软件级时间戳而非硬件级微秒时间戳，这里给出具体数值。
/// </summary>
public interface IMonotonicClock
{
    /// <summary>当前时刻。单调递增，不会因系统校时而回跳。</summary>
    DateTimeOffset Now { get; }

    /// <summary>底层计时器的分辨率。</summary>
    TimeSpan Resolution { get; }
}
