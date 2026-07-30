using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class SunriseSunsetTests
{
    private const double Lat = 51.5072; // London
    private const double Lon = -0.1276;
    private static readonly DateTime Date = new(2026, 7, 30, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FindSunriseUtc_IsBeforeFindSunsetUtc()
    {
        var sunrise = SolarCalculator.FindSunriseUtc(Date, Lat, Lon);
        var sunset = SolarCalculator.FindSunsetUtc(Date, Lat, Lon);

        Assert.NotNull(sunrise);
        Assert.NotNull(sunset);
        Assert.True(sunrise < sunset, $"Expected sunrise ({sunrise}) before sunset ({sunset})");
    }

    [Fact]
    public void FindSunriseUtc_ElevationAtThatMomentIsNearThreshold()
    {
        var sunrise = SolarCalculator.FindSunriseUtc(Date, Lat, Lon);
        Assert.NotNull(sunrise);

        double elevation = SolarCalculator.GetSolarElevationDegrees(sunrise.Value, Lat, Lon);
        Assert.InRange(elevation, -1.5, 0.5); // should sit right around the -0.833 threshold
    }

    [Fact]
    public void FindSunsetUtc_ElevationAtThatMomentIsNearThreshold()
    {
        var sunset = SolarCalculator.FindSunsetUtc(Date, Lat, Lon);
        Assert.NotNull(sunset);

        double elevation = SolarCalculator.GetSolarElevationDegrees(sunset.Value, Lat, Lon);
        Assert.InRange(elevation, -1.5, 0.5);
    }

    [Fact]
    public void FindSunriseUtc_IsInPlausibleRangeForLondonLateJuly()
    {
        // London late July: sunrise is roughly 03:50-04:10 UTC (04:50-05:10 BST).
        var sunrise = SolarCalculator.FindSunriseUtc(Date, Lat, Lon);
        Assert.NotNull(sunrise);
        Assert.InRange(sunrise.Value.TimeOfDay, TimeSpan.FromHours(3), TimeSpan.FromHours(5));
    }

    [Fact]
    public void FindSunsetUtc_IsInPlausibleRangeForLondonLateJuly()
    {
        // London late July: sunset is roughly 19:45-20:15 UTC (20:45-21:15 BST).
        var sunset = SolarCalculator.FindSunsetUtc(Date, Lat, Lon);
        Assert.NotNull(sunset);
        Assert.InRange(sunset.Value.TimeOfDay, TimeSpan.FromHours(19), TimeSpan.FromHours(21));
    }

    [Fact]
    public void NearPolarLocation_MidsummerHasNoSunset()
    {
        // Well above the Arctic Circle in midsummer, the sun never sets -- FindSunsetUtc
        // should return null rather than a wrong answer or an infinite loop.
        var midsummer = new DateTime(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc);
        var sunset = SolarCalculator.FindSunsetUtc(midsummer, 78.0, 15.0); // Svalbard
        Assert.Null(sunset);
    }
}
