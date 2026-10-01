using System.Runtime.InteropServices;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using DiSerial.Infrastructure.Serial.Enumeration;
using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Serial;

// List available serial ports.
public sealed class SystemPortEnumerator : IPortEnumerator
{
    private readonly IPortNameSource _names;
    private readonly IPortDetailProvider _details;

    // Initialize port providers.
    public SystemPortEnumerator(ILoggerFactory? loggerFactory = null)
        : this(CreateNameSource(loggerFactory), loggerFactory)
    {
    }

    // Initialize port providers.
    internal SystemPortEnumerator(IPortNameSource names, ILoggerFactory? loggerFactory = null)
    {
        _names = names;
        _details = CreateDetailProvider(loggerFactory);
    }

    internal IPortNameSource NameSource => _names;

    // List available serial ports.
    public async Task<IReadOnlyList<SerialPortInfo>> GetPortsAsync(
        CancellationToken cancellationToken = default)
    {
        var names = _names.GetPortNames();
        var descriptions = await _details.GetDescriptionsAsync(cancellationToken);

        return names
            .Where(name => !DropCallinNodes || !IsMacOsCallinNode(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(NumericAwareKey, StringComparer.OrdinalIgnoreCase)
            .Select(name => new SerialPortInfo
            {
                PortName = name,
                Description = descriptions.GetValueOrDefault(name, string.Empty)
            })
            .ToArray();
    }

    private static bool DropCallinNodes => OperatingSystem.IsMacOS();

    // Detect a macOS call-in node.
    public static bool IsMacOsCallinNode(string portName)
    {
        if (string.IsNullOrEmpty(portName)) return false;

        var lastSlash = portName.LastIndexOf('/');
        var fileName = portName.AsSpan(lastSlash + 1);
        return fileName.StartsWith("tty.", StringComparison.Ordinal);
    }

    // Build a numeric sorting key.
    private static string NumericAwareKey(string portName)
    {
        var digitStart = portName.Length;
        while (digitStart > 0 && char.IsAsciiDigit(portName[digitStart - 1])) digitStart--;

        if (digitStart == portName.Length) return portName;

        var prefix = portName[..digitStart];
        var digits = portName[digitStart..];
        return $"{prefix}{digits.PadLeft(6, '0')}";
    }

    // Create the port name provider.
    private static IPortNameSource CreateNameSource(ILoggerFactory? loggerFactory)
        => SelectNameSource(OperatingSystem.IsLinux(), loggerFactory);

    // Choose the port name provider.
    internal static IPortNameSource SelectNameSource(bool isLinux, ILoggerFactory? loggerFactory)
        => isLinux
            ? new LinuxPortNameSource(loggerFactory?.CreateLogger(nameof(LinuxPortNameSource)))
            : new BclPortNameSource();

    // Create the port detail provider.
    private static IPortDetailProvider CreateDetailProvider(ILoggerFactory? loggerFactory)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WindowsPortDetailProvider(
                loggerFactory?.CreateLogger(nameof(WindowsPortDetailProvider)));

        return new NullPortDetailProvider();
    }
}
