using System.Globalization;

namespace MonitorWellness.Core;

/// <summary>What drives the day/night schedule.</summary>
public static class ScheduleModes
{
    /// <summary>Follow the sun at the person's location (the default).</summary>
    public const string Sun = "Sun";

    /// <summary>Use two clock times instead; no location is needed.</summary>
    public const string FixedTimes = "FixedTimes";

    public static bool IsSupported(string? mode) => mode is Sun or FixedTimes;
}

/// <summary>
/// A day/night schedule from two clock times instead of the sun, for people who don't want to
/// set a location, work nights, or just want predictable times. It produces a synthetic "solar
/// elevation" with the same shape the real sun has (night, then a twilight ramp, then day, then
/// the reverse, then a deeper night), so everything downstream (colour temperature, brightness,
/// deep night, the bedtime blend, the status line) works unchanged.
/// </summary>
public static class FixedTimeSchedule
{
    public const double DefaultTransitionMinutes = 60;
    public const string DefaultSunrise = "07:00";
    public const string DefaultSunset = "19:00";

    // The same stages the sun goes through: deep night (-12 degrees), end of civil twilight
    // (-6), full day (+3), see ScheduleCurve's thresholds.
    private const double DeepNight = ScheduleCurve.DeepNightThresholdDeg;
    private const double Night = ScheduleCurve.NightThresholdDeg;
    private const double Day = ScheduleCurve.DayThresholdDeg;
    private const double PreDawnMinutes = 90;
    private const double PostSunsetMinutes = 90;

    private const double MinimumDayMinutes = 4 * 60;
    private const double MaximumDayMinutes = 20 * 60;

    /// <summary>Parses an "HH:mm" time of day. Invariant culture, 00:00 to 23:59.</summary>
    public static bool TryParseTimeOfDay(string? text, out TimeSpan time)
    {
        time = default;
        return !string.IsNullOrWhiteSpace(text)
            && TimeSpan.TryParse(text.Trim(), CultureInfo.InvariantCulture, out time)
            && time >= TimeSpan.Zero
            && time < TimeSpan.FromDays(1);
    }

    /// <summary>
    /// Checks the two times: both valid, and the day long enough and the night long enough for the
    /// twilight ramps to fit (day between 4 and 20 hours). Returns null when fine, otherwise a
    /// message for the person. Pure and testable.
    /// </summary>
    public static string? Validate(string? sunrise, string? sunset)
    {
        if (!TryParseTimeOfDay(sunrise, out TimeSpan rise))
            return "The time the day starts must be in HH:mm format, e.g. 07:00.";
        if (!TryParseTimeOfDay(sunset, out TimeSpan set))
            return "The time the evening starts must be in HH:mm format, e.g. 19:00.";

        double dayMinutes = Mod((set - rise).TotalMinutes, 1440);
        return dayMinutes is < MinimumDayMinutes or > MaximumDayMinutes
            ? "Daytime must last between 4 and 20 hours. Choose a start of day and a start of evening that are further apart."
            : null;
    }

    /// <summary>
    /// The synthetic elevation (degrees) for a time of day. The day starts rising at
    /// <paramref name="sunrise"/> and the evening starts at <paramref name="sunset"/>, each
    /// centred in a transition of <paramref name="transitionMinutes"/>; before the morning
    /// transition the night lightens over 90 minutes, and after the evening one it deepens over 90.
    /// Assumes <see cref="Validate"/> passed. Pure and testable.
    /// </summary>
    public static double GetSyntheticElevationDegrees(TimeSpan timeOfDay, TimeSpan sunrise, TimeSpan sunset, double transitionMinutes = DefaultTransitionMinutes)
    {
        double transition = Math.Max(1, transitionMinutes);
        double half = transition / 2;
        double dayLength = Mod((sunset - sunrise).TotalMinutes, 1440);

        // Minutes since the start of the pre-dawn lightening, which opens the cycle.
        double start = sunrise.TotalMinutes - half - PreDawnMinutes;
        double t = Mod(timeOfDay.TotalMinutes - start, 1440);

        double morningEnd = PreDawnMinutes + transition;          // day plateau begins
        double eveningStart = dayLength + PreDawnMinutes;         // = (sunset - half) relative to start
        double eveningEnd = eveningStart + transition;
        double nightDeepenEnd = eveningEnd + PostSunsetMinutes;

        if (t < PreDawnMinutes) return Lerp(DeepNight, Night, t / PreDawnMinutes);
        if (t < morningEnd) return Lerp(Night, Day, (t - PreDawnMinutes) / transition);
        if (t < eveningStart) return Day;
        if (t < eveningEnd) return Lerp(Day, Night, (t - eveningStart) / transition);
        if (t < nightDeepenEnd) return Lerp(Night, DeepNight, (t - eveningEnd) / PostSunsetMinutes);
        return DeepNight;
    }

    private static double Lerp(double from, double to, double fraction) => from + (to - from) * Math.Clamp(fraction, 0.0, 1.0);

    private static double Mod(double value, double modulus) => ((value % modulus) + modulus) % modulus;
}

/// <summary>
/// The single answer to "how high is the sun, for scheduling purposes, right now?": the real sun
/// at the person's location, or the synthetic one from their fixed times. Everything that used to
/// call the solar calculator directly asks this instead.
/// </summary>
public static class ScheduleSource
{
    /// <summary>True when fixed clock times are selected and valid (an invalid saved pair falls back to the sun rather than breaking the schedule).</summary>
    public static bool UsesFixedTimes(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return string.Equals(settings.ScheduleMode, ScheduleModes.FixedTimes, StringComparison.Ordinal)
            && FixedTimeSchedule.Validate(settings.FixedSunriseLocal, settings.FixedSunsetLocal) is null;
    }

    public static double GetElevationDegrees(AppSettings settings, DateTime utcNow, DateTime localNow)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (UsesFixedTimes(settings)
            && FixedTimeSchedule.TryParseTimeOfDay(settings.FixedSunriseLocal, out TimeSpan rise)
            && FixedTimeSchedule.TryParseTimeOfDay(settings.FixedSunsetLocal, out TimeSpan set))
        {
            return FixedTimeSchedule.GetSyntheticElevationDegrees(localNow.TimeOfDay, rise, set);
        }

        return SolarCalculator.GetSolarElevationDegrees(utcNow, settings.Latitude, settings.Longitude);
    }
}
