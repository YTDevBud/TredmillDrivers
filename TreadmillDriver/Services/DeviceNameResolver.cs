using System.Management;

namespace TreadmillDriver.Services;

/// <summary>
/// Resolves friendly device names from raw input device paths using WMI
/// and the Windows registry. Especially useful for Bluetooth mice.
/// </summary>
public static class DeviceNameResolver
{
    private static readonly Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolve a friendly name for a device given its Raw Input device path.
    /// Returns null if no friendly name can be found.
    /// </summary>
    public static string? ResolveFriendlyName(string devicePath)
    {
        if (string.IsNullOrEmpty(devicePath))
            return null;

        if (_cache.TryGetValue(devicePath, out var cached))
            return cached;

        var name = TryResolveViaWmi(devicePath)
                ?? TryResolveViaRegistry(devicePath);

        if (name != null)
            _cache[devicePath] = name;

        return name;
    }

    /// <summary>
    /// Query WMI for Bluetooth and PnP device names that match the device path.
    /// </summary>
    private static string? TryResolveViaWmi(string devicePath)
    {
        try
        {
            // Extract hardware IDs from the raw input path
            // Raw Input paths look like: \\?\HID#VID_XXXX&PID_XXXX#...
            // or for BT: \\?\HID#BTHENUM#Dev_XXXXXXXXXXXX...
            var instanceId = ConvertPathToInstanceId(devicePath);
            if (string.IsNullOrEmpty(instanceId))
                return null;

            // Query PnP entities for matching device
            using var searcher = new ManagementObjectSearcher(
                $"SELECT Name, Description FROM Win32_PnPEntity WHERE PNPDeviceID LIKE '%{EscapeWmiString(instanceId)}%'");

            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(name) && name != "HID-compliant mouse")
                    return name;

                var desc = obj["Description"]?.ToString();
                if (!string.IsNullOrWhiteSpace(desc) && desc != "HID-compliant mouse")
                    return desc;
            }

            // Also try searching Bluetooth devices specifically
            if (devicePath.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase) ||
                devicePath.Contains("BTH", StringComparison.OrdinalIgnoreCase))
            {
                return TryResolveBluetooth(devicePath);
            }
        }
        catch
        {
            // WMI can fail on restricted systems
        }

        return null;
    }

    /// <summary>
    /// Try to find the Bluetooth device name from WMI Bluetooth queries.
    /// </summary>
    private static string? TryResolveBluetooth(string devicePath)
    {
        try
        {
            // Extract Bluetooth MAC from device path if present
            // e.g., Dev_AABBCCDDEEFF or similar patterns
            var macAddress = ExtractBluetoothMac(devicePath);

            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DeviceID FROM Win32_PnPEntity WHERE Service = 'BTHPORT' OR DeviceID LIKE '%BTHENUM%'");

            foreach (ManagementObject obj in searcher.Get())
            {
                var devId = obj["DeviceID"]?.ToString() ?? "";
                var name = obj["Name"]?.ToString();

                // Match by MAC if we have one
                if (!string.IsNullOrEmpty(macAddress) && devId.Contains(macAddress, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(name))
                        return name;
                }
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Try the Windows registry to find a device friendly name.
    /// </summary>
    private static string? TryResolveViaRegistry(string devicePath)
    {
        try
        {
            // Convert device path to registry key path
            // \\?\HID#VID_046D&PID_B02A#... -> SYSTEM\CurrentControlSet\Enum\HID\VID_046D&PID_B02A\...
            var cleaned = devicePath
                .Replace("\\\\?\\", "")
                .Replace("#{", "")
                .TrimEnd('}');

            // Replace # with \
            var parts = cleaned.Split('#');
            if (parts.Length < 3) return null;

            var regPath = $"SYSTEM\\CurrentControlSet\\Enum\\{parts[0]}\\{parts[1]}\\{parts[2]}";

            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(regPath);
            if (key != null)
            {
                var friendlyName = key.GetValue("FriendlyName") as string;
                if (!string.IsNullOrWhiteSpace(friendlyName))
                    return friendlyName;

                var deviceDesc = key.GetValue("DeviceDesc") as string;
                if (!string.IsNullOrWhiteSpace(deviceDesc))
                {
                    // DeviceDesc format: "@driver.inf,...;Friendly Name" — extract the name part
                    var semicolonIdx = deviceDesc.LastIndexOf(';');
                    return semicolonIdx >= 0 ? deviceDesc[(semicolonIdx + 1)..].Trim() : deviceDesc;
                }
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Convert a Raw Input device path to a partial PnP instance ID for WMI matching.
    /// </summary>
    private static string ConvertPathToInstanceId(string devicePath)
    {
        // \\?\HID#VID_046D&PID_B02A#7&12345 -> VID_046D&PID_B02A
        var cleaned = devicePath.Replace("\\\\?\\", "").Replace("\\", "#");
        var parts = cleaned.Split('#', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length >= 2)
        {
            // Return the hardware ID part (VID_XXXX&PID_XXXX or BTHENUM identifier)
            return parts[1];
        }

        return string.Empty;
    }

    /// <summary>
    /// Extract a Bluetooth MAC address from a device path.
    /// </summary>
    private static string? ExtractBluetoothMac(string devicePath)
    {
        // Look for pattern like Dev_XXXXXXXXXXXX (12 hex chars)
        var upper = devicePath.ToUpperInvariant();
        var devIdx = upper.IndexOf("DEV_", StringComparison.Ordinal);
        if (devIdx >= 0 && devIdx + 16 <= upper.Length)
        {
            var mac = upper.Substring(devIdx + 4, 12);
            if (mac.All(c => "0123456789ABCDEF".Contains(c)))
                return mac;
        }

        return null;
    }

    private static string EscapeWmiString(string input)
    {
        return input.Replace("\\", "\\\\").Replace("'", "\\'").Replace("_", "[_]");
    }

    /// <summary>
    /// Clear the name resolution cache.
    /// </summary>
    public static void ClearCache() => _cache.Clear();
}
