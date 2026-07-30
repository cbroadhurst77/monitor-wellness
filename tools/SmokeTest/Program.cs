using MonitorWellness.Core;

// London, late July: solar noon elevation should be roughly 55-60 deg. Sunrise/sunset should
// read close to 0 deg elevation. Extended to verify the new deep-night (bedtime-like) phase
// added after the color/brightness research pass -- see IMPLEMENTATION.md.
const double lat = 51.5072;
const double lon = -0.1276;
const int dayKelvin = 6500, nightKelvin = 3400;
const double dayBrightness = 1.0, nightBrightness = 0.85, deepNightBrightness = 0.7;

void Report(string label, DateTime utc)
{
    double elevation = SolarCalculator.GetSolarElevationDegrees(utc, lat, lon);
    int kelvin = ScheduleCurve.GetTargetKelvin(elevation, dayKelvin, nightKelvin);
    double nightPhaseBrightness = ScheduleCurve.GetTargetBrightness(elevation, dayBrightness, nightBrightness);
    double deepFactor = ScheduleCurve.GetDeepNightFactor(elevation);
    double finalBrightness = nightPhaseBrightness + (deepNightBrightness - nightPhaseBrightness) * deepFactor;
    Console.WriteLine($"{label,-28} {utc:yyyy-MM-dd HH:mm} UTC  elevation={elevation,7:F2}deg  kelvin={kelvin}  deepNightFactor={deepFactor:F2}  brightness={finalBrightness:F2}");
}

var date = new DateTime(2026, 7, 30, 0, 0, 0, DateTimeKind.Utc);

Report("Solar noon (~13:05 UTC)", date.AddHours(13).AddMinutes(5));
Report("Sunrise (~04:15 UTC)", date.AddHours(4).AddMinutes(15));
Report("Sunset (~19:35 UTC)", date.AddHours(19).AddMinutes(35));
Report("Civil twilight end (~20:20 UTC)", date.AddHours(20).AddMinutes(20));
Report("Deep night midpoint (~21:00 UTC)", date.AddHours(21));
Report("Midnight (full deep night)", date);
Report("Mid-morning (09:00 UTC)", date.AddHours(9));
Report("Mid-twilight (~04:45 UTC)", date.AddHours(4).AddMinutes(45));
