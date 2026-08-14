using System.Text.Json;

namespace LocalShareWindows;

/// <summary>
/// Persisted app configuration — the Windows equivalent of the Android app's SharedPreferences.
/// </summary>
public class AppSettings
{
    public int Port { get; set; } = 8080;
    public bool ShareEntirePc { get; set; } = false;
    public string HiddenPaths { get; set; } = "";
    public bool LaunchOnStartup { get; set; } = false;

    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LocalShare");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null) return loaded;
            }
        }
        catch
        {
            // Corrupt or unreadable settings file — fall back to defaults rather than crash.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Best-effort persistence; a save failure shouldn't crash the app.
        }
    }
}
