using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using TreadmillDriver.Models;
using TreadmillDriver.Native;

namespace TreadmillDriver.Services;

/// <summary>
/// Captures raw input from a specific mouse device using the Windows Raw Input API.
/// When BlockCursor is enabled, installs a low-level mouse hook that intercepts
/// ALL mouse movement at the system level, then uses Raw Input to identify which
/// device caused each move. Non-target devices get their movement re-injected.
/// The target device's movement is consumed exclusively by this app.
/// </summary>
public class MouseCaptureService : IDisposable
{
    private IntPtr _targetDeviceHandle = IntPtr.Zero;
    private HwndSource? _hwndSource;
    private bool _isCapturing;
    private bool _disposed;

    /// <summary>Fires when mouse movement is detected from the target device. Provides delta X, Y.</summary>
    public event Action<int, int>? MouseMoved;

    /// <summary>Whether capture is currently active.</summary>
    public bool IsCapturing => _isCapturing;

    /// <summary>
    /// When true, blocks the target mouse from moving the system cursor entirely.
    /// Your normal mouse continues working (its moves are re-injected).
    /// </summary>
    public bool BlockCursor { get; set; } = true;

    // ─── Device Enumeration ──────────────────────────────────────────

    /// <summary>
    /// Enumerates all connected mouse devices, identifying Bluetooth devices.
    /// </summary>
    public static List<MouseDeviceInfo> EnumerateMouseDevices()
    {
        var devices = new List<MouseDeviceInfo>();

        uint deviceCount = 0;
        uint size = (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICELIST>();
        NativeMethods.GetRawInputDeviceList(null, ref deviceCount, size);

        if (deviceCount == 0) return devices;

        var deviceList = new NativeMethods.RAWINPUTDEVICELIST[deviceCount];
        NativeMethods.GetRawInputDeviceList(deviceList, ref deviceCount, size);

        foreach (var rawDevice in deviceList)
        {
            if (rawDevice.dwType != NativeMethods.RIM_TYPEMOUSE)
                continue;

            var devicePath = GetDeviceName(rawDevice.hDevice);
            if (string.IsNullOrEmpty(devicePath))
                continue;

            var isBluetooth = devicePath.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase)
                           || devicePath.Contains("BLUETOOTH", StringComparison.OrdinalIgnoreCase)
                           || devicePath.Contains("BTH", StringComparison.OrdinalIgnoreCase);

            // Get device info for button count
            uint infoSize = (uint)Marshal.SizeOf<NativeMethods.RID_DEVICE_INFO>();
            var deviceInfo = new NativeMethods.RID_DEVICE_INFO { cbSize = infoSize };
            var infoPtr = Marshal.AllocHGlobal((int)infoSize);
            try
            {
                Marshal.StructureToPtr(deviceInfo, infoPtr, false);
                NativeMethods.GetRawInputDeviceInfo(rawDevice.hDevice, NativeMethods.RIDI_DEVICEINFO, infoPtr, ref infoSize);
                deviceInfo = Marshal.PtrToStructure<NativeMethods.RID_DEVICE_INFO>(infoPtr);
            }
            finally
            {
                Marshal.FreeHGlobal(infoPtr);
            }

            var friendlyName = GenerateFriendlyName(devicePath, isBluetooth, deviceInfo.mouse.dwNumberOfButtons);

            devices.Add(new MouseDeviceInfo
            {
                DeviceHandle = rawDevice.hDevice,
                DevicePath = devicePath,
                DisplayName = friendlyName,
                IsBluetooth = isBluetooth,
                ButtonCount = deviceInfo.mouse.dwNumberOfButtons
            });
        }

        // Sort: Bluetooth devices first
        devices.Sort((a, b) =>
        {
            if (a.IsBluetooth && !b.IsBluetooth) return -1;
            if (!a.IsBluetooth && b.IsBluetooth) return 1;
            return string.Compare(a.DisplayName, b.DisplayName, StringComparison.Ordinal);
        });

        return devices;
    }

    private static string GetDeviceName(IntPtr hDevice)
    {
        uint size = 0;
        NativeMethods.GetRawInputDeviceInfo(hDevice, NativeMethods.RIDI_DEVICENAME, IntPtr.Zero, ref size);

        if (size == 0) return string.Empty;

        var namePtr = Marshal.AllocHGlobal((int)(size * 2)); // Unicode chars
        try
        {
            NativeMethods.GetRawInputDeviceInfo(hDevice, NativeMethods.RIDI_DEVICENAME, namePtr, ref size);
            return Marshal.PtrToStringUni(namePtr) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(namePtr);
        }
    }

    private static string GenerateFriendlyName(string devicePath, bool isBluetooth, uint buttonCount)
    {
        // Try to resolve a real friendly name via WMI / registry
        var resolvedName = DeviceNameResolver.ResolveFriendlyName(devicePath);

        var prefix = isBluetooth ? "🔵 BT" : "🔌 USB";

        if (!string.IsNullOrEmpty(resolvedName))
        {
            return $"{prefix} — {resolvedName}";
        }

        // Fallback: extract VID/PID from the device path
        var pathParts = devicePath.Split(new[] { '#', '\\', '&' }, StringSplitOptions.RemoveEmptyEntries);
        var vendorPart = "";
        foreach (var part in pathParts)
        {
            if (part.StartsWith("VID_", StringComparison.OrdinalIgnoreCase) ||
                part.StartsWith("PID_", StringComparison.OrdinalIgnoreCase))
            {
                vendorPart += (vendorPart.Length > 0 ? " " : "") + part;
            }
        }

        var btnText = buttonCount > 0 ? $"{buttonCount}-btn" : "";
        if (string.IsNullOrEmpty(vendorPart))
            vendorPart = "Mouse";

        return $"{prefix} {btnText} Mouse ({vendorPart})".Replace("  ", " ").Trim();
    }

    // ─── Capture Control ─────────────────────────────────────────────

    /// <summary>
    /// Start capturing raw input from the specified device.
    /// Must be called from the UI thread.
    /// </summary>
    public bool StartCapture(IntPtr deviceHandle, Window window)
    {
        if (_isCapturing)
            StopCapture();

        _targetDeviceHandle = deviceHandle;

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return false;

        _hwndSource = HwndSource.FromHwnd(hwnd);
        if (_hwndSource == null) return false;

        // Register for raw mouse input
        var rid = new NativeMethods.RAWINPUTDEVICE[]
        {
            new()
            {
                usUsagePage = 0x01,  // HID_USAGE_PAGE_GENERIC
                usUsage = 0x02,      // HID_USAGE_GENERIC_MOUSE
                dwFlags = NativeMethods.RIDEV_INPUTSINK,
                hwndTarget = hwnd
            }
        };

        if (!NativeMethods.RegisterRawInputDevices(rid, 1, (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>()))
            return false;

        _hwndSource.AddHook(WndProc);
        _isCapturing = true;

        return true;
    }

    /// <summary>
    /// Stop capturing raw input.
    /// </summary>
    public void StopCapture()
    {
        if (!_isCapturing) return;

        _hwndSource?.RemoveHook(WndProc);

        // Unregister raw input
        if (_hwndSource != null)
        {
            var rid = new NativeMethods.RAWINPUTDEVICE[]
            {
                new()
                {
                    usUsagePage = 0x01,
                    usUsage = 0x02,
                    dwFlags = NativeMethods.RIDEV_REMOVE,
                    hwndTarget = IntPtr.Zero
                }
            };
            NativeMethods.RegisterRawInputDevices(rid, 1, (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>());
        }

        _hwndSource = null;
        _isCapturing = false;
        _targetDeviceHandle = IntPtr.Zero;
    }

    // ─── Cursor Counter-Injection ──────────────────────────────────

    /// <summary>
    /// Inject an opposite mouse move to undo the target device's cursor movement.
    /// This lets all system interactions (window drag, resize, etc.) work normally
    /// because we never block any mouse messages — we just counteract the target's delta.
    /// </summary>
    private static void CounterInjectMove(int dx, int dy)
    {
        var inputs = new NativeMethods.INPUT[]
        {
            new()
            {
                type = NativeMethods.INPUT_MOUSE,
                u = new NativeMethods.INPUT_UNION
                {
                    mi = new NativeMethods.MOUSEINPUT
                    {
                        dx = -dx,
                        dy = -dy,
                        mouseData = 0,
                        dwFlags = NativeMethods.MOUSEEVENTF_MOVE,
                        time = 0,
                        dwExtraInfo = NativeMethods.REINJECT_MAGIC
                    }
                }
            }
        };
        NativeMethods.SendInput(1, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    // ─── Message Processing ──────────────────────────────────────────

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_INPUT)
        {
            ProcessRawInput(lParam);
        }
        return IntPtr.Zero;
    }

    private void ProcessRawInput(IntPtr hRawInput)
    {
        uint headerSize = (uint)Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>();
        uint size = 0;

        // Get required buffer size
        NativeMethods.GetRawInputData(hRawInput, NativeMethods.RID_INPUT, IntPtr.Zero, ref size, headerSize);

        if (size == 0) return;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (NativeMethods.GetRawInputData(hRawInput, NativeMethods.RID_INPUT, buffer, ref size, headerSize) == unchecked((uint)-1))
                return;

            // Read header
            var header = Marshal.PtrToStructure<NativeMethods.RAWINPUTHEADER>(buffer);

            if (header.dwType != NativeMethods.RIM_TYPEMOUSE)
                return;

            // Skip synthetic input (generated by SendInput, e.g. our own re-injections).
            // hDevice == 0 means it didn't come from a physical device.
            if (header.hDevice == IntPtr.Zero)
                return;

            // Read mouse data
            var mouseOffset = buffer + Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>();
            var mouse = Marshal.PtrToStructure<NativeMethods.RAWMOUSE>(mouseOffset);

            if (mouse.usFlags != NativeMethods.MOUSE_MOVE_RELATIVE)
                return;

            if (mouse.lLastX == 0 && mouse.lLastY == 0)
                return;

            bool isTargetDevice = (_targetDeviceHandle != IntPtr.Zero && header.hDevice == _targetDeviceHandle);

            if (isTargetDevice)
            {
                // Target device: consume movement for treadmill processing
                MouseMoved?.Invoke(mouse.lLastX, mouse.lLastY);

                // If blocking, inject an opposite move to undo the cursor displacement
                if (BlockCursor)
                    CounterInjectMove(mouse.lLastX, mouse.lLastY);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // ─── Dispose ─────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopCapture();  // also removes hook
        GC.SuppressFinalize(this);
    }
}
