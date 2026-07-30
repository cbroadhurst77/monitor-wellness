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
