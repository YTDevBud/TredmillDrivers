namespace TreadmillDriver.Models;

/// <summary>
/// Information about a detected mouse device.
/// </summary>
public class MouseDeviceInfo
{
    /// <summary>Raw input device handle.</summary>
    public IntPtr DeviceHandle { get; set; }

    /// <summary>Device path from the system (used to identify Bluetooth devices).</summary>
    public string DevicePath { get; set; } = string.Empty;

    /// <summary>Friendly display name for the device.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Whether this device is connected via Bluetooth.</summary>
    public bool IsBluetooth { get; set; }

    /// <summary>Number of buttons on the mouse.</summary>
    public uint ButtonCount { get; set; }

    public override string ToString() => DisplayName;
}
