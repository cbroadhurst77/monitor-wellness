using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace MonitorWellness.Core;

/// <summary>
/// Validates the persisted settings boundary. Settings files are intentionally user-readable,
/// which also means they must be treated as untrusted input when loaded or imported.
/// </summary>
public static class AppSettingsValidator
{
    /// <summary>Lowest brightness allowed in persisted settings, so normal scheduling cannot make a display unusable.</summary>
    public const double MinimumSafeBrightness = 0.05;

    private const uint AllowedHotkeyModifiers = GlobalHotkey.MOD_ALT | GlobalHotkey.MOD_CONTROL | GlobalHotkey.MOD_SHIFT | GlobalHotkey.MOD_WIN;
    private const int MaximumAutoRevertMinutes = 24 * 60;
    private const double MaximumMonitorDimMultiplier = 5.0;
    private const int MaximumMonitorKelvinOffset = 2_000;
    // No real setup has anywhere near this many entries (a handful of physical monitors at
    // most); this only guards against a corrupted/hand-edited settings.json driving an
    // unbounded per-monitor collection into UI code that iterates it on every startup.
    private const int MaximumMonitorEntries = 64;

    public static bool TryValidate(AppSettings? settings, out string error)
    {
        error = "";
        if (settings is null)
            return Invalid("Settings are missing.", out error);

        if (!IsFiniteInRange(settings.Latitude, -90, 90))
            return Invalid("Latitude must be a finite number between -90 and 90.", out error);
        if (!IsFiniteInRange(settings.Longitude, -180, 180))
            return Invalid("Longitude must be a finite number between -180 and 180.", out error);
        if (!ColorTemperature.IsSafeForGammaRamp(settings.DayKelvin) || !ColorTemperature.IsSafeForGammaRamp(settings.NightKelvin))
            return Invalid("Day and night color temperatures must be safe for the gamma ramp.", out error);
        if (!IsFiniteInRange(settings.DayBrightness, MinimumSafeBrightness, 1)
            || !IsFiniteInRange(settings.NightBrightness, MinimumSafeBrightness, 1)
            || !IsFiniteInRange(settings.DeepNightBrightness, MinimumSafeBrightness, 1)
            || !IsFiniteInRange(settings.MigraineOverlayOpacity, 0, 1)
            || !IsFiniteInRange(settings.MigraineContrastReduction, 0, 0.3))
            return Invalid($"Brightness must be between {MinimumSafeBrightness:P0} and 100%; opacity and contrast must be finite and within their supported ranges.", out error);
        if (!IsValidColor(settings.DeepNightOverlayColorHex) || !IsValidColor(settings.MigraineOverlayColorHex))
            return Invalid("Overlay colors must be valid color values.", out error);
        if (!MigraineResponsePlans.IsSupported(settings.DefaultMigraineResponsePlan))
            return Invalid("The default migraine response plan is invalid.", out error);
        if (!string.IsNullOrWhiteSpace(settings.BedtimeLocal)
            && (!TimeSpan.TryParse(settings.BedtimeLocal, out TimeSpan bedtime) || bedtime < TimeSpan.Zero || bedtime >= TimeSpan.FromDays(1)))
            return Invalid("Bedtime must be a valid time between 00:00 and 23:59.", out error);
        if (!string.IsNullOrWhiteSpace(settings.WakeTimeLocal))
        {
            if (!FixedTimeSchedule.TryParseTimeOfDay(settings.WakeTimeLocal, out TimeSpan wake))
                return Invalid("Wake-up time must be a valid time between 00:00 and 23:59.", out error);
            if (!string.IsNullOrWhiteSpace(settings.BedtimeLocal)
                && TimeSpan.TryParse(settings.BedtimeLocal, out TimeSpan bedtimeForWake)
                && wake == bedtimeForWake)
                return Invalid("Wake-up time must be different from bedtime.", out error);
        }
        if (!ScheduleModes.IsSupported(settings.ScheduleMode))
            return Invalid("The schedule mode is not recognised.", out error);
        if (string.Equals(settings.ScheduleMode, ScheduleModes.FixedTimes, StringComparison.Ordinal)
            && FixedTimeSchedule.Validate(settings.FixedSunriseLocal, settings.FixedSunsetLocal) is string fixedTimesError)
            return Invalid(fixedTimesError, out error);
        if (settings.MigraineAutoRevertMinutes is < 0 or > MaximumAutoRevertMinutes)
            return Invalid($"Migraine auto-revert must be between 0 and {MaximumAutoRevertMinutes} minutes.", out error);
        if (settings.BreakReminderIntervalMinutes is < BreakReminderPolicy.MinimumIntervalMinutes or > BreakReminderPolicy.MaximumIntervalMinutes)
            return Invalid($"Break reminder interval must be between {BreakReminderPolicy.MinimumIntervalMinutes} and {BreakReminderPolicy.MaximumIntervalMinutes} minutes.", out error);
        if (!FixedTimeSchedule.TryParseTimeOfDay(settings.BreakReminderFromLocal, out _)
            || !FixedTimeSchedule.TryParseTimeOfDay(settings.BreakReminderToLocal, out _))
            return Invalid("Break reminder hours must be times between 00:00 and 23:59.", out error);
        if (!BreakReminderStyles.IsSupported(settings.BreakReminderStyle))
            return Invalid("The break reminder style is not recognised.", out error);
        if (settings.StandUpIntervalMinutes is < BreakReminderPolicy.MinimumStandUpMinutes or > BreakReminderPolicy.MaximumStandUpMinutes)
            return Invalid($"The stand-up reminder interval must be between {BreakReminderPolicy.MinimumStandUpMinutes} and {BreakReminderPolicy.MaximumStandUpMinutes} minutes.", out error);
        if (settings.BreakFocusSeconds is < BreakReminderStyles.MinimumFocusSeconds or > BreakReminderStyles.MaximumFocusSeconds)
            return Invalid($"The focus break must be between {BreakReminderStyles.MinimumFocusSeconds} and {BreakReminderStyles.MaximumFocusSeconds} seconds.", out error);
        if ((settings.MigraineHotkeyModifiers & ~AllowedHotkeyModifiers) != 0 || settings.MigraineHotkeyModifiers == 0 || settings.MigraineHotkeyKey == 0)
            return Invalid("Migraine hotkey settings are invalid.", out error);
        if (settings.ExcludedMonitors is null || settings.ColorExcludedMonitors is null
            || settings.MonitorDimMultiplier is null || settings.MonitorKelvinOffset is null
            || settings.HardwareBrightnessEnabledMonitors is null || settings.HardwareBrightnessSafetyByMonitor is null)
            return Invalid("Monitor settings collections cannot be null.", out error);
        if (settings.ExcludedMonitors.Count > MaximumMonitorEntries || settings.ColorExcludedMonitors.Count > MaximumMonitorEntries
            || settings.MonitorDimMultiplier.Count > MaximumMonitorEntries || settings.MonitorKelvinOffset.Count > MaximumMonitorEntries
            || settings.HardwareBrightnessEnabledMonitors.Count > MaximumMonitorEntries || settings.HardwareBrightnessSafetyByMonitor.Count > MaximumMonitorEntries)
            return Invalid($"No more than {MaximumMonitorEntries} entries are allowed per monitor setting.", out error);
        if (settings.ApplicationComfortRules is null)
            return Invalid("Application comfort rules cannot be null.", out error);
        if (!ValidateMonitorMultipliers(settings.MonitorDimMultiplier, out error)
            || !ValidateMonitorOffsets(settings.MonitorKelvinOffset, out error)
            || !ValidateHardwareBrightnessSafety(settings.HardwareBrightnessSafetyByMonitor, out error)
            || !ValidateApplicationComfortRules(settings.ApplicationComfortRules, out error))
            return false;

        return true;
    }

    private static bool ValidateMonitorMultipliers(IReadOnlyDictionary<string, double> multipliers, out string error)
    {
        foreach (var (deviceName, multiplier) in multipliers)
        {
            if (string.IsNullOrWhiteSpace(deviceName) || !IsFiniteInRange(multiplier, 0, MaximumMonitorDimMultiplier))
                return Invalid($"Dim multiplier for '{deviceName}' must be finite and between 0 and {MaximumMonitorDimMultiplier}.", out error);
        }

        error = "";
        return true;
    }

    private static bool ValidateMonitorOffsets(IReadOnlyDictionary<string, int> offsets, out string error)
    {
        foreach (var (deviceName, offset) in offsets)
        {
            if (string.IsNullOrWhiteSpace(deviceName) || Math.Abs((long)offset) > MaximumMonitorKelvinOffset)
                return Invalid($"Kelvin offset for '{deviceName}' must be between -{MaximumMonitorKelvinOffset} and {MaximumMonitorKelvinOffset}.", out error);
        }

        error = "";
        return true;
    }

    private static bool ValidateHardwareBrightnessSafety(IReadOnlyDictionary<string, HardwareBrightnessSafetyState> states, out string error)
    {
        foreach (var (key, state) in states)
        {
            if (string.IsNullOrWhiteSpace(key) || state is null)
                return Invalid("Hardware brightness safety records must have a monitor identifier.", out error);
            if (state.IsQuarantined && state.IsApproved)
                return Invalid("A hardware-brightness monitor cannot be approved while quarantined.", out error);
            if (state.FailureCount is < 0 or > 1000)
                return Invalid("Hardware brightness failure count is out of range.", out error);
            if (state.QuarantineReason?.Length > 500)
                return Invalid("Hardware brightness quarantine information is too long.", out error);
        }

        error = "";
        return true;
    }

    private static bool ValidateApplicationComfortRules(List<ApplicationComfortRule> rules, out string error)
    {
        if (rules.Count > 100)
            return Invalid("No more than 100 application comfort rules can be saved.", out error);

        var ruleContexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ApplicationComfortRule? rule in rules)
        {
            if (rule is null || !ApplicationComfortRules.TryNormalizeProcessName(rule.ProcessName, out string normalized)
                || !ApplicationComfortActions.IsSupported(rule.Action)
                || rule.WindowTitleContains?.Length > 200
                || rule.WindowTitleContains?.Any(char.IsControl) == true
                || (string.Equals(rule.Action, ApplicationComfortActions.ApplySensoryComfortPlan, StringComparison.Ordinal)
                    && !SensoryComfortPlans.IsSupported(rule.ComfortPlanName))
                || (string.Equals(rule.Action, ApplicationComfortActions.RestoreNativeDisplay, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(rule.ComfortPlanName)))
            {
                return Invalid("Application comfort rules must use a valid executable name, optional safe window-title condition, and a complete supported action.", out error);
            }
            string titleCondition = rule.WindowTitleContains?.Trim() ?? "";
            if (!ruleContexts.Add($"{normalized}\u001f{titleCondition}"))
                return Invalid("Each application and window-title condition can have only one comfort rule.", out error);
        }

        error = "";
        return true;
    }

    private static bool IsValidColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
            return false;

        try
        {
            _ = (MediaColor)MediaColorConverter.ConvertFromString(color)!;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsFiniteInRange(double value, double minimum, double maximum)
        => double.IsFinite(value) && value >= minimum && value <= maximum;

    private static bool Invalid(string message, out string error)
    {
        error = message;
        return false;
    }
}
