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
    private static readonly string SettingsPath = Path.Combine(AppDataFolder.FullPath, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private const int MaximumKeptUnreadableCopies = 3;

    /// <summary>
    /// Set when Load() had to fall back to defaults because settings.json was unreadable or
    /// invalid: says what happened and where the original was kept, for the app to tell the
    /// person. Null after a normal load.
    /// </summary>
    public static string? LastLoadProblem { get; private set; }

    public static AppSettings Load()
    {
        LastLoadProblem = null;
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                var problems = new List<string>();
                AppSettings? loaded = Deserialize(json, problems);
                if (loaded is not null)
                {
                    // One bad value should cost that value, not every setting.
                    problems.AddRange(SettingsRepair.Repair(loaded));
                    if (AppSettingsValidator.TryValidate(loaded, out _))
                    {
                        DebugLog.Write($"SettingsStore: loaded settings from {SettingsPath}");
                        if (loaded.SchemaVersion > AppSettings.CurrentSchemaVersion)
                        {
                            // Written by a newer build. This build will drop what it doesn't know
                            // on the next save, so keep the original once, untouched.
                            string? kept = PreserveCopy(SettingsPath, "from-newer-version", pruneOldCopies: false);
                            DebugLog.Write($"SettingsStore: settings.json is schema v{loaded.SchemaVersion}, newer than this build (v{AppSettings.CurrentSchemaVersion}); original kept as {kept ?? "(copy failed)"}.");
                        }
                        if (problems.Count > 0)
                            RecordRepairedSettings(problems);
                        return loaded;
                    }
                }

                string validationError = loaded is null
                    ? "The file was empty or did not contain a settings object."
                    : GetValidationError(loaded);
                DebugLog.Write($"SettingsStore: settings file failed validation: {validationError}");
                RecordUnreadableSettings(validationError);
                if (TryLoadBackup() is AppSettings fromBackup)
                    return fromBackup;
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
            DebugLog.Write($"SettingsStore: FAILED to load settings: {ex}");
            RecordUnreadableSettings(ex.Message);
            if (TryLoadBackup() is AppSettings fromBackup)
                return fromBackup;
        }

        return new AppSettings();
    }

    /// <summary>Reads the file strictly; if a value has the wrong type, reads it property by property so only that value is lost.</summary>
    private static AppSettings? Deserialize(string json, List<string> problems)
    {
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
        }
        catch (JsonException)
        {
            // Throws JsonException again if the text is not a JSON object at all.
            return SettingsRepair.DeserializeLeniently(json, JsonOptions, problems);
        }
    }

    /// <summary>Some values were invalid and have been reset; everything else was kept. Keeps a dated copy of the original and says so.</summary>
    private static void RecordRepairedSettings(IReadOnlyList<string> problems)
    {
        string list = string.Join(", ", problems.Distinct());
        string? kept = PreserveCopy(SettingsPath, "invalid-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture), pruneOldCopies: true);
        DebugLog.Write($"SettingsStore: reset invalid settings to their defaults and kept the rest: {list}");
        LastLoadProblem = kept is null
            ? $"Some of your saved settings were not valid and were reset to their defaults ({list}). Everything else was kept."
            : $"Some of your saved settings were not valid and were reset to their defaults ({list}). Everything else was kept, and the original file was kept as {Path.GetFileName(kept)} in the Monitor Wellness folder.";
    }

    /// <summary>
    /// The settings as they were before the last save, kept by Save(). Used when settings.json
    /// is unreadable (typically an interrupted write), so that costs one save of changes rather
    /// than every setting. Null when there is no usable backup.
    /// </summary>
    private static AppSettings? TryLoadBackup()
    {
        try
        {
            string backupPath = DurableFile.BackupPath(SettingsPath);
            if (!File.Exists(backupPath))
                return null;

            var backup = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(backupPath), JsonOptions);
            if (backup is null || !AppSettingsValidator.TryValidate(backup, out _))
                return null;

            DebugLog.Write("SettingsStore: restored the previous saved settings from settings.json.bak.");
            LastLoadProblem = "Your latest settings file couldn't be read, so your previous saved settings were restored. The unreadable file was kept in the Monitor Wellness folder.";
            return backup;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Falling back to defaults is right, but the next Save() overwrites settings.json, which
    /// would destroy a file that is probably only slightly wrong (one hand-edited value out of
    /// range loses every other setting). Keeps a dated copy first and remembers a message.
    /// </summary>
    private static void RecordUnreadableSettings(string reason)
    {
        string? kept = PreserveCopy(SettingsPath, "invalid-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture), pruneOldCopies: true);
        LastLoadProblem = kept is null
            ? "Your settings file couldn't be read, so the defaults are in use."
            : $"Your settings file couldn't be read ({reason.TrimEnd('.')}), so the defaults are in use. The original was kept as {Path.GetFileName(kept)} in the Monitor Wellness folder (Diagnostics > Open Logs Folder).";
    }

    /// <summary>Copies <paramref name="sourcePath"/> to a sibling named settings.&lt;label&gt;.json. Returns the new path, or null if the copy failed. Never throws.</summary>
    internal static string? PreserveCopy(string sourcePath, string label, bool pruneOldCopies)
    {
        try
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? "";
            string stem = Path.GetFileNameWithoutExtension(sourcePath);
            string target = Path.Combine(directory, $"{stem}.{label}.json");
            File.Copy(sourcePath, target, overwrite: true);

            if (pruneOldCopies)
            {
                foreach (string old in Directory.GetFiles(directory, $"{stem}.invalid-*.json")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .Skip(MaximumKeptUnreadableCopies))
                {
                    File.Delete(old);
                }
            }
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DebugLog.Write($"SettingsStore: couldn't keep a copy of {Path.GetFileName(sourcePath)}: {ex.Message}");
            return null;
        }
    }

    public static void Save(AppSettings settings)
    {
        if (!AppSettingsValidator.TryValidate(settings, out string validationError))
            throw new ArgumentException($"Refusing to save invalid settings: {validationError}", nameof(settings));

        string? directory = Path.GetDirectoryName(SettingsPath);
        if (directory is not null)
            Directory.CreateDirectory(directory);

        settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
        string json = JsonSerializer.Serialize(settings, JsonOptions);
        WriteAtomically(SettingsPath, json, keepBackup: true);
    }

    /// <summary>
    /// Writes a standalone copy of the given settings to an arbitrary path — for backing up
    /// before a reinstall, or carrying tuned values to a second machine. ProfileStore already
    /// covers switching between preference sets on one machine; this covers the "everything,
    /// to a file I control" case that was previously only possible by knowing to manually copy
    /// %AppData%\MonitorWellness\settings.json, undocumented anywhere in the app itself.
    /// </summary>
    public static void ExportTo(AppSettings settings, string destinationPath)
    {
        if (!AppSettingsValidator.TryValidate(settings, out string validationError))
            throw new ArgumentException($"Refusing to export invalid settings: {validationError}", nameof(settings));

        string json = JsonSerializer.Serialize(settings, JsonOptions);
        WriteAtomically(destinationPath, json);
    }

    /// <summary>Reads settings from an arbitrary path (the counterpart to ExportTo) without touching the real settings.json — the caller decides whether/when to apply and persist it.</summary>
    public static bool TryImportFrom(string sourcePath, out AppSettings settings, out string error)
    {
        settings = new AppSettings();
        error = "";
        try
        {
            string json = File.ReadAllText(sourcePath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (loaded is null)
            {
                error = "That file doesn't contain valid Monitor Wellness settings.";
                return false;
            }
            if (!AppSettingsValidator.TryValidate(loaded, out error))
            {
                error = $"That file contains invalid Monitor Wellness settings: {error}";
                return false;
            }
            settings = loaded;
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            error = $"Couldn't read that file: {ex.Message}";
            return false;
        }
    }

    private static void WriteAtomically(string destinationPath, string contents, bool keepBackup = false)
        => DurableFile.WriteAllText(destinationPath, contents, keepBackup);

    private static string GetValidationError(AppSettings settings)
    {
        _ = AppSettingsValidator.TryValidate(settings, out string error);
        return error;
    }
}
