using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MonitorWellness.Core;

/// <summary>
/// Salvages a settings file with a few bad values. Settings files are meant to be readable and
/// editable, so one out-of-range or wrongly typed value is a likely mistake, and throwing away
/// everything else (location, hotkey, onboarding state, monitor setup) because of it is far worse
/// than resetting just that value. A file that is not JSON at all is still not salvageable.
/// </summary>
public static class SettingsRepair
{
    private sealed record Group(string Name, Action<AppSettings, AppSettings> Copy);

    // Each group is validated on its own: the settings are valid when everything else is at its default
    // and only this group's values come from the file. A group that fails is put back to the defaults.
    private static readonly Group[] Groups =
    {
        new("location", (to, from) => { to.Latitude = from.Latitude; to.Longitude = from.Longitude; }),
        new("colour temperatures", (to, from) => { to.DayKelvin = from.DayKelvin; to.NightKelvin = from.NightKelvin; }),
        new("brightness levels", (to, from) => { to.DayBrightness = from.DayBrightness; to.NightBrightness = from.NightBrightness; to.DeepNightBrightness = from.DeepNightBrightness; }),
        new("migraine overlay strength", (to, from) => { to.MigraineOverlayOpacity = from.MigraineOverlayOpacity; to.MigraineContrastReduction = from.MigraineContrastReduction; }),
        new("overlay colours", (to, from) => { to.DeepNightOverlayColorHex = from.DeepNightOverlayColorHex; to.MigraineOverlayColorHex = from.MigraineOverlayColorHex; }),
        new("migraine response plan", (to, from) => to.DefaultMigraineResponsePlan = from.DefaultMigraineResponsePlan),
        new("bedtime and wake time", (to, from) => { to.BedtimeLocal = from.BedtimeLocal; to.WakeTimeLocal = from.WakeTimeLocal; }),
        new("schedule", (to, from) => { to.ScheduleMode = from.ScheduleMode; to.FixedSunriseLocal = from.FixedSunriseLocal; to.FixedSunsetLocal = from.FixedSunsetLocal; }),
        new("migraine auto-off time", (to, from) => to.MigraineAutoRevertMinutes = from.MigraineAutoRevertMinutes),
        new("break reminder interval", (to, from) => to.BreakReminderIntervalMinutes = from.BreakReminderIntervalMinutes),
        new("break reminder hours", (to, from) => { to.BreakReminderLimitToHours = from.BreakReminderLimitToHours; to.BreakReminderFromLocal = from.BreakReminderFromLocal; to.BreakReminderToLocal = from.BreakReminderToLocal; }),
        new("break reminder style", (to, from) => to.BreakReminderStyle = from.BreakReminderStyle),
        new("focus break length", (to, from) => to.BreakFocusSeconds = from.BreakFocusSeconds),
        new("migraine hotkey", (to, from) => { to.MigraineHotkeyModifiers = from.MigraineHotkeyModifiers; to.MigraineHotkeyKey = from.MigraineHotkeyKey; }),
        new("excluded monitors", (to, from) => to.ExcludedMonitors = from.ExcludedMonitors),
        new("colour-accurate monitors", (to, from) => to.ColorExcludedMonitors = from.ColorExcludedMonitors),
        new("monitor dim multipliers", (to, from) => to.MonitorDimMultiplier = from.MonitorDimMultiplier),
        new("monitor colour offsets", (to, from) => to.MonitorKelvinOffset = from.MonitorKelvinOffset),
        new("hardware brightness monitors", (to, from) => to.HardwareBrightnessEnabledMonitors = from.HardwareBrightnessEnabledMonitors),
        new("hardware brightness safety records", (to, from) => to.HardwareBrightnessSafetyByMonitor = from.HardwareBrightnessSafetyByMonitor),
        new("application comfort rules", (to, from) => to.ApplicationComfortRules = from.ApplicationComfortRules),
    };

    /// <summary>
    /// Resets each invalid group of settings to its default. Returns the names of the groups that
    /// were reset (empty when the settings were already valid).
    /// </summary>
    public static List<string> Repair(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var repaired = new List<string>();
        if (AppSettingsValidator.TryValidate(settings, out _))
            return repaired;

        var defaults = new AppSettings();
        foreach (Group group in Groups)
        {
            var probe = new AppSettings();
            group.Copy(probe, settings);
            if (AppSettingsValidator.TryValidate(probe, out _))
                continue;

            group.Copy(settings, defaults);
            repaired.Add(group.Name);
        }
        return repaired;
    }

    /// <summary>
    /// Reads a settings object one property at a time, so that a value of the wrong type (text where
    /// a number belongs) costs only that property. Names of the properties that were skipped are added
    /// to <paramref name="skipped"/>. Throws <see cref="JsonException"/> if the text is not a JSON object.
    /// </summary>
    public static AppSettings DeserializeLeniently(string json, JsonSerializerOptions options, List<string> skipped)
    {
        if (JsonNode.Parse(json) is not JsonObject root)
            throw new JsonException("The settings file does not contain a JSON object.");

        var result = new AppSettings();
        foreach (var (name, node) in root)
        {
            PropertyInfo? property = typeof(AppSettings).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property is null || !property.CanWrite)
                continue; // unknown or read-only: ignored, as the strict reader does

            try
            {
                bool nonNullableValueType = property.PropertyType.IsValueType && Nullable.GetUnderlyingType(property.PropertyType) is null;
                if (node is null && nonNullableValueType)
                    throw new JsonException("null is not a valid value here.");

                property.SetValue(result, node?.Deserialize(property.PropertyType, options));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException or ArgumentException)
            {
                skipped.Add(name);
            }
        }
        return result;
    }
}
