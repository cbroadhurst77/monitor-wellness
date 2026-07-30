using System.IO;
using System.Text.Json;

namespace MonitorWellness.Core;

/// <summary>
/// Named, saved snapshots of the Day/Night/migraine/bedtime "preferences" (the same subset
/// SettingsWindow's Reset button and LoadPreferencesFrom already treat as one unit, distinct
/// from location and per-monitor hardware setup) under
/// %AppData%\MonitorWellness\Profiles\&lt;name&gt;.json. Lets someone switch quickly between,
/// say, a bright work setup and a migraine-prone-day setup without re-entering every slider
/// each time. Stored as full AppSettings JSON for simplicity — SettingsWindow only reads the
/// preference fields back out of it (via LoadPreferencesFrom), the same way Reset already
/// only reads those fields out of a fresh AppSettings().
/// </summary>
public static class ProfileStore
{
    private static readonly string ProfilesDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MonitorWellness",
        "Profiles");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static IReadOnlyList<string> ListNames()
    {
        if (!Directory.Exists(ProfilesDirectory))
            return Array.Empty<string>();

        return Directory.GetFiles(ProfilesDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n is not null)
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void Save(string name, AppSettings snapshot)
    {
        Directory.CreateDirectory(ProfilesDirectory);
        string json = JsonSerializer.Serialize(snapshot, JsonOptions);
        File.WriteAllText(PathFor(name), json);
        DebugLog.Write($"ProfileStore: saved profile '{name}'");
    }

    public static AppSettings? Load(string name)
    {
        string path = PathFor(name);
        try
        {
            if (!File.Exists(path))
                return null;

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            DebugLog.Write($"ProfileStore: FAILED to load profile '{name}': {ex}");
            return null;
        }
    }

    public static void Delete(string name)
    {
        string path = PathFor(name);
        if (File.Exists(path))
            File.Delete(path);
        DebugLog.Write($"ProfileStore: deleted profile '{name}'");
    }

    private static string PathFor(string name) => Path.Combine(ProfilesDirectory, $"{SanitizeFileName(name)}.json");

    private static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
