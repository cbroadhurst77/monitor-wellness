using Color = System.Windows.Media.Color;

namespace MonitorWellness.Core;

/// <summary>Everything the schedule target depends on, supplied by the caller so the math is pure.</summary>
public sealed record ScheduleTargetInputs(
    int DayKelvin,
    int NightKelvin,
    double DayBrightness,
    double NightBrightness,
    double DeepNightBrightness,
    double SolarElevationDegrees,
    string? BedtimeLocal,
    DateTime NowLocal,
    string DeepNightOverlayColorHex);

/// <summary>The global (pre per-monitor) schedule target.</summary>
public readonly record struct ScheduleTarget(int Kelvin, double GlobalBrightness, Color DimColor);

/// <summary>
/// The pure core of App.ComputeScheduleTarget: solar elevation (plus an optional bedtime) in,
/// kelvin / global brightness / dim colour out. Extracted so it is testable without WPF windows,
/// the sensor, or live settings. Per-monitor handling stays in ScheduleBrightnessTargets.
/// </summary>
public static class ScheduleTargetCalculator
{
    /// <param name="ambientAdjustment">
    /// Null when ambient matching is off. Otherwise called with the current day factor and
    /// returns the brightness adjustment to add, or null when no light reading is available
    /// (the common no-sensor case), which leaves brightness untouched.
    /// </param>
    public static ScheduleTarget Compute(ScheduleTargetInputs inputs, Func<double, double?>? ambientAdjustment = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        double elevation = inputs.SolarElevationDegrees;
        int kelvin = ScheduleCurve.GetTargetKelvin(elevation, inputs.DayKelvin, inputs.NightKelvin);
        double nightBrightness = ScheduleCurve.GetTargetBrightness(elevation, inputs.DayBrightness, inputs.NightBrightness);

        double deepNightFactor = ScheduleCurve.GetDeepNightFactor(elevation);
        if (!string.IsNullOrWhiteSpace(inputs.BedtimeLocal) && TimeSpan.TryParse(inputs.BedtimeLocal, out var bedtime))
        {
            double bedtimeFactor = ScheduleCurve.GetBedtimeFactor(inputs.NowLocal, bedtime);
            deepNightFactor = Math.Max(deepNightFactor, bedtimeFactor);
        }
        double globalBrightness = Lerp(nightBrightness, inputs.DeepNightBrightness, deepNightFactor);

        if (ambientAdjustment is not null)
        {
            // Scaled by dayFactor by the caller so this has zero effect at night.
            double? adjustment = ambientAdjustment(ScheduleCurve.GetDayFactor(elevation));
            if (adjustment.HasValue)
                globalBrightness = Math.Clamp(globalBrightness + adjustment.Value, 0.0, 1.0);
        }

        Color dimColor = LerpColor(
            System.Windows.Media.Colors.Black,
            (Color)System.Windows.Media.ColorConverter.ConvertFromString(inputs.DeepNightOverlayColorHex)!,
            deepNightFactor);

        return new ScheduleTarget(kelvin, globalBrightness, dimColor);
    }

    private static double Lerp(double from, double to, double t) => from + (to - from) * t;

    private static Color LerpColor(Color from, Color to, double t) => Color.FromRgb(
        (byte)Lerp(from.R, to.R, t),
        (byte)Lerp(from.G, to.G, t),
        (byte)Lerp(from.B, to.B, t));
}
