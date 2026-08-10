namespace MonitorWellness.Core;

/// <summary>Schedule-only values used by a built-in comfort plan while an app rule is active.</summary>
public sealed record SensoryComfortSchedule(
    int DayKelvin,
    double DayBrightness,
    int NightKelvin,
    double NightBrightness,
    double DeepNightBrightness);

/// <summary>
/// Named, non-medical starting points for personal display comfort. They apply only reversible
/// app settings and remain an editable preview until the user chooses Save.
/// </summary>
public static class SensoryComfortPlans
{
    public const string Balanced = "Balanced";
    public const string Reading = "Reading";
    public const string ColourCritical = "ColourCritical";
    public const string EarlySensitivity = "EarlySensitivity";
    public const string Recovery = "Recovery";

    public static bool IsSupported(string? plan) => plan is Balanced or Reading or ColourCritical or EarlySensitivity or Recovery;

    /// <summary>
    /// Gets only the reversible schedule values for a plan. App-aware rules use this rather than
    /// mutating saved user preferences, so leaving the matched app immediately restores the
    /// person's normal schedule.
    /// </summary>
    public static bool TryGetSchedule(string? plan, out SensoryComfortSchedule schedule)
    {
        schedule = plan switch
        {
            Balanced => new SensoryComfortSchedule(6500, 1.0, 3400, 0.85, 0.70),
            Reading => new SensoryComfortSchedule(5400, 0.85, 3800, 0.75, 0.65),
            ColourCritical => new SensoryComfortSchedule(6500, 1.0, 6500, 1.0, 1.0),
            EarlySensitivity => new SensoryComfortSchedule(5200, 0.75, 3800, 0.65, 0.55),
            Recovery => new SensoryComfortSchedule(4600, 0.60, 3400, 0.50, 0.40),
            _ => new SensoryComfortSchedule(0, 0, 0, 0, 0),
        };
        return IsSupported(plan);
    }

    public static bool Apply(string plan, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!TryGetSchedule(plan, out SensoryComfortSchedule schedule))
            return false;

        ApplySchedule(settings, schedule);

        switch (plan)
        {
            case Balanced:
                settings.DefaultMigraineResponsePlan = MigraineResponsePlans.Strong;
                break;
            case Reading:
                settings.DefaultMigraineResponsePlan = MigraineResponsePlans.Gentle;
                break;
            case ColourCritical:
                settings.DefaultMigraineResponsePlan = MigraineResponsePlans.Gentle;
                break;
            case EarlySensitivity:
                settings.DefaultMigraineResponsePlan = MigraineResponsePlans.Gentle;
                break;
            case Recovery:
                settings.DefaultMigraineResponsePlan = MigraineResponsePlans.Strong;
                break;
        }

        return true;
    }

    private static void ApplySchedule(AppSettings settings, SensoryComfortSchedule schedule)
    {
        settings.DayKelvin = schedule.DayKelvin;
        settings.DayBrightness = schedule.DayBrightness;
        settings.NightKelvin = schedule.NightKelvin;
        settings.NightBrightness = schedule.NightBrightness;
        settings.DeepNightBrightness = schedule.DeepNightBrightness;
    }
}
