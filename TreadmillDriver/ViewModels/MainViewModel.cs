using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using TreadmillDriver.Models;
using TreadmillDriver.Services;

namespace TreadmillDriver.ViewModels;

/// <summary>
/// Main ViewModel orchestrating device capture, input processing, and output generation.
/// </summary>
public class MainViewModel : ViewModelBase, IDisposable
{
    private readonly MouseCaptureService _mouseCapture;
    private readonly InputProcessor _inputProcessor;
    private readonly KeyboardOutputService _keyboardOutput;
    private readonly GamepadOutputService _gamepadOutput;
    private readonly SharedMemoryService _sharedMemory;
    private readonly OpenXRLayerManager _vrLayerManager;
    private readonly AppSettings _settings;
    private bool _disposed;

    // ─── Constructor ─────────────────────────────────────────────────

    public MainViewModel()
    {
        _settings = AppSettings.Load();
        _mouseCapture = new MouseCaptureService();
        _inputProcessor = new InputProcessor();
        _keyboardOutput = new KeyboardOutputService();
        _gamepadOutput = new GamepadOutputService();
        _sharedMemory = new SharedMemoryService();
        _vrLayerManager = new OpenXRLayerManager();

        // Wire up mouse movement to input processor
        _mouseCapture.MouseMoved += (dx, dy) => _inputProcessor.AddDelta(dy);

        // Wire up processed velocity to output
        _inputProcessor.VelocityUpdated += OnVelocityUpdated;

        // Apply loaded settings
        ApplySettings();

        // Create commands
        RefreshDevicesCommand = new RelayCommand(RefreshDevices);
        ConnectCommand = new RelayCommand(ToggleConnection, () => SelectedDevice != null);
        SelectOutputModeCommand = new RelayCommand(OnSelectOutputMode);
        ToggleVRLayerCommand = new RelayCommand(ToggleVRLayer);

        // Initial device scan
        RefreshDevices();
    }

    // ─── Device Properties ───────────────────────────────────────────

    private ObservableCollection<MouseDeviceInfo> _devices = new();
    public ObservableCollection<MouseDeviceInfo> Devices
    {
        get => _devices;
        set => SetProperty(ref _devices, value);
    }

    private MouseDeviceInfo? _selectedDevice;
    public MouseDeviceInfo? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
                ((RelayCommand)ConnectCommand).RaiseCanExecuteChanged();
        }
    }

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (SetProperty(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(ConnectionStatusText));
                OnPropertyChanged(nameof(ConnectButtonText));
            }
        }
    }

    public string ConnectionStatusText => IsConnected ? "● Connected" : "○ Disconnected";
    public string ConnectButtonText => IsConnected ? "Disconnect" : "Connect";

    // ─── Output Mode Properties ──────────────────────────────────────

    private OutputMode _selectedOutputMode;
    public OutputMode SelectedOutputMode
    {
        get => _selectedOutputMode;
        set
        {
            if (SetProperty(ref _selectedOutputMode, value))
            {
                _settings.SelectedOutputMode = value;
                OnPropertyChanged(nameof(IsKeyboardMode));
                OnPropertyChanged(nameof(IsXboxMode));
                OnPropertyChanged(nameof(IsVRMode));
                OnPropertyChanged(nameof(OutputModeStatusText));
                SwitchOutputMode(value);
            }
        }
    }

    public bool IsKeyboardMode => SelectedOutputMode == OutputMode.Keyboard;
    public bool IsXboxMode => SelectedOutputMode == OutputMode.XboxController;
    public bool IsVRMode => SelectedOutputMode == OutputMode.VRController;

    public string OutputModeStatusText => SelectedOutputMode switch
    {
        OutputMode.Keyboard => "Keyboard (W / S)",
        OutputMode.XboxController => "Xbox Controller (Left Stick)",
        OutputMode.VRController => "VR Controller (Left Stick)",
        _ => "None"
    };

    // ─── Settings Properties ─────────────────────────────────────────

    public double Sensitivity
    {
        get => _settings.Sensitivity;
        set
        {
            _settings.Sensitivity = value;
            _inputProcessor.Sensitivity = value;
            OnPropertyChanged();
        }
    }

    public double DeadZone
    {
        get => _settings.DeadZone;
        set
        {
            _settings.DeadZone = value;
            _inputProcessor.DeadZone = value;
            OnPropertyChanged();
        }
    }

    public double SmoothingValue
    {
        get => _settings.Smoothing;
        set
        {
            _settings.Smoothing = value;
            _inputProcessor.Smoothing = value;
            OnPropertyChanged();
        }
    }

    public double MaxSpeed
    {
        get => _settings.MaxSpeed;
        set
        {
            _settings.MaxSpeed = value;
            _inputProcessor.MaxSpeed = value;
            OnPropertyChanged();
        }
    }

    public bool InvertDirection
    {
        get => _settings.InvertDirection;
        set
        {
            _settings.InvertDirection = value;
            _inputProcessor.InvertDirection = value;
            OnPropertyChanged();
        }
    }

    public bool BlockCursor
    {
        get => _settings.BlockCursor;
        set
        {
            _settings.BlockCursor = value;
            _mouseCapture.BlockCursor = value;
            OnPropertyChanged();
        }
    }

    // ─── Live Monitor Properties ─────────────────────────────────────

    private double _currentVelocity;
    public double CurrentVelocity
    {
        get => _currentVelocity;
        set
        {
            SetProperty(ref _currentVelocity, value);
            OnPropertyChanged(nameof(VelocityPercentage));
            OnPropertyChanged(nameof(VelocityDisplayText));
            OnPropertyChanged(nameof(DirectionText));
            OnPropertyChanged(nameof(ForwardBarHeight));
            OnPropertyChanged(nameof(BackwardBarHeight));
        }
    }

    public double VelocityPercentage => Math.Abs(CurrentVelocity) * 100;
    public string VelocityDisplayText => $"{VelocityPercentage:F0}%";
    public string DirectionText => CurrentVelocity > 0.01 ? "▲ Forward" : CurrentVelocity < -0.01 ? "▼ Backward" : "— Idle";

    // Bar heights for the visual indicator (max 150 pixels)
    public double ForwardBarHeight => Math.Max(0, CurrentVelocity) * 150;
    public double BackwardBarHeight => Math.Max(0, -CurrentVelocity) * 150;

    // ─── Status ──────────────────────────────────────────────────────

    private string _statusMessage = "Ready — Select a mouse device to begin";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    private string _gamepadStatusMessage = "";
    public string GamepadStatusMessage
    {
        get => _gamepadStatusMessage;
        set => SetProperty(ref _gamepadStatusMessage, value);
    }

    // ─── Commands ────────────────────────────────────────────────────

    public ICommand RefreshDevicesCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand SelectOutputModeCommand { get; }
    public ICommand ToggleVRLayerCommand { get; }

    // ─── VR OpenXR Layer Properties ──────────────────────────────────

    public bool IsVRLayerInstalled => _vrLayerManager.IsInstalled;

    public string VRLayerStatusText => IsVRLayerInstalled
        ? "Layer installed — active for all OpenXR apps"
        : "Layer not installed";

    public string VRLayerButtonText => IsVRLayerInstalled
        ? "Uninstall Layer"
        : "Install Layer";

    private string _vrLayerMessage = "";
    public string VRLayerMessage
    {
        get => _vrLayerMessage;
        set => SetProperty(ref _vrLayerMessage, value);
    }

    private void ToggleVRLayer()
    {
        if (IsVRLayerInstalled)
        {
            var (_, msg) = _vrLayerManager.Uninstall();
            VRLayerMessage = msg;
        }
        else
        {
            var (_, msg) = _vrLayerManager.Install();
            VRLayerMessage = msg;
        }
        OnPropertyChanged(nameof(IsVRLayerInstalled));
        OnPropertyChanged(nameof(VRLayerStatusText));
        OnPropertyChanged(nameof(VRLayerButtonText));
    }

    // ─── Window reference (needed for raw input registration) ────────

    private Window? _window;
    public void SetWindow(Window window) => _window = window;

    // ─── Methods ─────────────────────────────────────────────────────

    private void RefreshDevices()
    {
        var devices = MouseCaptureService.EnumerateMouseDevices();
        Devices = new ObservableCollection<MouseDeviceInfo>(devices);

        // Try to re-select the previously used device
        if (!string.IsNullOrEmpty(_settings.LastDevicePath))
        {
            SelectedDevice = Devices.FirstOrDefault(d => d.DevicePath == _settings.LastDevicePath);
        }

        if (SelectedDevice == null && Devices.Count > 0)
        {
            // Prefer Bluetooth devices
            SelectedDevice = Devices.FirstOrDefault(d => d.IsBluetooth) ?? Devices[0];
        }

        StatusMessage = $"Found {devices.Count} mouse device(s) — {devices.Count(d => d.IsBluetooth)} Bluetooth";
    }

    private void ToggleConnection()
    {
        if (IsConnected)
        {
            Disconnect();
        }
        else
        {
            Connect();
        }
    }

    private void Connect()
    {
        if (SelectedDevice == null || _window == null) return;

        if (_mouseCapture.StartCapture(SelectedDevice.DeviceHandle, _window))
        {
            _inputProcessor.Start();
            _sharedMemory.Start();
            IsConnected = true;
            _settings.LastDevicePath = SelectedDevice.DevicePath;

            // If using gamepad mode, connect the virtual controller
            if (SelectedOutputMode != OutputMode.Keyboard)
            {
                SwitchOutputMode(SelectedOutputMode);
            }

            StatusMessage = $"Active — Capturing from {SelectedDevice.DisplayName}";
        }
        else
        {
            StatusMessage = "⚠ Failed to start capture. Try running as Administrator.";
        }
    }

    private void Disconnect()
    {
        _inputProcessor.Stop();
        _mouseCapture.StopCapture();
        _keyboardOutput.ReleaseAll();
        _gamepadOutput.ResetAxis();
        _gamepadOutput.Disconnect();
        _sharedMemory.Stop();

        IsConnected = false;
        CurrentVelocity = 0;
        GamepadStatusMessage = "";
        StatusMessage = "Disconnected — Select a device to begin";
    }

    private void SwitchOutputMode(OutputMode mode)
    {
        // Clean up current output
        _keyboardOutput.ReleaseAll();
        _gamepadOutput.ResetAxis();
        _gamepadOutput.Disconnect();
        GamepadStatusMessage = "";

        if (mode != OutputMode.Keyboard && IsConnected)
        {
            if (!_gamepadOutput.Connect(mode))
            {
                GamepadStatusMessage = _gamepadOutput.LastError ?? "Failed to create virtual controller";
            }
            else
            {
                string label = mode == OutputMode.XboxController ? "Xbox 360" : "Xbox 360 + Keyboard (VR)";
                GamepadStatusMessage = $"✓ Virtual {label} controller active";
            }
        }

        _settings.SelectedOutputMode = mode;
    }

    private void OnSelectOutputMode(object? parameter)
    {
        if (parameter is string modeStr && Enum.TryParse<OutputMode>(modeStr, out var mode))
        {
            SelectedOutputMode = mode;
        }
    }

    private void OnVelocityUpdated(double normalizedVelocity)
    {
        CurrentVelocity = normalizedVelocity;

        // Always write to shared memory (OpenXR layer reads it)
        _sharedMemory.UpdateVelocity((float)normalizedVelocity);

        switch (SelectedOutputMode)
        {
            case OutputMode.Keyboard:
                _keyboardOutput.Update(normalizedVelocity);
                break;

            case OutputMode.XboxController:
                _gamepadOutput.Update(normalizedVelocity);
                break;

            case OutputMode.VRController:
                // VR mode: OpenXR layer handles thumbstick injection directly.
                // Also send gamepad + keyboard as fallback for non-OpenXR games.
                _gamepadOutput.Update(normalizedVelocity);
                _keyboardOutput.Update(normalizedVelocity);
                break;
        }
    }

    private void ApplySettings()
    {
        _inputProcessor.Sensitivity = _settings.Sensitivity;
        _inputProcessor.DeadZone = _settings.DeadZone;
        _inputProcessor.Smoothing = _settings.Smoothing;
        _inputProcessor.MaxSpeed = _settings.MaxSpeed;
        _inputProcessor.InvertDirection = _settings.InvertDirection;
        _mouseCapture.BlockCursor = _settings.BlockCursor;
        _selectedOutputMode = _settings.SelectedOutputMode;
    }

    public void SaveSettings() => _settings.Save();

    // ─── Dispose ─────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        SaveSettings();
        _inputProcessor.Stop();
        _mouseCapture.Dispose();
        _inputProcessor.Dispose();
        _keyboardOutput.Dispose();
        _gamepadOutput.Dispose();
        _sharedMemory.Dispose();

        GC.SuppressFinalize(this);
    }
}
