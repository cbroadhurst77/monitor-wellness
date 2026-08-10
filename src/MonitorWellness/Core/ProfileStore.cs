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
    private static readonly HashSet<string> ReservedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };
    private const int MaximumNameLength = 80;
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
        if (!TryValidateName(name, out string nameError))
            throw new ArgumentException(nameError, nameof(name));
        if (!AppSettingsValidator.TryValidate(snapshot, out string validationError))
            throw new ArgumentException($"Refusing to save invalid profile: {validationError}", nameof(snapshot));

        Directory.CreateDirectory(ProfilesDirectory);
        string json = JsonSerializer.Serialize(snapshot, JsonOptions);
        WriteAtomically(PathFor(name), json);
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
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (loaded is null)
            {
                DebugLog.Write($"ProfileStore: profile '{name}' was empty or malformed");
                return null;
            }
            if (!AppSettingsValidator.TryValidate(loaded, out string validationError))
            {
                DebugLog.Write($"ProfileStore: profile '{name}' failed validation: {validationError}");
                return null;
            }

            return loaded;
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

    public static bool TryValidateName(string? name, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Enter a profile name.";
            return false;
        }
        if (name.Length > MaximumNameLength)
        {
            error = $"Profile names can be at most {MaximumNameLength} characters.";
            return false;
        }
        if (name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || ReservedFileNames.Contains(name))
        {
            error = "That profile name can't be used as a Windows file name.";
            return false;
        }

        return true;
    }

    private static string PathFor(string name) => Path.Combine(ProfilesDirectory, $"{SanitizeFileName(name)}.json");

    private static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    private static void WriteAtomically(string destinationPath, string contents)
    {
        string directory = Path.GetDirectoryName(destinationPath)
            ?? throw new IOException("Profile destination has no parent directory.");
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, contents);
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
