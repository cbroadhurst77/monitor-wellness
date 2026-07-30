namespace MonitorWellness.Core;

/// <summary>
/// Solar position calculations based on the NOAA solar position algorithm
/// (Meeus, "Astronomical Algorithms"). Accurate to within about 0.01 degrees,
/// which is far more precision than a display schedule needs.
/// </summary>
public static class SolarCalculator
{
    /// <summary>
    /// Returns the sun's elevation angle in degrees above the horizon for a given
    /// UTC instant and observer location. Negative values are below the horizon.
    /// </summary>
    public static double GetSolarElevationDegrees(DateTime utc, double latitudeDeg, double longitudeDeg)
    {
        double julianDay = ToJulianDay(utc);
        double t = (julianDay - 2451545.0) / 36525.0;

        double l0 = NormalizeDegrees(280.46646 + t * (36000.76983 + t * 0.0003032));
        double m = 357.52911 + t * (35999.05029 - 0.0001537 * t);
        double mRad = ToRad(m);
        double eccentricity = 0.016708634 - t * (0.000042037 + 0.0000001267 * t);

        double c = Math.Sin(mRad) * (1.914602 - t * (0.004817 + 0.000014 * t))
                 + Math.Sin(2 * mRad) * (0.019993 - 0.000101 * t)
                 + Math.Sin(3 * mRad) * 0.000289;

        double trueLongitude = l0 + c;

        double omega = 125.04 - 1934.136 * t;
        double apparentLongitude = trueLongitude - 0.00569 - 0.00478 * Math.Sin(ToRad(omega));

        double meanObliquity = 23.0 + (26.0 + (21.448 - t * (46.815 + t * (0.00059 - t * 0.001813))) / 60.0) / 60.0;
        double obliquityCorrected = meanObliquity + 0.00256 * Math.Cos(ToRad(omega));

        double declinationRad = Math.Asin(Math.Sin(ToRad(obliquityCorrected)) * Math.Sin(ToRad(apparentLongitude)));

        double y = Math.Pow(Math.Tan(ToRad(obliquityCorrected) / 2.0), 2);
        double l0Rad = ToRad(l0);
        double equationOfTimeMinutes = 4.0 * ToDeg(
            y * Math.Sin(2 * l0Rad)
            - 2 * eccentricity * Math.Sin(mRad)
            + 4 * eccentricity * y * Math.Sin(mRad) * Math.Cos(2 * l0Rad)
            - 0.5 * y * y * Math.Sin(4 * l0Rad)
            - 1.25 * eccentricity * eccentricity * Math.Sin(2 * mRad));

        double utcHours = utc.Hour + utc.Minute / 60.0 + utc.Second / 3600.0;
        double solarTimeHours = utcHours + longitudeDeg / 15.0 + equationOfTimeMinutes / 60.0;
        double hourAngleDeg = (solarTimeHours - 12.0) * 15.0;

        double latRad = ToRad(latitudeDeg);
        double hourAngleRad = ToRad(hourAngleDeg);

        double sinElevation = Math.Sin(latRad) * Math.Sin(declinationRad)
                             + Math.Cos(latRad) * Math.Cos(declinationRad) * Math.Cos(hourAngleRad);

        return ToDeg(Math.Asin(Math.Clamp(sinElevation, -1.0, 1.0)));
    }

    /// <summary>
    /// Standard sunrise/sunset elevation threshold: the sun's center at -0.833 degrees,
    /// accounting for its ~0.25-degree apparent radius plus ~0.567 degrees of atmospheric
    /// refraction near the horizon. This is the conventional definition used by almanacs and
    /// most published sunrise/sunset times, so results here should roughly match those.
    /// </summary>
    private const double SunriseSunsetThresholdDeg = -0.833;

    /// <summary>Finds when the sun crosses the sunrise threshold (ascending) on the given UTC date, or null if it doesn't (polar day/night).</summary>
    public static DateTime? FindSunriseUtc(DateTime dateUtc, double latitudeDeg, double longitudeDeg)
        => FindElevationCrossing(dateUtc, latitudeDeg, longitudeDeg, ascending: true);

    /// <summary>Finds when the sun crosses the sunset threshold (descending) on the given UTC date, or null if it doesn't (polar day/night).</summary>
    public static DateTime? FindSunsetUtc(DateTime dateUtc, double latitudeDeg, double longitudeDeg)
        => FindElevationCrossing(dateUtc, latitudeDeg, longitudeDeg, ascending: false);

    /// <summary>
    /// Coarse scan across the day in 5-minute steps to bracket the crossing, then bisects
    /// within that bracket for sub-second precision. Simple and robust rather than a closed-
    /// form solution — elevation isn't perfectly monotonic across a full day in every edge
    /// case (e.g. very high latitudes near the equinox), but is monotonic enough within any
    /// single 5-minute window for bisection to behave correctly.
    /// </summary>
    private static DateTime? FindElevationCrossing(DateTime dateUtc, double latitudeDeg, double longitudeDeg, bool ascending)
    {
        DateTime dayStart = dateUtc.Date;
        const int stepMinutes = 5;
        int steps = (24 * 60) / stepMinutes;

        double previousElevation = GetSolarElevationDegrees(dayStart, latitudeDeg, longitudeDeg);
        for (int i = 1; i <= steps; i++)
        {
            DateTime t = dayStart.AddMinutes(i * stepMinutes);
            double elevation = GetSolarElevationDegrees(t, latitudeDeg, longitudeDeg);

            bool crossed = ascending
                ? previousElevation < SunriseSunsetThresholdDeg && elevation >= SunriseSunsetThresholdDeg
                : previousElevation >= SunriseSunsetThresholdDeg && elevation < SunriseSunsetThresholdDeg;

            if (crossed)
                return Bisect(dayStart.AddMinutes((i - 1) * stepMinutes), t, latitudeDeg, longitudeDeg, ascending);

            previousElevation = elevation;
        }

        return null;
    }

    private static DateTime Bisect(DateTime lo, DateTime hi, double latitudeDeg, double longitudeDeg, bool ascending)
    {
        for (int i = 0; i < 20; i++) // 20 halvings of a 5-minute bracket gives sub-second precision
        {
            DateTime mid = lo + TimeSpan.FromTicks((hi - lo).Ticks / 2);
            double elevation = GetSolarElevationDegrees(mid, latitudeDeg, longitudeDeg);
            bool crossingStillAhead = ascending
                ? elevation < SunriseSunsetThresholdDeg
                : elevation >= SunriseSunsetThresholdDeg;

            if (crossingStillAhead) lo = mid; else hi = mid;
        }
        return lo + TimeSpan.FromTicks((hi - lo).Ticks / 2);
    }

    private static double ToJulianDay(DateTime utc)
    {
        // OLE Automation epoch (Dec 30 1899, 00:00 UTC) corresponds to JD 2415018.5.
        return utc.ToOADate() + 2415018.5;
    }

    private static double NormalizeDegrees(double degrees)
    {
        double d = degrees % 360.0;
        return d < 0 ? d + 360.0 : d;
    }

    private static double ToRad(double degrees) => degrees * Math.PI / 180.0;

    private static double ToDeg(double radians) => radians * 180.0 / Math.PI;
}
