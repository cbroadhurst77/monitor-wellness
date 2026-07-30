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

    /// <summary>
    /// An alternative, clock-time-driven path to the same deep-night factor produced by
    /// GetDeepNightFactor. For anyone who goes to bed well before the sun sets deep enough
    /// (winter especially) or wants the wind-down to track their actual routine rather than
    /// the sun's, this ramps 0.0 -&gt; 1.0 over the <paramref name="rampMinutes"/> before
    /// bedtime, holds at 1.0 from bedtime through <paramref name="maxPastMinutes"/> after it
    /// (so it doesn't stay maxed out all the way until the following evening), then eases back
    /// to 0.0 outside that window. Callers combine this with GetDeepNightFactor via Math.Max so
    /// whichever signal -- sun or clock -- reaches deep night first wins.
    /// </summary>
    public static double GetBedtimeFactor(DateTime nowLocal, TimeSpan bedtimeOfDay, double rampMinutes = 90, double maxPastMinutes = 600)
    {
        double minutesFromBedtime = (nowLocal.TimeOfDay - bedtimeOfDay).TotalMinutes;

        // Normalize into (-720, 720] so a bedtime near midnight doesn't see a spurious
        // ~1440-minute gap between e.g. 23:45 and 00:15.
        while (minutesFromBedtime > 720) minutesFromBedtime -= 1440;
        while (minutesFromBedtime <= -720) minutesFromBedtime += 1440;

        if (minutesFromBedtime < 0)
        {
            // Before bedtime: ramp up over the last rampMinutes.
            double t = InverseLerpClamped(-rampMinutes, 0.0, minutesFromBedtime);
            return SmoothStep(t);
        }

        // At or after bedtime: full strength until maxPastMinutes, then ease back down over
        // the same ramp length so it doesn't cut off abruptly the next morning.
        if (minutesFromBedtime <= maxPastMinutes) return 1.0;
        double tDown = InverseLerpClamped(maxPastMinutes + rampMinutes, maxPastMinutes, minutesFromBedtime);
        return SmoothStep(tDown);
    }

    private static double InverseLerpClamped(double a, double b, double value)
        => Math.Clamp((value - a) / (b - a), 0.0, 1.0);

    private static double SmoothStep(double t) => t * t * (3.0 - 2.0 * t);
}
