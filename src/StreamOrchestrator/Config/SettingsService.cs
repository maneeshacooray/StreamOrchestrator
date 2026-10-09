using System.IO;
using System.Text.Json;

namespace StreamOrchestrator.Config;

/// <summary>Persisted user preferences.</summary>
public sealed class AppSettings
{
    public string? LastUrl { get; set; }
    public int LastDisplayIndex { get; set; }
    public bool StreamAlwaysOnTop { get; set; }
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
