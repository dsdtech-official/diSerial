using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiSerial.Infrastructure.Formatting;

/// <summary>
/// 不做真正翻译的解析器：优先取兜底文本，没有则退回键名。
///
/// 用途有二：
///   - 单元测试 —— 无需拉起整个本地化子系统即可测试格式化逻辑
///   - 将来的命令行 / 无头模式（V2.0）—— 没有界面语言这一概念
///
/// 正式的界面运行时由 App 层注册基于资源文件的实现覆盖它。
/// 刻意<b>不</b>在 Infrastructure 的 DI 里自动注册 —— 若 App 忘了提供实现，
/// 应当在启动时立刻失败，而不是悄悄显示一堆资源键名。
/// </summary>
public sealed class FallbackTextResolver(ILogger<FallbackTextResolver>? logger = null)
    : ILocalizedTextResolver
{
    /// <summary>可为 null：本类的主要用途就是单元测试，那里不搭日志管线。</summary>
    private readonly ILogger _logger = logger ?? NullLogger<FallbackTextResolver>.Instance;

    public string? Resolve(LocalizableText? text)
    {
        if (text is null) return null;
        if (!string.IsNullOrEmpty(text.Fallback)) return Format(text.Fallback, text.Args);
        return text.HasKey ? text.Key : null;
    }

    private string Format(string template, IReadOnlyList<object?> args)
    {
        if (args.Count == 0) return template;

        try
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture, template, [.. args]);
        }
        catch (FormatException e)
        {
            // ⚠️ 记 Debug：同一个坏模板每次格式化都会抛，会重复触发。
            // 判据见 01-spec 4.7 共有第 1 条。
            _logger.LogDebug(e, "Format failed for template {Template}; showing it as-is.", template);
            return template;
        }
    }
}
