using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TreadmillDriver.Models;

/// <summary>
/// Application settings that can be persisted to disk.
/// </summary>
public class AppSettings
{
    /// <summary>Movement sensitivity multiplier (0.1 to 10.0).</summary>
    public double Sensitivity { get; set; } = 2.0;

    /// <summary>Minimum movement threshold to register input (0 to 50).</summary>
    public double DeadZone { get; set; } = 5.0;

    /// <summary>Smoothing factor for input (0.05 to 1.0). Lower = smoother.</summary>
    public double Smoothing { get; set; } = 0.25;

    /// <summary>Maximum output speed cap (percentage 1-100).</summary>
    public double MaxSpeed { get; set; } = 100.0;

    /// <summary>Whether to invert the movement direction.</summary>
    public bool InvertDirection { get; set; } = false;

    /// <summary>Whether to block the captured mouse from moving the system cursor.</summary>
    public bool BlockCursor { get; set; } = true;

    /// <summary>Currently selected output mode.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OutputMode SelectedOutputMode { get; set; } = OutputMode.Keyboard;

    /// <summary>Device path of the last selected mouse device.</summary>
    public string? LastDevicePath { get; set; }

    // ─── Persistence ─────────────────────────────────────────────────

    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TreadmillDriver");

    private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
        }
        catch
        {
            // Silently fail on save errors
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
            // Return defaults on load failure
        }
        return new AppSettings();
    }
}
