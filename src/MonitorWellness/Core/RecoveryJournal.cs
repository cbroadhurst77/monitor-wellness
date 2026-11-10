using System.IO;
using System.Text.Json;

namespace MonitorWellness.Core;

/// <summary>
/// A tiny "write-ahead" file for display state this app is about to change. The file exists
/// only while something is modified: it is written before the first change and deleted once the
/// state has been restored cleanly. If the process is killed, crashes hard, or the machine loses
/// power in between, the next launch (or <c>--restore</c>) finds the file and puts the display
/// back, instead of treating the leftover dimmed/tinted state as the display's "normal".
/// Never throws: a journal problem must not stop the app or the restore it supports.
/// </summary>
public sealed class RecoveryJournal<T> where T : class
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly object _lock = new();

    public RecoveryJournal(string path) => _path = path;

    public string FilePath => _path;

    public IReadOnlyList<T> Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_path))
                    return Array.Empty<T>();
                return JsonSerializer.Deserialize<List<T>>(File.ReadAllText(_path), JsonOptions)
                    ?.Where(entry => entry is not null).ToList()
                    ?? (IReadOnlyList<T>)Array.Empty<T>();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                DebugLog.Write($"RecoveryJournal: couldn't read {System.IO.Path.GetFileName(_path)}: {ex.Message}");
                return Array.Empty<T>();
            }
        }
    }

    /// <summary>Replaces the journal's contents. An empty list deletes the file.</summary>
    public void Save(IReadOnlyCollection<T> entries)
    {
        lock (_lock)
        {
            try
            {
                if (entries.Count == 0)
                {
                    if (File.Exists(_path))
                        File.Delete(_path);
                    return;
                }

                // Flushed to disk before the move: the journal exists to survive a power loss, so
                // it must not be an empty file after one.
                DurableFile.WriteAllText(_path, JsonSerializer.Serialize(entries, JsonOptions));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                DebugLog.Write($"RecoveryJournal: couldn't write {System.IO.Path.GetFileName(_path)}: {ex.Message}");
            }
        }
    }

    public void Delete() => Save(Array.Empty<T>());
}

/// <summary>The gamma ramp a display had before this app first modified it.</summary>
public sealed record GammaBaselineEntry(string DeviceName, string HardwareDeviceId, ushort[] Red, ushort[] Green, ushort[] Blue);

/// <summary>A monitor's own hardware (DDC/CI) brightness before this app first changed it.</summary>
public sealed record BrightnessOriginalEntry(
    string DeviceName,
    string HardwareDeviceId,
    int PhysicalIndex,
    uint Value,
    uint Minimum,
    uint Maximum,
    DateTime SavedUtc);

public static class RecoveryJournals
{
    private static readonly string Directory_ = AppDataFolder.FullPath;

    public static RecoveryJournal<GammaBaselineEntry> GammaBaselines { get; } =
        new(System.IO.Path.Combine(Directory_, "recovery-gamma.json"));

    public static RecoveryJournal<BrightnessOriginalEntry> BrightnessOriginals { get; } =
        new(System.IO.Path.Combine(Directory_, "recovery-brightness.json"));
}

/// <summary>Add/remove helpers over the brightness journal. The first recorded value for a monitor wins, so a second session can never overwrite the real original with an already-dimmed reading.</summary>
public static class BrightnessOriginalJournal
{
    private static readonly object Lock = new();

    public static void Add(IEnumerable<BrightnessOriginalEntry> entries, RecoveryJournal<BrightnessOriginalEntry>? journal = null)
    {
        journal ??= RecoveryJournals.BrightnessOriginals;
        lock (Lock)
        {
            var current = journal.Load().ToList();
            bool changed = false;
            foreach (var entry in entries)
            {
                if (current.Any(existing => SameMonitor(existing, entry)))
                    continue;
                current.Add(entry);
                changed = true;
            }
            if (changed)
                journal.Save(current);
        }
    }

    public static void Remove(IEnumerable<BrightnessOriginalEntry> entries, RecoveryJournal<BrightnessOriginalEntry>? journal = null)
    {
        journal ??= RecoveryJournals.BrightnessOriginals;
        lock (Lock)
        {
            var toRemove = entries.ToList();
            var current = journal.Load().ToList();
            int removed = current.RemoveAll(existing => toRemove.Any(entry => SameMonitor(existing, entry)));
            if (removed > 0)
                journal.Save(current);
        }
    }

    public static bool SameMonitor(BrightnessOriginalEntry a, BrightnessOriginalEntry b)
        => a.PhysicalIndex == b.PhysicalIndex
            && (!string.IsNullOrWhiteSpace(a.HardwareDeviceId) && !string.IsNullOrWhiteSpace(b.HardwareDeviceId)
                ? string.Equals(a.HardwareDeviceId, b.HardwareDeviceId, StringComparison.OrdinalIgnoreCase)
                : string.Equals(a.DeviceName, b.DeviceName, StringComparison.OrdinalIgnoreCase));
}
