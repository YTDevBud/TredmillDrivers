using System;
using System.IO;
using Microsoft.Win32;

namespace TreadmillDriver.Services;

/// <summary>
/// Manages installation and uninstallation of the OpenXR implicit API layer.
/// Copies the DLL + JSON manifest to a local directory and registers
/// the layer in the Windows registry so any OpenXR application picks it up.
/// </summary>
public class OpenXRLayerManager
{
    private const string RegistryPath = @"SOFTWARE\Khronos\OpenXR\1\ApiLayers\Implicit";
    private const string LayerDllName = "treadmill_layer.dll";
    private const string LayerJsonName = "treadmill_layer.json";

    private readonly string _installDir;
    private readonly string _installedDllPath;
    private readonly string _installedJsonPath;

    /// <summary>
    /// Resolved path to the DLL we will install from (build output or prebuilt).
    /// </summary>
    public string SourceDllPath { get; }

    public OpenXRLayerManager()
    {
        _installDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TreadmillDriver", "OpenXRLayer");

        _installedDllPath  = Path.Combine(_installDir, LayerDllName);
        _installedJsonPath = Path.Combine(_installDir, LayerJsonName);

        // Walk up from bin/Debug/net8.0-windows/ → project → solution root
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var solutionRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));

        // 1st priority: prebuilt/ folder (GitHub Actions artifact drop)
        var prebuiltPath = Path.Combine(solutionRoot, "OpenXRLayer", "prebuilt", LayerDllName);
        // 2nd priority: local build output in bin/
        var buildPath = Path.Combine(solutionRoot, "OpenXRLayer", "bin", LayerDllName);

        SourceDllPath = File.Exists(prebuiltPath) ? prebuiltPath : buildPath;
    }

    /// <summary>
    /// True if the layer is registered in the Windows registry.
    /// </summary>
    public bool IsInstalled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
                if (key == null) return false;
                var val = key.GetValue(_installedJsonPath);
                return val is int i && i == 0;
            }
            catch { return false; }
        }
    }

    /// <summary>
    /// True if the native DLL has been built and exists in the expected location.
    /// </summary>
    public bool IsDllBuilt => File.Exists(SourceDllPath);

    /// <summary>
    /// Installs the OpenXR layer: copies files and adds the registry entry.
    /// Returns a human-readable status message.
    /// </summary>
    public (bool Success, string Message) Install()
    {
        try
        {
            if (!File.Exists(SourceDllPath))
            {
                return (false,
                    $"DLL not found. Build it first:\n" +
                    $"  1. Open a Developer Command Prompt for VS\n" +
                    $"  2. cd to OpenXRLayer\\\n" +
                    $"  3. Run build.bat\n\n" +
                    $"Expected: {SourceDllPath}");
            }

            Directory.CreateDirectory(_installDir);

            // Copy DLL
            File.Copy(SourceDllPath, _installedDllPath, overwrite: true);

            // Write JSON manifest (library_path must be absolute for OpenXR loader compatibility)
            var dllAbsolutePath = _installedDllPath.Replace('\\', '/');
            var json = $$"""
            {
                "file_format_version": "1.0.0",
                "api_layer": {
                    "name": "XR_APILAYER_TREADMILL_driver",
                    "library_path": "{{dllAbsolutePath}}",
                    "api_version": "1.0.0",
                    "implementation_version": "1",
                    "description": "Treadmill Driver - Injects treadmill velocity into VR left thumbstick",
                    "disable_environment": "DISABLE_TREADMILL_LAYER"
                }
            }
            """;
            File.WriteAllText(_installedJsonPath, json, new System.Text.UTF8Encoding(false));

            // Register as an implicit API layer (HKCU — no admin needed)
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            key.SetValue(_installedJsonPath, 0, RegistryValueKind.DWord);

            return (true, "OpenXR layer installed successfully.\nRestart any running VR game to activate.");
        }
        catch (Exception ex)
        {
            return (false, $"Install failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes the registry entry and deletes installed files.
    /// </summary>
    public (bool Success, string Message) Uninstall()
    {
        try
        {
            // Remove registry entry
            using (var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true))
            {
                key?.DeleteValue(_installedJsonPath, throwOnMissingValue: false);
            }

            // Delete installed files
            if (File.Exists(_installedDllPath))  File.Delete(_installedDllPath);
            if (File.Exists(_installedJsonPath)) File.Delete(_installedJsonPath);

            // Try to remove the directory if empty
            if (Directory.Exists(_installDir) &&
                Directory.GetFiles(_installDir).Length == 0)
            {
                Directory.Delete(_installDir);
            }

            return (true, "OpenXR layer uninstalled.\nRestart any running VR game to deactivate.");
        }
        catch (Exception ex)
        {
            return (false, $"Uninstall failed: {ex.Message}");
        }
    }
}
