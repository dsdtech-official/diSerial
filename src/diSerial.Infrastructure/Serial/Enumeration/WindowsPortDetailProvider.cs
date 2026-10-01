using System.Management;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Serial.Enumeration;

// Read Windows port descriptions.
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsPortDetailProvider(ILogger? logger = null) : IPortDetailProvider
{
    private readonly ILogger? _logger = logger;

    // Read serial port descriptions.
    public Task<IReadOnlyDictionary<string, string>> GetDescriptionsAsync(
        CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyDictionary<string, string>>(Query, cancellationToken);
    }

    // Query Windows serial metadata.
    private Dictionary<string, string> Query()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Caption FROM Win32_PnPEntity WHERE Caption LIKE '%(COM%'");
            using var items = searcher.Get();

            foreach (var item in items)
            {
                using var device = item;
                if (device["Caption"] is not string caption || caption.Length == 0) continue;

                var portName = ExtractPortName(caption);
                if (portName is null) continue;

                var description = PortSuffixRegex().Replace(caption, string.Empty).Trim();
                if (description.Length > 0) result[portName] = description;
            }
        }
        catch (Exception e)
        {
            _logger?.LogDebug(e, "WMI query for port descriptions failed; falling back to names only.");
        }

        return result;
    }

    // Extract a COM port identifier.
    private static string? ExtractPortName(string caption)
    {
        var open = caption.LastIndexOf('(');
        var close = caption.LastIndexOf(')');
        if (open < 0 || close < open) return null;

        var name = caption[(open + 1)..close].Trim();
        return name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ? name : null;
    }

    // Match a COM port name suffix.
    [GeneratedRegex(@"\s*\(COM\d+\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PortSuffixRegex();
}
