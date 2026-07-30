namespace MonitorWellness.Core;

/// <summary>
/// Maps solar elevation angle to target color temperature and brightness, smoothly
/// interpolated across the twilight band. This avoids the jarring clock-time cutover
/// that a naive sunrise/sunset schedule would produce.
/// </summary>
public static class ScheduleCurve
{
    /// <summary>Elevation (degrees) above which it's treated as full daytime.</summary>
    public const double DayThresholdDeg = 3.0;

    /// <summary>Elevation (degrees) below which it's treated as full night (end of civil twilight).</summary>
    public const double NightThresholdDeg = -6.0;

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

    private static double InverseLerpClamped(double a, double b, double value)
        => Math.Clamp((value - a) / (b - a), 0.0, 1.0);

    private static double SmoothStep(double t) => t * t * (3.0 - 2.0 * t);
}
