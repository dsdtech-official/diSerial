namespace DiSerial.Core.Models;

// Describe an available port.
public sealed record SerialPortInfo
{
    public required string PortName { get; init; }

    public string Description { get; init; } = string.Empty;

    public string DisplayName =>
        string.IsNullOrEmpty(Description) ? PortName : $"{PortName} — {Description}";

    // Make a filename-safe port name.
    public static string FileNameSegment(string? portName)
    {
        if (string.IsNullOrWhiteSpace(portName)) return string.Empty;

        var cut = portName.LastIndexOfAny(['/', '\\']);
        var segment = cut >= 0 ? portName[(cut + 1)..] : portName;

        var chars = segment.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (InvalidInFileName.Contains(chars[i]) || char.IsControl(chars[i]))
                chars[i] = '-';

        return new string(chars);
    }

    private const string InvalidInFileName = "/\\:*?\"<>|";
}
