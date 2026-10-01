namespace DiSerial.Infrastructure.Serial.Enumeration;

internal interface IPortDetailProvider
{
    // Read serial port descriptions.
    Task<IReadOnlyDictionary<string, string>> GetDescriptionsAsync(CancellationToken cancellationToken);
}

// Provide no port descriptions.
internal sealed class NullPortDetailProvider : IPortDetailProvider
{
    // Read serial port descriptions.
    public Task<IReadOnlyDictionary<string, string>> GetDescriptionsAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
}
