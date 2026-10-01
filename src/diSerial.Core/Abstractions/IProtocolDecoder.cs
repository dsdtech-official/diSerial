using DiSerial.Core.Models;

namespace DiSerial.Core.Abstractions;

public interface IProtocolDecoder
{
    string Id { get; }

    string DisplayName { get; }

    // Decode a captured serial frame.
    DecodeResult? Decode(SerialFrame frame);
}

// Hold decoded frame content.
public sealed record DecodeResult
{
    public required LocalizableText Summary { get; init; }

    public IReadOnlyList<DecodedField> Fields { get; init; } = [];

    public LocalizableText? Error { get; init; }

    public bool IsError => Error is not null;
}

// Describe a decoded field.
public sealed record DecodedField(LocalizableText Name, string Value, int Offset, int Length);

public interface IDecoderRegistry
{
    IReadOnlyList<IProtocolDecoder> Decoders { get; }

    // Find a decoder by identifier.
    IProtocolDecoder? Find(string id);
}
