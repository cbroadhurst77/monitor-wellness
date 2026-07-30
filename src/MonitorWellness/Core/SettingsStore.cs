using System.IO;
using System.Text.Json;

namespace MonitorWellness.Core;

/// <summary>
/// Loads/saves AppSettings to %AppData%\MonitorWellness\settings.json. A registry-based
/// store was deliberately avoided (see IMPLEMENTATION.md locked decisions) — a plain JSON
/// file is easier for a user to inspect, back up, or paste into a support conversation.
/// </summary>
public static class SettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MonitorWellness",
        "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded is not null)
                {
                    DebugLog.Write($"SettingsStore: loaded settings from {SettingsPath}");
                    return loaded;
                }
            }
            else
            {
                DebugLog.Write($"SettingsStore: no settings file at {SettingsPath}, using defaults");
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable settings file — fall back to defaults rather than
            // crashing on startup. Previously this was silent, which meant a hand-edited or
            // malformed settings.json would reset every setting to defaults with zero
            // visibility — exactly what happened to the Asus monitor's dim multiplier during
            // Week 4 testing. The file gets overwritten (with valid JSON) on the next Save().
            DebugLog.Write($"SettingsStore: FAILED to load settings, falling back to defaults: {ex}");
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        string? directory = Path.GetDirectoryName(SettingsPath);
        if (directory is not null)
            Directory.CreateDirectory(directory);

        string json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}
