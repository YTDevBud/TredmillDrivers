using System.Runtime.InteropServices;
using TreadmillDriver.Native;

namespace TreadmillDriver.Services;

/// <summary>
/// Simulates keyboard W/S key presses based on velocity input.
/// </summary>
public class KeyboardOutputService : IDisposable
{
    private bool _wPressed;
    private bool _sPressed;
    private bool _disposed;

    /// <summary>Threshold for triggering a key press (0.0 to 1.0).</summary>
    public double Threshold { get; set; } = 0.05;

    /// <summary>
    /// Update the keyboard output based on normalized velocity.
    /// Positive = W (forward), Negative = S (backward).
    /// </summary>
    public void Update(double normalizedVelocity)
    {
        if (_disposed) return;

        bool shouldPressW = normalizedVelocity > Threshold;
        bool shouldPressS = normalizedVelocity < -Threshold;

        // Press/Release W
        if (shouldPressW && !_wPressed)
        {
            SendKeyDown(NativeMethods.VK_W);
            _wPressed = true;
        }
        else if (!shouldPressW && _wPressed)
        {
            SendKeyUp(NativeMethods.VK_W);
            _wPressed = false;
        }

        // Press/Release S
        if (shouldPressS && !_sPressed)
        {
            SendKeyDown(NativeMethods.VK_S);
            _sPressed = true;
        }
        else if (!shouldPressS && _sPressed)
        {
            SendKeyUp(NativeMethods.VK_S);
            _sPressed = false;
        }
    }

    /// <summary>Release all keys.</summary>
    public void ReleaseAll()
    {
        if (_wPressed) { SendKeyUp(NativeMethods.VK_W); _wPressed = false; }
        if (_sPressed) { SendKeyUp(NativeMethods.VK_S); _sPressed = false; }
    }

    private static void SendKeyDown(ushort vkCode)
    {
        var inputs = new NativeMethods.INPUT[]
        {
            new()
            {
                type = NativeMethods.INPUT_KEYBOARD,
                u = new NativeMethods.INPUT_UNION
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = vkCode,
                        wScan = 0,
                        dwFlags = 0, // Key down
                        time = 0,
                        dwExtraInfo = NativeMethods.GetMessageExtraInfo()
                    }
                }
            }
        };
        NativeMethods.SendInput(1, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static void SendKeyUp(ushort vkCode)
    {
        var inputs = new NativeMethods.INPUT[]
        {
            new()
            {
                type = NativeMethods.INPUT_KEYBOARD,
                u = new NativeMethods.INPUT_UNION
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = vkCode,
                        wScan = 0,
                        dwFlags = NativeMethods.KEYEVENTF_KEYUP,
                        time = 0,
                        dwExtraInfo = NativeMethods.GetMessageExtraInfo()
                    }
                }
            }
        };
        NativeMethods.SendInput(1, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseAll();
        GC.SuppressFinalize(this);
    }
}
