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
                if (loaded is not null && AppSettingsValidator.TryValidate(loaded, out _))
                {
                    DebugLog.Write($"SettingsStore: loaded settings from {SettingsPath}");
                    return loaded;
                }

                string validationError = loaded is null
                    ? "The file was empty or did not contain a settings object."
                    : GetValidationError(loaded);
                DebugLog.Write($"SettingsStore: settings file failed validation, falling back to defaults: {validationError}");
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
        if (!AppSettingsValidator.TryValidate(settings, out string validationError))
            throw new ArgumentException($"Refusing to save invalid settings: {validationError}", nameof(settings));

        string? directory = Path.GetDirectoryName(SettingsPath);
        if (directory is not null)
            Directory.CreateDirectory(directory);

        string json = JsonSerializer.Serialize(settings, JsonOptions);
        WriteAtomically(SettingsPath, json);
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

    private static void WriteAtomically(string destinationPath, string contents)
    {
        string fullPath = Path.GetFullPath(destinationPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory))
            throw new IOException("Settings destination has no parent directory.");

        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, contents);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static string GetValidationError(AppSettings settings)
    {
        _ = AppSettingsValidator.TryValidate(settings, out string error);
        return error;
    }
}
