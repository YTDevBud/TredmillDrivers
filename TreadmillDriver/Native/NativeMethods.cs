using System.Runtime.InteropServices;

namespace TreadmillDriver.Native;

/// <summary>
/// P/Invoke declarations for Windows Raw Input API, SendInput, and related functions.
/// </summary>
internal static class NativeMethods
{
    // ─── Constants ───────────────────────────────────────────────────

    public const int WM_INPUT = 0x00FF;
    public const int RID_INPUT = 0x10000003;
    public const int RIM_TYPEMOUSE = 0;
    public const int RIM_TYPEKEYBOARD = 1;
    public const int RIM_TYPEHID = 2;

    public const uint RIDEV_INPUTSINK = 0x00000100;
    public const uint RIDEV_REMOVE = 0x00000001;

    public const int RIDI_DEVICENAME = 0x20000007;
    public const int RIDI_DEVICEINFO = 0x2000000b;

    public const int MOUSE_MOVE_RELATIVE = 0x00;

    // SendInput constants
    public const uint INPUT_MOUSE = 0;
    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_SCANCODE = 0x0008;

    // Virtual key codes
    public const ushort VK_W = 0x57;
    public const ushort VK_S = 0x53;

    // ─── Raw Input Structures ────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICELIST
    {
        public IntPtr hDevice;
        public uint dwType;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct RAWMOUSE
    {
        [FieldOffset(0)]
        public ushort usFlags;
        [FieldOffset(4)]
        public uint ulButtons;
        [FieldOffset(4)]
        public ushort usButtonFlags;
        [FieldOffset(6)]
        public ushort usButtonData;
        [FieldOffset(8)]
        public uint ulRawButtons;
        [FieldOffset(12)]
        public int lLastX;
        [FieldOffset(16)]
        public int lLastY;
        [FieldOffset(20)]
        public uint ulExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RID_DEVICE_INFO_MOUSE
    {
        public uint dwId;
        public uint dwNumberOfButtons;
        public uint dwSampleRate;
        public int fHasHorizontalWheel;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RID_DEVICE_INFO
    {
        public uint cbSize;
        public uint dwType;
        public RID_DEVICE_INFO_MOUSE mouse;
        // We only need mouse info, keyboard/hid ignored
    }

    // ─── SendInput Structures ────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUT_UNION
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public INPUT_UNION u;
    }

    // ─── Raw Input Functions ─────────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterRawInputDevices(
        RAWINPUTDEVICE[] pRawInputDevices,
        uint uiNumDevices,
        uint cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetRawInputData(
        IntPtr hRawInput,
        uint uiCommand,
        IntPtr pData,
        ref uint pcbSize,
        uint cbSizeHeader);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetRawInputDeviceList(
        [Out] RAWINPUTDEVICELIST[]? pRawInputDeviceList,
        ref uint puiNumDevices,
        uint cbSize);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern uint GetRawInputDeviceInfo(
        IntPtr hDevice,
        uint uiCommand,
        IntPtr pData,
        ref uint pcbSize);

    // ─── SendInput Functions ─────────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(
        uint nInputs,
        INPUT[] pInputs,
        int cbSize);

    [DllImport("user32.dll")]
    public static extern IntPtr GetMessageExtraInfo();

    // ─── Low-Level Mouse Hook ────────────────────────────────────────

    public const int WH_MOUSE_LL = 14;
    public const int WM_MOUSEMOVE = 0x0200;
    public const uint MOUSEEVENTF_MOVE       = 0x0001;
    public const uint MOUSEEVENTF_LEFTDOWN   = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP     = 0x0004;
    public const uint MOUSEEVENTF_RIGHTDOWN  = 0x0008;
    public const uint MOUSEEVENTF_RIGHTUP    = 0x0010;
    public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    public const uint MOUSEEVENTF_MIDDLEUP   = 0x0040;
    public const uint MOUSEEVENTF_XDOWN      = 0x0080;
    public const uint MOUSEEVENTF_XUP        = 0x0100;
    public const uint MOUSEEVENTF_WHEEL      = 0x0800;
    public const uint MOUSEEVENTF_HWHEEL     = 0x1000;

    // Raw Input mouse button flags
    public const ushort RI_MOUSE_LEFT_BUTTON_DOWN   = 0x0001;
    public const ushort RI_MOUSE_LEFT_BUTTON_UP     = 0x0002;
    public const ushort RI_MOUSE_RIGHT_BUTTON_DOWN  = 0x0004;
    public const ushort RI_MOUSE_RIGHT_BUTTON_UP    = 0x0008;
    public const ushort RI_MOUSE_MIDDLE_BUTTON_DOWN = 0x0010;
    public const ushort RI_MOUSE_MIDDLE_BUTTON_UP   = 0x0020;
    public const ushort RI_MOUSE_BUTTON_4_DOWN      = 0x0040;
    public const ushort RI_MOUSE_BUTTON_4_UP        = 0x0080;
    public const ushort RI_MOUSE_BUTTON_5_DOWN      = 0x0100;
    public const ushort RI_MOUSE_BUTTON_5_UP        = 0x0200;
    public const ushort RI_MOUSE_WHEEL              = 0x0400;
    public const ushort RI_MOUSE_HWHEEL             = 0x0800;

    // XButton identifiers for mouseData
    public const uint XBUTTON1 = 0x0001;
    public const uint XBUTTON2 = 0x0002;

    // Button WM_ messages for LL hook
    public const int WM_LBUTTONDOWN  = 0x0201;
    public const int WM_LBUTTONUP    = 0x0202;
    public const int WM_RBUTTONDOWN  = 0x0204;
    public const int WM_RBUTTONUP    = 0x0205;
    public const int WM_MBUTTONDOWN  = 0x0207;
    public const int WM_MBUTTONUP    = 0x0208;
    public const int WM_MOUSEWHEEL   = 0x020A;
    public const int WM_XBUTTONDOWN  = 0x020B;
    public const int WM_XBUTTONUP    = 0x020C;
    public const int WM_MOUSEHWHEEL  = 0x020E;

    /// <summary>Magic value we stamp on re-injected mouse events so the hook lets them through.</summary>
    public static readonly IntPtr REINJECT_MAGIC = new(0x54524541); // "TREA"

    public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(
        int idHook,
        LowLevelMouseProc lpfn,
        IntPtr hMod,
        uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(
        IntPtr hhk,IntPtr nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);
}
