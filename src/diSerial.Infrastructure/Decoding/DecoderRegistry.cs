using DiSerial.Core.Abstractions;

namespace DiSerial.Infrastructure.Decoding;

// Resolve protocol decoders.
public sealed class DecoderRegistry(IEnumerable<IProtocolDecoder> decoders) : IDecoderRegistry
{
    public IReadOnlyList<IProtocolDecoder> Decoders { get; } = decoders.ToArray();

    // Find a decoder by identifier.
    public IProtocolDecoder? Find(string id) =>
        Decoders.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
}
