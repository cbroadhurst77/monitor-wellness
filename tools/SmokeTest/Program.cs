using MonitorWellness.Core;

// London, late July: solar noon elevation should be roughly 58-60 deg (well below the
// 90-deg midsummer max at the poles, close to the UK's actual max since we're past the
// solstice but not by much). Sunrise/sunset should read close to 0 deg elevation.
const double lat = 51.5072;
const double lon = -0.1276;

void Report(string label, DateTime utc)
{
    double elevation = SolarCalculator.GetSolarElevationDegrees(utc, lat, lon);
    int kelvin = ScheduleCurve.GetTargetKelvin(elevation, 6500, 3400);
    double brightness = ScheduleCurve.GetTargetBrightness(elevation, 1.0, 0.85);
    Console.WriteLine($"{label,-22} {utc:yyyy-MM-dd HH:mm} UTC  elevation={elevation,7:F2}deg  kelvin={kelvin}  brightness={brightness:F2}");
}

var date = new DateTime(2026, 7, 30, 0, 0, 0, DateTimeKind.Utc);

Report("Solar noon (~13:05 UTC)", date.AddHours(13).AddMinutes(5));
Report("Sunrise (~04:15 UTC)", date.AddHours(4).AddMinutes(15));
Report("Sunset (~19:35 UTC)", date.AddHours(19).AddMinutes(35));
Report("Midnight", date);
Report("Mid-morning (09:00 UTC)", date.AddHours(9));
Report("Mid-twilight (~04:45 UTC)", date.AddHours(4).AddMinutes(45));
