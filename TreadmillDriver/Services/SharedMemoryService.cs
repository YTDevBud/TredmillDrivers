using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace TreadmillDriver.Services;

/// <summary>
/// Writes treadmill velocity to a named memory-mapped file so the
/// native OpenXR API layer can read it and inject into VR input.
/// </summary>
public sealed class SharedMemoryService : IDisposable
{
    private const string SharedMemName = "TreadmillDriverVelocity";
    private const int SharedMemSize = 8; // float velocity + uint32 active

    private MemoryMappedFile? _mmf;
    private MemoryMappedViewAccessor? _accessor;
    private bool _disposed;

    /// <summary>
    /// Creates / opens the shared memory region and marks it active.
    /// </summary>
    public void Start()
    {
        if (_mmf != null) return;

        _mmf = MemoryMappedFile.CreateOrOpen(
            SharedMemName,
            SharedMemSize,
            MemoryMappedFileAccess.ReadWrite);

        _accessor = _mmf.CreateViewAccessor(0, SharedMemSize, MemoryMappedFileAccess.ReadWrite);

        // velocity = 0.0f
        _accessor.Write(0, 0.0f);
        // active = 1
        _accessor.Write(4, (uint)1);
    }

    /// <summary>
    /// Writes the current normalised velocity (-1 … 1) to shared memory.
    /// Called on every processing tick (~60 fps).
    /// </summary>
    public void UpdateVelocity(float velocity)
    {
        _accessor?.Write(0, velocity);
    }

    /// <summary>
    /// Marks the shared memory as inactive and zeros the velocity.
    /// </summary>
    public void Stop()
    {
        if (_accessor != null)
        {
            _accessor.Write(0, 0.0f);   // velocity = 0
            _accessor.Write(4, (uint)0); // active = 0
        }

        _accessor?.Dispose();
        _accessor = null;
        _mmf?.Dispose();
        _mmf = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
