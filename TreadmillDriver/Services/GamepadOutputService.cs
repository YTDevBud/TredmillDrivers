using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using TreadmillDriver.Models;

namespace TreadmillDriver.Services;

/// <summary>
/// Emulates a virtual gamepad (Xbox 360 or DualShock 4) using ViGEmBus.
/// Maps velocity to the left thumbstick Y axis.
/// </summary>
public class GamepadOutputService : IDisposable
{
    private ViGEmClient? _client;
    private IXbox360Controller? _xbox360;
    private IDualShock4Controller? _ds4;
    private OutputMode _currentMode = OutputMode.XboxController;
    private bool _isConnected;
    private bool _disposed;
    private string? _lastError;

    /// <summary>Whether ViGEmBus is available on this system.</summary>
    public bool IsViGEmAvailable { get; private set; }

    /// <summary>Whether a virtual controller is currently connected.</summary>
    public bool IsConnected => _isConnected;

    /// <summary>Last error message if connection failed.</summary>
    public string? LastError => _lastError;

    /// <summary>
    /// Connect a virtual gamepad of the specified type.
    /// </summary>
    public bool Connect(OutputMode mode)
    {
        Disconnect();
        _currentMode = mode;

        try
        {
            _client = new ViGEmClient();

            // Both Xbox and VR modes use Xbox 360 controller for best compatibility.
            // SteamVR/OpenXR recognizes Xbox 360 natively and can map it to VR input.
            _xbox360 = _client.CreateXbox360Controller();
            _xbox360.Connect();

            _isConnected = true;
            _lastError = null;
            IsViGEmAvailable = true;
            return true;
        }
        catch (Exception ex)
        {
            _lastError = $"ViGEmBus error: {ex.Message}\nPlease install ViGEmBus driver from: https://github.com/nefarius/ViGEmBus/releases";
            _isConnected = false;
            IsViGEmAvailable = false;
            return false;
        }
    }

    /// <summary>
    /// Disconnect the virtual controller.
    /// </summary>
    public void Disconnect()
    {
        try { _xbox360?.Disconnect(); } catch { }
        try { _ds4?.Disconnect(); } catch { }
        try { _client?.Dispose(); } catch { }

        _xbox360 = null;
        _ds4 = null;
        _client = null;
        _isConnected = false;
    }

    /// <summary>
    /// Update the virtual controller's left thumbstick Y axis.
    /// </summary>
    /// <param name="normalizedVelocity">-1.0 (backward) to 1.0 (forward)</param>
    public void Update(double normalizedVelocity)
    {
        if (!_isConnected) return;

        try
        {
            if (_xbox360 != null)
            {
                // Xbox 360 left thumb Y: short range -32768 to 32767
                short value = (short)(normalizedVelocity * 32767);
                _xbox360.SetAxisValue(Xbox360Axis.LeftThumbY, value);
            }
        }
        catch (Exception ex)
        {
            _lastError = $"Controller update error: {ex.Message}";
        }
    }

    /// <summary>Reset the joystick to center position.</summary>
    public void ResetAxis()
    {
        Update(0.0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ResetAxis();
        Disconnect();
        GC.SuppressFinalize(this);
    }
}
