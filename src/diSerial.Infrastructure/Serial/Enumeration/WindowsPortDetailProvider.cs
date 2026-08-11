using System.Management;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Serial.Enumeration;

/// <summary>
/// Windows 平台的端口描述来源：WMI <c>Win32_PnPEntity</c>。
///
/// 这是整个解决方案中唯一引用 Windows 专有 API 的文件。
///
/// 取值示例：Caption = "Prolific USB-to-Serial Comm Port (COM3)"
/// 从尾部括号取端口名，去掉括号后的部分即为描述。
///
/// 注意这里<b>不再解析</b> PNPDeviceID 中的 VID/PID —— 设备识别已改为
/// 基于端口共现，与芯片厂商解耦，因此那部分解析连同正则一并删除了。
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsPortDetailProvider(ILogger? logger = null) : IPortDetailProvider
{
    private readonly ILogger? _logger = logger;

    public Task<IReadOnlyDictionary<string, string>> GetDescriptionsAsync(
        CancellationToken cancellationToken)
    {
        // WMI 查询是同步阻塞的（实测约 250ms），放到线程池执行以免卡住调用方。
        return Task.Run<IReadOnlyDictionary<string, string>>(Query, cancellationToken);
    }

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

                // 描述里去掉尾部的 " (COM3)"，避免与端口名重复显示。
                var description = PortSuffixRegex().Replace(caption, string.Empty).Trim();
                if (description.Length > 0) result[portName] = description;
            }
        }
        catch (Exception e)
        {
            // Any failure here degrades to "port names only", which costs nothing users
            // depend on -- the description is enrichment, the name is the functional part.
            //
            // ⭐ P1-46: this used to be a whitelist (ManagementException /
            // UnauthorizedAccessException), and the exception it missed was the one that
            // mattered. WMI throws COMException when the service is stopped or wedged;
            // that escaped through Task.Run into the 1.5s port-poll loop and killed it,
            // so the port dropdown stopped refreshing because a *description lookup*
            // failed. **The scope of the failure has to match the scope of the feature.**
            //
            // ⚠️ Debug, not Warning: this runs on every poll round, and WMI being
            // unavailable is a normal environment state, so a Warning would flood the log
            // and weaken it (03-conventions 8.0 / 8.2 (2); 01-spec 4.7 shared rule 1:
            // anything that can repeat goes to Debug). Turn logLevel up to debug to see it.
            _logger?.LogDebug(e, "WMI query for port descriptions failed; falling back to names only.");
        }

        return result;
    }

    private static string? ExtractPortName(string caption)
    {
        var open = caption.LastIndexOf('(');
        var close = caption.LastIndexOf(')');
        if (open < 0 || close < open) return null;

        var name = caption[(open + 1)..close].Trim();
        return name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ? name : null;
    }

    [GeneratedRegex(@"\s*\(COM\d+\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PortSuffixRegex();
}
