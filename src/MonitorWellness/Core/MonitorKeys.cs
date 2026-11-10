namespace MonitorWellness.Core;

/// <summary>
/// How per-monitor settings (exclude, colour-accurate, dim multiplier, Kelvin offset) identify a
/// monitor. They used to be keyed by the Windows display name (<c>\\.\DISPLAY2</c>), which Windows
/// renumbers after a dock, GPU or port change and after some reboots, so a setting could land on
/// the wrong physical screen (a colour-critical reference monitor tinted, another one dimmed).
/// They are now keyed by the monitor's physical identity (<see cref="HardwareBrightnessSafety.GetKey"/>,
/// the same identity hardware-brightness approval already uses). A monitor that exposes no physical
/// identity falls back to its display name.
/// Old files hold display names; <see cref="MigrateLegacyNames"/> converts them the first time the
/// monitor is seen, binding each to whichever monitor currently has that name.
/// </summary>
public static class MonitorKeys
{
    private const string PhysicalPrefix = "monitor:";

    /// <summary>A monitor known only by its display name (the enumerator could not resolve its hardware identity).</summary>
    public static MonitorInfo Unidentified(string deviceName) => new(deviceName, "", false, "");

    public static string For(MonitorInfo monitor)
        => HardwareBrightnessSafety.GetKey(monitor) ?? monitor.DeviceName;

    public static bool IsPhysicalKey(string key)
        => key.StartsWith(PhysicalPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True if a stored entry refers to this monitor. A display name matches only a monitor that
    /// has no physical identity: for any other monitor a display name is exactly the unreliable
    /// reference this class exists to avoid, and is converted by <see cref="MigrateLegacyNames"/>.
    /// </summary>
    public static bool Matches(string entry, MonitorInfo monitor)
        => string.Equals(entry, For(monitor), StringComparison.OrdinalIgnoreCase);

    public static bool Contains(IEnumerable<string> entries, MonitorInfo monitor)
        => entries.Any(entry => Matches(entry, monitor));

    public static bool TryGetValue<T>(IReadOnlyDictionary<string, T> entries, MonitorInfo monitor, out T value)
    {
        foreach (var (key, candidate) in entries)
        {
            if (Matches(key, monitor))
            {
                value = candidate;
                return true;
            }
        }

        value = default!;
        return false;
    }

    /// <summary>
    /// Replaces display-name entries with physical keys for every monitor that is connected now.
    /// Returns true if anything changed. Entries for monitors that are not connected stay as they are.
    /// </summary>
    public static bool MigrateLegacyNames(AppSettings settings, IReadOnlyList<MonitorInfo> activeMonitors)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(activeMonitors);

        var keyByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (MonitorInfo monitor in activeMonitors)
        {
            if (HardwareBrightnessSafety.GetKey(monitor) is string key)
                keyByName.TryAdd(monitor.DeviceName, key);
        }
        if (keyByName.Count == 0)
            return false;

        bool changed = false;
        settings.ExcludedMonitors = MigrateList(settings.ExcludedMonitors, keyByName, ref changed);
        settings.ColorExcludedMonitors = MigrateList(settings.ColorExcludedMonitors, keyByName, ref changed);
        settings.MonitorDimMultiplier = MigrateDictionary(settings.MonitorDimMultiplier, keyByName, ref changed);
        settings.MonitorKelvinOffset = MigrateDictionary(settings.MonitorKelvinOffset, keyByName, ref changed);
        return changed;
    }

    private static List<string> MigrateList(List<string> entries, Dictionary<string, string> keyByName, ref bool changed)
    {
        var result = new List<string>(entries.Count);
        foreach (string entry in entries)
        {
            string migrated = !IsPhysicalKey(entry) && keyByName.TryGetValue(entry, out string? key) ? key : entry;
            changed |= !ReferenceEquals(migrated, entry);
            if (!result.Contains(migrated, StringComparer.OrdinalIgnoreCase))
                result.Add(migrated);
            else
                changed = true;
        }
        return result;
    }

    private static Dictionary<string, T> MigrateDictionary<T>(Dictionary<string, T> entries, Dictionary<string, string> keyByName, ref bool changed)
    {
        var result = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        // Physical keys first, so that if both forms exist the one already in the new format wins.
        foreach (var (key, value) in entries.Where(pair => IsPhysicalKey(pair.Key) || !keyByName.ContainsKey(pair.Key)))
            result[key] = value;
        foreach (var (key, value) in entries.Where(pair => !IsPhysicalKey(pair.Key) && keyByName.ContainsKey(pair.Key)))
        {
            result.TryAdd(keyByName[key], value);
            changed = true;
        }
        return result;
    }

    /// <summary>
    /// The new list for a flag setting after the Settings window saves it: entries for monitors the
    /// window showed are replaced by what the window says; entries for monitors that were not
    /// connected are kept (the window cannot show them, and dropping them used to forget a
    /// docked monitor's settings every time Settings was saved while undocked).
    /// </summary>
    public static List<string> MergeFlags(IEnumerable<string> existing, IReadOnlyCollection<MonitorInfo> shown, IEnumerable<MonitorInfo> flagged)
    {
        var result = existing.Where(entry => !shown.Any(monitor => Matches(entry, monitor))).ToList();
        foreach (MonitorInfo monitor in flagged)
        {
            string key = For(monitor);
            if (!result.Contains(key, StringComparer.OrdinalIgnoreCase))
                result.Add(key);
        }
        return result;
    }

    /// <summary>Same as <see cref="MergeFlags"/> for settings that hold a value per monitor.</summary>
    public static Dictionary<string, T> MergeValues<T>(
        IReadOnlyDictionary<string, T> existing,
        IReadOnlyCollection<MonitorInfo> shown,
        IEnumerable<(MonitorInfo Monitor, T Value)> values)
    {
        var result = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in existing)
        {
            if (!shown.Any(monitor => Matches(key, monitor)))
                result[key] = value;
        }
        foreach (var (monitor, value) in values)
            result[For(monitor)] = value;
        return result;
    }

    /// <summary>
    /// What the schedule needs, keyed by display name (the surface it applies to): which displays
    /// are excluded, and each one's dim multiplier, resolved through the monitors' physical identity.
    /// </summary>
    public static (HashSet<string> Excluded, Dictionary<string, double> Multipliers) ResolveDimSettings(
        AppSettings settings, IEnumerable<string> deviceNames, IReadOnlyList<MonitorInfo> activeMonitors)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var byName = new Dictionary<string, MonitorInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (MonitorInfo monitor in activeMonitors)
            byName.TryAdd(monitor.DeviceName, monitor);

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var multipliers = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (string deviceName in deviceNames)
        {
            MonitorInfo monitor = byName.TryGetValue(deviceName, out MonitorInfo? known) ? known : Unidentified(deviceName);
            if (Contains(settings.ExcludedMonitors, monitor))
                excluded.Add(deviceName);
            if (TryGetValue(settings.MonitorDimMultiplier, monitor, out double multiplier))
                multipliers[deviceName] = multiplier;
        }
        return (excluded, multipliers);
    }
}
