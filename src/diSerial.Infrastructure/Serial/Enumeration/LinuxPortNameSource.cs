using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Serial.Enumeration;

// Find Linux serial device nodes.
internal sealed class LinuxPortNameSource(ILogger? logger = null) : IPortNameSource
{
    private const string DevDirectory = "/dev";
    private const string ByIdDirectory = "/dev/serial/by-id";

    private static readonly string[] AttachedDeviceProbes = ["ttyUSB", "ttyACM", "ttyAMA"];

    // List serial port names.
    public string[] GetPortNames()
        => Select(
            new BclPortNameSource().GetPortNames(),
            Candidates(Entries(DevDirectory), Entries(ByIdDirectory), ResolveLink),
            File.Exists);

    // Check for an attached device.
    internal static bool IsAttachedDeviceNode(string path)
    {
        var name = Path.GetFileName(path);
        return AttachedDeviceProbes.Any(p => name.StartsWith(p, StringComparison.Ordinal));
    }

    // List candidate device nodes.
    internal static IEnumerable<string> Candidates(
        IEnumerable<string> devEntries,
        IEnumerable<string> byIdEntries,
        Func<string, string?> resolve)
    {
        foreach (var path in devEntries)
        {
            if (IsAttachedDeviceNode(path)) yield return path;
        }

        foreach (var link in byIdEntries)
        {
            var target = resolve(link);
            if (target is not null) yield return target;
        }
    }

    // Merge existing port nodes.
    internal static string[] Select(
        IEnumerable<string> bclNames,
        IEnumerable<string> candidates,
        Func<string, bool> nodeExists)
    {
        var names = new List<string>(bclNames);

        foreach (var path in candidates)
        {
            if (!nodeExists(path)) continue;
            if (names.Contains(path, StringComparer.Ordinal)) continue;

            names.Add(path);
        }

        return [.. names];
    }

    // List device directory items.
    private string[] Entries(string directory)
    {
        try
        {
            return Directory.Exists(directory)
                ? Directory.GetFileSystemEntries(directory)
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(
                ex, "Could not list {Directory} while enumerating serial ports; skipping it", directory);
            return [];
        }
    }

    // Resolve a device symbolic link.
    private string? ResolveLink(string link)
    {
        try
        {
            return File.ResolveLinkTarget(link, returnFinalTarget: true)?.FullName ?? link;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(ex, "Could not resolve {Link} while enumerating serial ports", link);
            return null;
        }
    }
}
