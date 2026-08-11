using System.Globalization;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiSerial.App.Localization;

/// <summary>
/// <see cref="ILocalizedTextResolver"/> 的资源文件实现。
///
/// 这是「下层只声明说什么、App 层决定用哪种语言说」这条分层约定的落点，
/// 与 <see cref="PlatformStatusPresenter"/> 处理平台状态码是同一套路。
///
/// 解析顺序：
///   1. 资源键存在 → 用当前语言的模板格式化
///   2. 键不存在但带兜底文本 → 用兜底文本（供第三方解码器插件）
///   3. 都没有 → 返回键名，便于开发期立刻发现漏配
/// </summary>
public sealed class LocalizedTextResolver(
    ILocalizationService localization,
    ILogger<LocalizedTextResolver>? logger = null) : ILocalizedTextResolver
{
    private readonly ILogger _logger = logger ?? NullLogger<LocalizedTextResolver>.Instance;

    public string? Resolve(LocalizableText? text)
    {
        if (text is null) return null;

        if (text.HasKey && localization.Find(text.Key) is { } template)
        {
            return Format(template, text.Args);
        }

        if (!string.IsNullOrEmpty(text.Fallback))
        {
            return Format(text.Fallback, text.Args);
        }

        // 漏配资源时暴露键名，而不是显示空白 —— 空白会被误认为「本来就没有摘要」。
        return text.HasKey ? $"!{text.Key}!" : null;
    }

    /// <summary>
    /// 参数格式化固定走 InvariantCulture。
    /// 摘要里的参数多是从站号、寄存器地址、字节数这类数值 ——
    /// 它们的写法不应随界面语言改变，理由同 FrameFormatter。
    /// </summary>
    private string Format(string template, IReadOnlyList<object?> args)
    {
        if (args.Count == 0) return template;

        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, [.. args]);
        }
        catch (FormatException e)
        {
            // 译文里占位符写错时不崩溃，直接暴露模板便于定位。
            //
            // ⚠️ 记 Debug：解码摘要每帧都会走这里，同一个坏模板会连续触发。
            // 结果显眼（界面上直接显示模板原文），不靠日志也能发现。
            // 判据见 01-spec 4.7 共有第 1 条：会重复触发的记 Debug。
            _logger.LogDebug(e, "Format failed for template {Template}; showing it as-is.", template);
            return template;
        }
    }
}
