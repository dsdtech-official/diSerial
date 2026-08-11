namespace DiSerial.Core.Models;

/// <summary>
/// 解码器摘要所用的资源键常量。
///
/// 放在 Core 而非 App，是为了让<b>解码器</b>（Infrastructure 层）能引用，
/// 同时不引入任何本地化依赖 —— 键只是标识符，不是文本。
/// App 层的资源文件按这些键提供各语言的译文。
///
/// 命名约定：<c>Decoder.{协议}.{含义}</c>
///
/// V1.3 实现 Modbus 解码器时在此扩充完整键集（功能码名、各异常码释义、
/// 寄存器字段名等）。第三方插件无法向本应用资源追加条目，
/// 应改用 <see cref="LocalizableText.FromKey(string, string, object?[])"/>
/// 自带兜底文本。
/// </summary>
public static class DecoderKeys
{
    /// <summary>Modbus 异常码 02：非法数据地址。</summary>
    public const string ModbusIllegalDataAddress = "Decoder.Modbus.IllegalDataAddress";
}

/// <summary>
/// Resource keys for the frame rows themselves (P2-69).
///
/// <para><b>Here rather than in App's <c>LocKeys</c> for the same reason as
/// <see cref="DecoderKeys"/></b>: <c>FrameFormatter</c> lives in Infrastructure and must be able
/// to name these without taking a dependency on the App layer. A key is an identifier, not text.</para>
///
/// <para>⛔ <b>These four used to be string literals inside the row ViewModel</b>, where neither
/// text guard could see them: <c>TX</c> / <c>RX</c> are ASCII, so the <c>.cs</c> scanner (which
/// asks "is this literal CJK?") passed them, and they are not markup, so the <c>.axaml</c>
/// scanner never looked. See P2-69 / P2-70.</para>
/// </summary>
public static class FrameTextKeys
{
    /// <summary>Terminal session, received from the peer. No placeholder.</summary>
    public const string DirectionRx = "Frame.Direction.Rx";

    /// <summary>Terminal session, sent by us. No placeholder.</summary>
    public const string DirectionTx = "Frame.Direction.Tx";

    /// <summary>
    /// Monitor session, observed on the bus. <c>{0}</c> = the channel label.
    /// </summary>
    public const string ChannelObserved = "Frame.Channel.Observed";

    /// <summary>
    /// Monitor session, injected by us (M-09). <c>{0}</c> = the channel label.
    ///
    /// <para>⚠️ The <c>TX</c> marker leads deliberately (P1-32): an injected frame has to be
    /// recognisable at a glance, and a prefix breaks the column alignment, which is the point.</para>
    /// </summary>
    public const string ChannelInjected = "Frame.Channel.Injected";
}
