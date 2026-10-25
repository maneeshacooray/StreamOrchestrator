using System.IO;
using System.Text.Json;

namespace StreamOrchestrator.Config;

/// <summary>Persisted user preferences.</summary>
public sealed class AppSettings
{
    public string? LastUrl { get; set; }
    public int LastDisplayIndex { get; set; }
    public bool StreamAlwaysOnTop { get; set; }
    public bool PresentationsOnly { get; set; } = true;
    public bool PinControlPanelOnTop { get; set; }
    public int AppDisplayIndex { get; set; } = -1;

    /// <summary>User hotkey overrides, keyed by action id. Missing entries use defaults.</summary>
    public Dictionary<string, Hotkey> Hotkeys { get; set; } = new();
}

/// <summary>A persisted global hotkey: Win32 modifier flags + virtual key.</summary>
public sealed class Hotkey
{
    public uint Modifiers { get; set; }
    public uint Vk { get; set; }

    /// <summary>A cleared binding has no key.</summary>
    public bool IsSet => Vk != 0;
}

/// <summary>Loads/saves <see cref="AppSettings"/> as JSON under %LOCALAPPDATA%.</summary>
public static class SettingsService
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "StreamOrchestrator");

    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            // Corrupt/unreadable settings fall back to defaults.
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Best-effort; ignore write failures.
        }
    }
}
