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

        // Apply inversion (mouse Y: negative = move forward on surface)
        // Default: negative deltaY means treadmill forward = positive velocity
        double direction = InvertDirection ? 1.0 : -1.0;
        double scaledDelta = rawDelta * direction * Sensitivity;

        // Exponential moving average smoothing
        double smoothingFactor = Math.Clamp(Smoothing, 0.05, 1.0);
        _smoothedVelocity = _smoothedVelocity * (1.0 - smoothingFactor) + scaledDelta * smoothingFactor;

        // Apply dead zone
        if (Math.Abs(_smoothedVelocity) < DeadZone)
        {
            // Decay towards zero when in dead zone
            _smoothedVelocity *= 0.8;
            if (Math.Abs(_smoothedVelocity) < 0.5)
                _smoothedVelocity = 0;
        }

        // Normalize to -1.0 to 1.0 range
        // Assume a "reasonable max" raw speed of ~200 units per tick at sensitivity 1
        double maxRawSpeed = 100.0 * (MaxSpeed / 100.0);
        double normalizedVelocity = Math.Clamp(_smoothedVelocity / maxRawSpeed, -1.0, 1.0);

        VelocityUpdated?.Invoke(normalizedVelocity);
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
