namespace MonitorWellness.Core;

/// <summary>
/// Maps solar elevation angle to target color temperature and brightness, smoothly
/// interpolated across the twilight band. This avoids the jarring clock-time cutover
/// that a naive sunrise/sunset schedule would produce.
///
/// Three phases, not two: Day -> Evening -> Deep night. The Day/Evening split (thresholds
/// below) matches f.lux's own day (6500K) / sunset (3400K) defaults, and 3400K also happens
/// to be roughly the safe floor for gamma-ramp-only color shifts on this hardware (see the
/// Week 1 finding in IMPLEMENTATION.md). Circadian/melatonin research supports going warmer
/// still approaching actual bedtime (commonly cited range ~1800-2400K shortly before sleep),
/// which gamma ramp cannot reach alone -- the Deep night phase is what lets App.xaml.cs layer
/// a low-opacity warm overlay tint on top of gamma's floor to approximate that, without
/// needing a separate color-temperature curve for it (gamma is already maxed out by then).
/// </summary>
public static class ScheduleCurve
{
    /// <summary>Elevation (degrees) above which it's treated as full daytime.</summary>
    public const double DayThresholdDeg = 3.0;

    /// <summary>Elevation (degrees) below which it's treated as full evening/night (end of civil twilight).</summary>
    public const double NightThresholdDeg = -6.0;

    /// <summary>Elevation (degrees) below which it's treated as deep night (end of nautical twilight) -- bedtime-like warmth/dim kicks in fully by here.</summary>
    public const double DeepNightThresholdDeg = -12.0;

    public static int GetTargetKelvin(double elevationDeg, int dayKelvin, int nightKelvin)
    {
        double eased = SmoothStep(InverseLerpClamped(NightThresholdDeg, DayThresholdDeg, elevationDeg));
        return (int)Math.Round(nightKelvin + (dayKelvin - nightKelvin) * eased);
    }

    public static double GetTargetBrightness(double elevationDeg, double dayBrightness, double nightBrightness)
    {
        double eased = SmoothStep(InverseLerpClamped(NightThresholdDeg, DayThresholdDeg, elevationDeg));
        return nightBrightness + (dayBrightness - nightBrightness) * eased;
    }

    /// <summary>
    /// 0.0 at the Night threshold, ramping smoothly to 1.0 at the Deep night threshold.
    /// Used to blend in the extra bedtime-like warmth/dim on top of the already-reached
    /// evening/night target, once past civil twilight.
    /// </summary>
    public static double GetDeepNightFactor(double elevationDeg)
        => SmoothStep(InverseLerpClamped(NightThresholdDeg, DeepNightThresholdDeg, elevationDeg));

    private static double InverseLerpClamped(double a, double b, double value)
        => Math.Clamp((value - a) / (b - a), 0.0, 1.0);

    private static double SmoothStep(double t) => t * t * (3.0 - 2.0 * t);
}
