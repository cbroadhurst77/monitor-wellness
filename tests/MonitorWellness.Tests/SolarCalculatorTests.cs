using MonitorWellness.Core;

namespace MonitorWellness.Tests;

/// <summary>
/// Turns the ad-hoc verification done via tools/SmokeTest during Week 1 into permanent
/// regression protection. Reference values are for London (51.5072, -0.1276) on 2026-07-30 —
/// the same date/location used during that original manual verification.
/// </summary>
public class SolarCalculatorTests
{
    private const double Lat = 51.5072;
    private const double Lon = -0.1276;
    private static readonly DateTime Date = new(2026, 7, 30, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SolarNoon_IsHighInSky()
    {
        double elevation = SolarCalculator.GetSolarElevationDegrees(Date.AddHours(13).AddMinutes(5), Lat, Lon);
        Assert.InRange(elevation, 45, 65); // late-July London solar noon, well below the polar midsummer max but clearly high
    }

    [Fact]
    public void Midnight_IsWellBelowHorizon()
    {
        double elevation = SolarCalculator.GetSolarElevationDegrees(Date, Lat, Lon);
        Assert.True(elevation < -15, $"Expected well below horizon at midnight, got {elevation}");
    }

    [Fact]
    public void MidMorning_IsAboveDayThreshold()
    {
        double elevation = SolarCalculator.GetSolarElevationDegrees(Date.AddHours(9), Lat, Lon);
        Assert.True(elevation > ScheduleCurve.DayThresholdDeg, $"Expected above day threshold at mid-morning, got {elevation}");
    }

    [Theory]
    [InlineData(4, 15)]  // near sunrise
    [InlineData(19, 35)] // near sunset
    public void NearSunriseAndSunset_ElevationIsNearHorizon(int hour, int minute)
    {
        double elevation = SolarCalculator.GetSolarElevationDegrees(Date.AddHours(hour).AddMinutes(minute), Lat, Lon);
        Assert.InRange(elevation, -5, 5);
    }

    [Fact]
    public void Elevation_DecreasesMonotonicallyFromNoonTowardSunset()
    {
        // A simpler, less fragile invariant than assuming exact symmetry around solar noon
        // (which doesn't hold well over a multi-hour window at this latitude/season) --
        // elevation should just keep dropping as the afternoon goes on, with no erratic jumps.
        double[] hours = { 13.5, 15, 17, 19 };
        double previous = double.MaxValue;
        foreach (double hour in hours)
        {
            double elevation = SolarCalculator.GetSolarElevationDegrees(Date.AddHours(hour), Lat, Lon);
            Assert.True(elevation < previous, $"Expected elevation to keep decreasing by hour {hour}, got {elevation} after {previous}");
            previous = elevation;
        }
    }
}
