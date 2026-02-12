# Treadmill Driver

A Windows desktop application that captures movement from a dedicated Bluetooth mouse (attached to a treadmill) and translates it into game input — keyboard keys, Xbox controller joystick, or VR controller joystick.

## How It Works

1. **Pair a Bluetooth mouse** through Windows Bluetooth settings and place it under/on your treadmill so the belt movement drives the mouse sensor.
2. **Select the mouse** in Treadmill Driver and click **Connect**.
3. **Choose an output mode** — the app translates the mouse's Y-axis movement (forward/backward) into your chosen input type.

## Output Modes

| Mode | Description |
|------|-------------|
| **Keyboard (W/S)** | Holds `W` while moving forward, `S` while moving backward. Works with any game. |
| **Xbox Controller** | Creates a virtual Xbox 360 controller. Maps movement to the left thumbstick Y axis (analog). |
| **VR Controller** | Creates a virtual DualShock 4 controller for SteamVR binding. Map it to VR locomotion in SteamVR controller settings. |

## Settings

| Setting | Range | Description |
|---------|-------|-------------|
| **Sensitivity** | 0.1 – 10.0 | Multiplier applied to raw movement. Higher = more responsive. |
| **Dead Zone** | 0 – 50 | Minimum movement threshold before input registers. Increase to ignore small vibrations. |
| **Smoothing** | 0.05 – 1.0 | How much to smooth the input. Lower = smoother but more latent. |
| **Max Speed** | 10 – 200 | Scaling factor for the speed-to-output mapping. |
| **Invert Direction** | On/Off | Reverse the forward/backward mapping if your mouse is oriented differently. |

## Prerequisites

### Required
- **Windows 10/11** (x64)
- **.NET 8.0 Runtime** — [Download](https://dotnet.microsoft.com/download/dotnet/8.0)
- **A Bluetooth mouse** paired through Windows

### For Xbox/VR Controller Modes
- **ViGEmBus Driver** — Required for virtual gamepad emulation
  - Download: https://github.com/nefarius/ViGEmBus/releases
  - Install the latest `.msi` and reboot

### For VR Controller Mode
- **SteamVR** — After connecting, go to SteamVR Settings → Controller Settings → Manage Controller Bindings and map the virtual DualShock 4 controller's left stick to your desired VR locomotion binding.

## Build & Run

```bash
# Clone or navigate to the project
cd TredmillDrivers

# Restore NuGet packages and build
dotnet restore TreadmillDriver.sln
dotnet build TreadmillDriver.sln -c Release

# Run the application
dotnet run --project TreadmillDriver
```

## Tips

- **Treadmill mouse placement**: Aim for consistent contact between the mouse sensor and the treadmill belt. A smooth belt surface works best.
- **Start with low sensitivity**: Begin at 1.0–2.0 sensitivity and increase until the response feels right.
- **Increase dead zone** if you see jitter when the treadmill is stopped.
- **Run as Administrator** if the application fails to capture mouse input.
- **Multiple mice**: The app lists all connected mice. Bluetooth mice appear at the top with a 🔵 indicator. Your regular desktop mouse will continue to work normally — only the selected device's Y-movement is captured.

## Architecture

```
TreadmillDriver/
├── Models/           Settings, device info, output modes
├── Native/           Win32 P/Invoke (Raw Input, SendInput)
├── Services/
│   ├── MouseCaptureService   — Raw Input API device capture
│   ├── InputProcessor        — Smoothing, dead zone, velocity calc
│   ├── KeyboardOutputService — SendInput W/S simulation
│   └── GamepadOutputService  — ViGEmBus virtual controller
├── ViewModels/       MVVM ViewModel with full data binding
├── Converters/       WPF value converters
├── Resources/        Dark theme styles
├── MainWindow.xaml   Main UI
└── App.xaml          Application entry
```

## License

MIT — Free for personal and commercial use.
