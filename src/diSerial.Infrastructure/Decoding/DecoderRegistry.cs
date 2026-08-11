using DiSerial.Core.Abstractions;

namespace DiSerial.Infrastructure.Decoding;

/// <summary>
/// 解码器注册表。
///
/// 通过构造函数注入全部已注册的 IProtocolDecoder，因此新增解码器
/// 只需在 DI 容器里 AddSingleton&lt;IProtocolDecoder, XxxDecoder&gt;()，
/// 本类与调用方均无需改动。
///
/// V1.0 容器中没有任何解码器实现，Decoders 为空集合，UI 的解码下拉框
/// 只显示「关闭」。V1.3 加入 Modbus 解码后自动出现在列表中。
/// </summary>
public sealed class DecoderRegistry(IEnumerable<IProtocolDecoder> decoders) : IDecoderRegistry
{
    public IReadOnlyList<IProtocolDecoder> Decoders { get; } = decoders.ToArray();

    public IProtocolDecoder? Find(string id) =>
        Decoders.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
}
