using DiSerial.Core.Models;

namespace DiSerial.Core.Abstractions;

/// <summary>
/// 协议解码器扩展点。
///
/// V1.0 不实现任何解码器，但接口先行定义，使显示层从一开始就带上
/// 解码摘要列位。V1.3 加入 Modbus RTU/ASCII 解码时无需改动显示层；
/// V2.0 开放插件 SDK 后，社区可贡献 DL/T645、IEC-101 等实现。
///
/// ⚠️ <b>所有用户可见文本一律用 <see cref="LocalizableText"/>，不得用 string。</b>
/// 解码器位于 Infrastructure 层，直接产出成品字符串会让领域层依赖界面语言，
/// 且译不了。V1.3 的 Modbus 解码器会产出大量功能码名、异常码释义、
/// 寄存器说明 —— 契约在写解码器之前定好，比事后返工便宜得多。
/// </summary>
public interface IProtocolDecoder
{
    /// <summary>唯一标识，如 "modbus-rtu"。用于配置持久化，不参与显示。</summary>
    string Id { get; }

    /// <summary>解码器名称，如 "Modbus RTU"。通常是协议名，一般不翻译。</summary>
    string DisplayName { get; }

    /// <summary>尝试解码一帧。无法解码时返回 null，显示层退回原始字节。</summary>
    DecodeResult? Decode(SerialFrame frame);
}

public sealed record DecodeResult
{
    /// <summary>
    /// 单行摘要，如资源键 <c>Decoder.Modbus.ReadHolding</c> 配合从站号、数量等参数。
    /// </summary>
    public required LocalizableText Summary { get; init; }

    /// <summary>字段级明细，供展开显示（V1.3 的请求-响应折叠视图）。</summary>
    public IReadOnlyList<DecodedField> Fields { get; init; } = [];

    /// <summary>协议错误说明（CRC 不符、异常码等）。无错误时为 null。</summary>
    public LocalizableText? Error { get; init; }

    public bool IsError => Error is not null;
}

/// <summary>
/// 解码出的一个字段。
/// <paramref name="Name"/> 是字段名，需要翻译；
/// <paramref name="Value"/> 是取值，通常是数字或十六进制串，不翻译。
/// </summary>
public sealed record DecodedField(LocalizableText Name, string Value, int Offset, int Length);

/// <summary>
/// 解码器注册表。新增解码器只需向 DI 容器注册 IProtocolDecoder 实现，
/// 无需改动任何既有代码 —— 这是本项目主要的插件式扩展点。
/// </summary>
public interface IDecoderRegistry
{
    IReadOnlyList<IProtocolDecoder> Decoders { get; }

    IProtocolDecoder? Find(string id);
}
