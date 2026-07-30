namespace MonitorWellness.Core;

/// <summary>
/// Pure mapping from a raw ambient-light sensor reading (lux) to a bounded brightness
/// adjustment layered on top of the existing solar-based schedule — see
/// TECHNICAL_UX_REVIEW.md §1.1. This is the one piece of ergonomics guidance EVALUATION.md's
/// own science review actually supports (matching screen brightness to ambient light), and
/// until now the app only ever approximated it with a fixed day/night schedule with no
/// awareness of the room's actual light level.
///
/// Deliberately a bounded *adjustment*, not a replacement for the schedule: it nudges the
/// already-computed daytime brightness target up or down by at most MaxAdjustment, and
/// (via App.ComputeScheduleTarget) is scaled by the same day/night blend factor the rest of
/// the schedule already uses, so it has no effect at night regardless of room lighting — a
/// dark bedroom with a lamp on shouldn't fight the night schedule.
/// </summary>
public static class AmbientLightAdapter
{
    /// <summary>Illuminance (lux) representing a typical well-lit indoor room — the point at which no adjustment is applied.</summary>
    public const double ReferenceLux = 300.0;

    /// <summary>At or below this lux, the maximum downward adjustment applies (a dim/curtained room).</summary>
    public const double DimLux = 20.0;

    /// <summary>At or above this lux, the maximum upward adjustment applies (bright daylight).</summary>
    public const double BrightLux = 2000.0;

    /// <summary>Maximum adjustment in either direction, as a fraction of full brightness (0.15 = ±15%).</summary>
    public const double MaxAdjustment = 0.15;

    /// <summary>
    /// Maps a raw lux reading to a brightness adjustment in [-MaxAdjustment, +MaxAdjustment],
    /// smoothstep-interpolated like the rest of this app's curves (see ScheduleCurve) rather
    /// than a hard cutoff at ReferenceLux.
    /// </summary>
    public static double ComputeBrightnessAdjustment(double lux)
    {
        if (lux <= DimLux)
            return -MaxAdjustment;
        if (lux >= BrightLux)
            return MaxAdjustment;

        double t = lux <= ReferenceLux
            ? SmoothStep(InverseLerpClamped(DimLux, ReferenceLux, lux)) - 1.0   // -1..0
            : SmoothStep(InverseLerpClamped(ReferenceLux, BrightLux, lux));     // 0..1

        return t * MaxAdjustment;
    }

    private static double InverseLerpClamped(double a, double b, double value)
        => Math.Clamp((value - a) / (b - a), 0.0, 1.0);

    private static double SmoothStep(double t) => t * t * (3.0 - 2.0 * t);
}
