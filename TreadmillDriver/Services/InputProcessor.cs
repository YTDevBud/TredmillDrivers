using System.Windows.Threading;

namespace TreadmillDriver.Services;

/// <summary>
/// Processes raw mouse deltas into a smoothed velocity value suitable for output.
/// Uses exponential moving average and dead zone filtering.
/// </summary>
public class InputProcessor : IDisposable
{
    private readonly DispatcherTimer _timer;
    private double _accumulatedDeltaY;
    private double _smoothedVelocity;
    private readonly object _lock = new();
    private bool _disposed;

    // ─── Settings ────────────────────────────────────────────────────

    /// <summary>Sensitivity multiplier (0.1 to 10.0).</summary>
    public double Sensitivity { get; set; } = 2.0;

    /// <summary>Dead zone threshold (0 to 50).</summary>
    public double DeadZone { get; set; } = 5.0;

    /// <summary>Smoothing factor (0.05 to 1.0). Lower = smoother but more latent.</summary>
    public double Smoothing { get; set; } = 0.25;

    /// <summary>Maximum speed percentage (1 to 100).</summary>
    public double MaxSpeed { get; set; } = 100.0;

    /// <summary>Whether to invert the movement direction.</summary>
    public bool InvertDirection { get; set; }

    // ─── Output ──────────────────────────────────────────────────────

    /// <summary>
    /// Fires on each tick with the processed velocity value.
    /// Range: -1.0 (full backward) to 1.0 (full forward).
    /// </summary>
    public event Action<double>? VelocityUpdated;

    /// <summary>Current smoothed velocity (-1.0 to 1.0).</summary>
    public double CurrentVelocity => _smoothedVelocity;

    // ─── Constructor ─────────────────────────────────────────────────

    public InputProcessor()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16) // ~60 fps
        };
        _timer.Tick += OnTick;
    }

    // ─── Control ─────────────────────────────────────────────────────

    public void Start()
    {
        _smoothedVelocity = 0;
        _accumulatedDeltaY = 0;
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _smoothedVelocity = 0;
        _accumulatedDeltaY = 0;
        VelocityUpdated?.Invoke(0);
    }

    /// <summary>
    /// Feed a raw mouse delta Y value into the processor.
    /// Thread-safe: can be called from any thread.
    /// </summary>
    public void AddDelta(int deltaY)
    {
        lock (_lock)
        {
            _accumulatedDeltaY += deltaY;
        }
    }

    // ─── Processing ──────────────────────────────────────────────────

    private void OnTick(object? sender, EventArgs e)
    {
        double rawDelta;
        lock (_lock)
        {
            rawDelta = _accumulatedDeltaY;
            _accumulatedDeltaY = 0;
        }

        // Apply direction inversion
        // Default: negative mouse deltaY = treadmill moving forward = positive velocity
        double direction = InvertDirection ? 1.0 : -1.0;
        rawDelta *= direction;

        // Convert raw delta to a target velocity in [-1, 1] range IMMEDIATELY.
        // Sensitivity controls how many raw units per tick reach 100%.
        // At default Sensitivity=2.0, ~50 raw units/tick = 100%.
        double targetVelocity = Math.Clamp(rawDelta * Sensitivity / 100.0, -1.0, 1.0);

        // Exponential moving average smoothing — operates in normalized [-1, 1] space
        // so the value can NEVER accumulate above 1.0 (fixes the "stuck at 100%" bug)
        double alpha = Math.Clamp(Smoothing, 0.05, 1.0);
        _smoothedVelocity = _smoothedVelocity * (1.0 - alpha) + targetVelocity * alpha;

        // Apply dead zone (as percentage of full range → 0.0 to 0.5 normalized)
        double deadZoneNorm = DeadZone / 100.0;
        if (Math.Abs(_smoothedVelocity) < deadZoneNorm)
        {
            _smoothedVelocity *= 0.5; // Fast decay within dead zone
            if (Math.Abs(_smoothedVelocity) < 0.005)
                _smoothedVelocity = 0;
        }

        // Apply max speed cap
        double maxSpeedNorm = MaxSpeed / 100.0;
        double output = Math.Clamp(_smoothedVelocity, -maxSpeedNorm, maxSpeedNorm);

        VelocityUpdated?.Invoke(output);
    }

    // ─── Dispose ─────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        GC.SuppressFinalize(this);
    }
}
