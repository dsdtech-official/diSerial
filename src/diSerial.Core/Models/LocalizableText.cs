namespace DiSerial.Core.Models;

/// <summary>
/// 一段<b>尚未本地化</b>的文本：只携带资源键与参数，不含成品字符串。
///
/// <b>为什么需要它</b>：协议解码器位于 Infrastructure 层，而该层不得产出
/// 用户可见的本地化文本 —— 否则领域层会依赖界面语言状态，且无法翻译。
/// 早期实现里 <c>DecodeResult.Summary</c> 是 <c>string</c>，桩数据直接吐出
/// 「异常码 02：非法数据地址」，结果在英文界面上原样显示了中文。
///
/// 改为本类型后，解码器只声明「说什么」，由 App 层的
/// <see cref="Abstractions.ILocalizedTextResolver"/> 决定「用哪种语言说」。
///
/// <see cref="Fallback"/> 是给第三方解码器插件（V2.0）留的出口 ——
/// 它们无法向本应用的资源文件里追加条目，只能自带兜底文本。
/// </summary>
public sealed record LocalizableText
{
    private LocalizableText() { }

    /// <summary>资源键。为空表示直接使用 <see cref="Fallback"/>。</summary>
    public string Key { get; private init; } = string.Empty;

    /// <summary>格式化参数，对应资源值里的 {0}、{1}……</summary>
    public IReadOnlyList<object?> Args { get; private init; } = [];

    /// <summary>资源键不存在时的兜底文本。</summary>
    public string? Fallback { get; private init; }

    public bool HasKey => !string.IsNullOrEmpty(Key);

    /// <summary>按资源键构造。这是解码器应当优先使用的方式。</summary>
    public static LocalizableText FromKey(string key, params object?[] args) =>
        new() { Key = key, Args = args };

    /// <summary>
    /// 按资源键构造，并附带兜底文本。
    /// 供第三方插件使用：键在宿主资源中不存在时退回自带文本。
    /// </summary>
    public static LocalizableText FromKey(string key, string fallback, params object?[] args) =>
        new() { Key = key, Args = args, Fallback = fallback };

    /// <summary>
    /// 直接给出字面文本，不参与翻译。
    /// 仅用于本来就与语言无关的内容（寄存器值、设备型号串等）。
    /// </summary>
    public static LocalizableText FromLiteral(string text) =>
        new() { Fallback = text };

    /// <summary>
    /// <b>Diagnostic form only.</b> Deliberately not the displayable text.
    ///
    /// <para>⛔ <b>What this used to be, and why it changed</b> (P2-37, 2026-08-05): it
    /// returned <c>Fallback ?? Key</c>. That made <c>$"…{summary}…"</c> — any interpolation,
    /// any string concatenation, any <c>Console.WriteLine</c>, any structured-log argument —
    /// compile cleanly and produce something that <i>looks</i> like a message, while
    /// <b>silently bypassing localization entirely</b>. On a resource-key text it emitted the
    /// raw key; on a literal it emitted untranslatable text. Either way the user saw the
    /// wrong thing and nothing failed.</para>
    ///
    /// <para>⚠️ <b>The irony is the whole point</b>: preventing exactly that leak is this
    /// type's entire reason to exist — <c>DecodeResult.Summary</c> was a <c>string</c> once,
    /// and a Chinese stub message rendered verbatim on the English UI. The type fixed the
    /// declared path and left <c>ToString</c> as an unmarked back door.</para>
    ///
    /// <para>⭐ The output now names the type, so it can never be mistaken for user-facing
    /// text in a log or a screenshot. The only supported way to render this is
    /// <see cref="Abstractions.ILocalizedTextResolver.Resolve"/>.</para>
    ///
    /// <para>✅ Verified before changing: nothing in <c>src/</c> or <c>tests/</c> interpolated
    /// or otherwise called this — the resolvers read <see cref="Key"/> and
    /// <see cref="Fallback"/> directly.</para>
    /// </summary>
    public override string ToString() =>
        HasKey ? $"LocalizableText(key: {Key})" : $"LocalizableText(literal: {Fallback})";
}
