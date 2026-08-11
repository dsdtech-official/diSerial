using DiSerial.Core.Models;

namespace DiSerial.Core.Abstractions;

/// <summary>
/// 把 <see cref="LocalizableText"/> 解析成当前界面语言的文本。
///
/// <b>依赖方向是刻意反过来的</b>：接口声明在 Core，由 Infrastructure（格式化、
/// 导出）<i>消费</i>，由 App 层<i>实现</i>。这样 Infrastructure 既能在日志与
/// 导出里输出可读文本，又不必知道有「界面语言」这回事。
///
/// App 未提供实现时可用 <c>FallbackTextResolver</c>（Infrastructure 内），
/// 它只返回兜底文本或键名 —— 适用于单元测试与将来的命令行模式。
/// </summary>
public interface ILocalizedTextResolver
{
    /// <summary>解析文本；入参为 null 时返回 null。</summary>
    string? Resolve(LocalizableText? text);
}
