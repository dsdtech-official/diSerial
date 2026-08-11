using DiSerial.Infrastructure.Diagnostics;

namespace DiSerial.Infrastructure.Replay;

/// <summary>
/// 回放功能的开关与参数，来自 <c>diserial.dev.json</c>。
///
/// 用配置文件而非设置界面：这是开发期工具，不应出现在正式界面上，
/// 也不该被普通用户误开。
///
/// <code>
///   "replay": "off"          不启用（默认）
///   "replay": "on"           启用，按帧投递
///   "replay": "coalesced"    启用，模拟驱动攒帧（复现 PL2523 风险）
///   "replay": "fragmented"   启用，一帧拆成多块
///   "replayWindowMs": 16     攒帧窗口毫秒数，仅 coalesced 模式有效
/// </code>
///
/// ⚠️ <b>只在 <c>debugMode</c> 为 true 时生效</b>（把关在
/// <see cref="From"/> 内完成）。回放端口走的是<b>真实采集链路</b>，
/// 产出的帧在日志里与真硬件的帧无法区分 —— 不把关，
/// 「用户形态不产生模拟数据」就只是一句空话。
///
/// <b>为什么保留四种取值而不收成一个布尔</b>：<c>coalesced</c> 是没有硬件时
/// 唯一能预演「驱动攒帧会怎样」的手段，而那正是 <c>Q-1</c> ——
/// 挡着监听会话真实链路（P0-1）的那个待确认项。
/// </summary>
public static class ReplayConfiguration
{
    /// <summary>
    /// 从开发者配置读取；未启用、或处于用户形态时返回 null。
    /// </summary>
    /// <param name="developer">为 null 时按全关处理。</param>
    public static ReplayOptions? From(DeveloperOptions? developer)
    {
        var options = developer ?? DeveloperOptions.Disabled;

        // 把关：用户形态下 replay 的取值一律忽略，等同 off。
        // 这一句是「用户形态不产生模拟数据」这条产品承诺的落点，不要绕过。
        if (!options.DebugMode) return null;

        var raw = options.Replay;
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var value = raw.Trim();
        if (value.Equals("off", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("0", StringComparison.Ordinal) ||
            value.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var delivery = value.ToLowerInvariant() switch
        {
            "coalesced" => ReplayDelivery.Coalesced,
            "fragmented" => ReplayDelivery.Fragmented,
            _ => ReplayDelivery.PerFrame
        };

        var result = new ReplayOptions { Delivery = delivery };

        if (options.ReplayWindowMs > 0)
        {
            result = result with { CoalesceWindow = TimeSpan.FromMilliseconds(options.ReplayWindowMs) };
        }

        return result;
    }
}
