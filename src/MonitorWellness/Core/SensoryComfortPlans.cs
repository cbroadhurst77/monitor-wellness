namespace MonitorWellness.Core;

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

    public static bool Apply(string plan, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!IsSupported(plan))
            return false;

        switch (plan)
        {
            case Balanced:
                ApplySchedule(settings, 6500, 1.0, 3400, 0.85, 0.70);
                settings.DefaultMigraineResponsePlan = MigraineResponsePlans.Strong;
                break;
            case Reading:
                ApplySchedule(settings, 5400, 0.85, 3800, 0.75, 0.65);
                settings.DefaultMigraineResponsePlan = MigraineResponsePlans.Gentle;
                break;
            case ColourCritical:
                ApplySchedule(settings, 6500, 1.0, 6500, 1.0, 1.0);
                settings.DefaultMigraineResponsePlan = MigraineResponsePlans.Gentle;
                break;
            case EarlySensitivity:
                ApplySchedule(settings, 5200, 0.75, 3800, 0.65, 0.55);
                settings.DefaultMigraineResponsePlan = MigraineResponsePlans.Gentle;
                break;
            case Recovery:
                ApplySchedule(settings, 4600, 0.60, 3400, 0.50, 0.40);
                settings.DefaultMigraineResponsePlan = MigraineResponsePlans.Strong;
                break;
        }

        return true;
    }

    private static void ApplySchedule(AppSettings settings, int dayKelvin, double dayBrightness, int nightKelvin, double nightBrightness, double deepNightBrightness)
    {
        settings.DayKelvin = dayKelvin;
        settings.DayBrightness = dayBrightness;
        settings.NightKelvin = nightKelvin;
        settings.NightBrightness = nightBrightness;
        settings.DeepNightBrightness = deepNightBrightness;
    }
}
