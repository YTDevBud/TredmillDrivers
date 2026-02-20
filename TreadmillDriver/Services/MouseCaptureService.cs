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
    private IntPtr _hookHandle = IntPtr.Zero;
    private NativeMethods.LowLevelMouseProc? _hookDelegate;

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

        // Install low-level mouse hook to block target device button/wheel events
        _hookDelegate = MouseHookCallback;
        _hookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL,
            _hookDelegate,
            NativeMethods.GetModuleHandle(null),
            0);

        return true;
    }

    /// <summary>
    /// Stop capturing raw input.
    /// </summary>
    public void StopCapture()
    {
        if (!_isCapturing) return;

        // Remove low-level mouse hook
        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
            _hookDelegate = null;
        }

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

    // ─── Low-Level Mouse Hook ────────────────────────────────────────

    /// <summary>
    /// LL hook callback: when BlockCursor is enabled, eats all button/wheel events
    /// that don't carry our magic stamp. Non-target device buttons are re-injected
    /// from ProcessRawInput. Target device buttons are silently dropped.
    /// Movement (WM_MOUSEMOVE) always passes through — counter-injection handles it.
    /// </summary>
    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && BlockCursor && _isCapturing)
        {
            int msg = wParam.ToInt32();

            // Let movement through (counter-injection handles it separately)
            if (msg != NativeMethods.WM_MOUSEMOVE)
            {
                // For button/wheel events, check if it's our re-injection
                var hookStruct = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                if (hookStruct.dwExtraInfo != NativeMethods.REINJECT_MAGIC)
                {
                    // Eat it — will be re-injected for non-target devices via Raw Input
                    return (IntPtr)1;
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, (IntPtr)nCode, wParam, lParam);
    }

    /// <summary>
    /// Re-inject button/wheel events from a non-target device with our magic stamp,
    /// so the LL hook passes them through on the second time around.
    /// </summary>
    private static void ReInjectButtons(ushort buttonFlags, short wheelDelta)
    {
        // Standard buttons (left, right, middle) — can be combined in one SendInput call
        uint flags = 0;
        if ((buttonFlags & NativeMethods.RI_MOUSE_LEFT_BUTTON_DOWN) != 0)   flags |= NativeMethods.MOUSEEVENTF_LEFTDOWN;
        if ((buttonFlags & NativeMethods.RI_MOUSE_LEFT_BUTTON_UP) != 0)     flags |= NativeMethods.MOUSEEVENTF_LEFTUP;
        if ((buttonFlags & NativeMethods.RI_MOUSE_RIGHT_BUTTON_DOWN) != 0)  flags |= NativeMethods.MOUSEEVENTF_RIGHTDOWN;
        if ((buttonFlags & NativeMethods.RI_MOUSE_RIGHT_BUTTON_UP) != 0)    flags |= NativeMethods.MOUSEEVENTF_RIGHTUP;
        if ((buttonFlags & NativeMethods.RI_MOUSE_MIDDLE_BUTTON_DOWN) != 0) flags |= NativeMethods.MOUSEEVENTF_MIDDLEDOWN;
        if ((buttonFlags & NativeMethods.RI_MOUSE_MIDDLE_BUTTON_UP) != 0)   flags |= NativeMethods.MOUSEEVENTF_MIDDLEUP;
        if (flags != 0)
            SendButtonInput(flags, 0);

        // Side buttons need separate calls (different mouseData per button)
        if ((buttonFlags & NativeMethods.RI_MOUSE_BUTTON_4_DOWN) != 0)
            SendButtonInput(NativeMethods.MOUSEEVENTF_XDOWN, NativeMethods.XBUTTON1);
        if ((buttonFlags & NativeMethods.RI_MOUSE_BUTTON_4_UP) != 0)
            SendButtonInput(NativeMethods.MOUSEEVENTF_XUP, NativeMethods.XBUTTON1);
        if ((buttonFlags & NativeMethods.RI_MOUSE_BUTTON_5_DOWN) != 0)
            SendButtonInput(NativeMethods.MOUSEEVENTF_XDOWN, NativeMethods.XBUTTON2);
        if ((buttonFlags & NativeMethods.RI_MOUSE_BUTTON_5_UP) != 0)
            SendButtonInput(NativeMethods.MOUSEEVENTF_XUP, NativeMethods.XBUTTON2);

        // Wheel / horizontal wheel
        if ((buttonFlags & NativeMethods.RI_MOUSE_WHEEL) != 0)
            SendButtonInput(NativeMethods.MOUSEEVENTF_WHEEL, unchecked((uint)wheelDelta));
        if ((buttonFlags & NativeMethods.RI_MOUSE_HWHEEL) != 0)
            SendButtonInput(NativeMethods.MOUSEEVENTF_HWHEEL, unchecked((uint)wheelDelta));
    }

    private static void SendButtonInput(uint eventFlags, uint mouseData)
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
                        dx = 0, dy = 0,
                        mouseData = mouseData,
                        dwFlags = eventFlags,
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

            bool isTargetDevice = (_targetDeviceHandle != IntPtr.Zero && header.hDevice == _targetDeviceHandle);

            // ── Movement: consume target device deltas for treadmill processing ──
            if (mouse.usFlags == NativeMethods.MOUSE_MOVE_RELATIVE &&
                (mouse.lLastX != 0 || mouse.lLastY != 0) &&
                isTargetDevice)
            {
                MouseMoved?.Invoke(mouse.lLastX, mouse.lLastY);
                if (BlockCursor)
                    CounterInjectMove(mouse.lLastX, mouse.lLastY);
            }

            // ── Buttons: re-inject non-target device clicks (target's are dropped) ──
            if (BlockCursor && mouse.usButtonFlags != 0 && !isTargetDevice)
            {
                ReInjectButtons(mouse.usButtonFlags, (short)mouse.usButtonData);
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
