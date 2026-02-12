namespace TreadmillDriver.Models;

/// <summary>
/// Represents the available output modes for translating treadmill movement.
/// </summary>
public enum OutputMode
{
    /// <summary>Simulate W/S keyboard presses.</summary>
    Keyboard,

    /// <summary>Virtual Xbox 360 controller left joystick Y axis.</summary>
    XboxController,

    /// <summary>Virtual controller for VR locomotion mapping (DualShock 4).</summary>
    VRController
}
