using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class ScheduleCurveTests
{
    private const int DayKelvin = 6500;
    private const int NightKelvin = 3400;
    private const double DayBrightness = 1.0;
    private const double NightBrightness = 0.85;

    [Fact]
    public void GetTargetKelvin_AtFullDay_ReturnsDayValue()
    {
        int kelvin = ScheduleCurve.GetTargetKelvin(ScheduleCurve.DayThresholdDeg + 10, DayKelvin, NightKelvin);
        Assert.Equal(DayKelvin, kelvin);
    }

    [Fact]
    public void GetTargetKelvin_AtFullNight_ReturnsNightValue()
    {
        int kelvin = ScheduleCurve.GetTargetKelvin(ScheduleCurve.NightThresholdDeg - 10, DayKelvin, NightKelvin);
        Assert.Equal(NightKelvin, kelvin);
    }

    [Fact]
    public void GetTargetKelvin_MidTwilight_IsBetweenDayAndNight()
    {
        double midpoint = (ScheduleCurve.DayThresholdDeg + ScheduleCurve.NightThresholdDeg) / 2.0;
        int kelvin = ScheduleCurve.GetTargetKelvin(midpoint, DayKelvin, NightKelvin);
        Assert.InRange(kelvin, NightKelvin, DayKelvin);
        Assert.NotEqual(DayKelvin, kelvin);
        Assert.NotEqual(NightKelvin, kelvin);
    }

    [Fact]
    public void GetTargetBrightness_AtFullDay_ReturnsDayValue()
    {
        double brightness = ScheduleCurve.GetTargetBrightness(ScheduleCurve.DayThresholdDeg + 10, DayBrightness, NightBrightness);
        Assert.Equal(DayBrightness, brightness, precision: 6);
    }

    [Fact]
    public void GetTargetBrightness_AtFullNight_ReturnsNightValue()
    {
        double brightness = ScheduleCurve.GetTargetBrightness(ScheduleCurve.NightThresholdDeg - 10, DayBrightness, NightBrightness);
        Assert.Equal(NightBrightness, brightness, precision: 6);
    }

    [Fact]
    public void GetDeepNightFactor_AtNightThreshold_IsZero()
    {
        Assert.Equal(0.0, ScheduleCurve.GetDeepNightFactor(ScheduleCurve.NightThresholdDeg), precision: 6);
    }

    [Fact]
    public void GetDeepNightFactor_AtDeepNightThreshold_IsOne()
    {
        Assert.Equal(1.0, ScheduleCurve.GetDeepNightFactor(ScheduleCurve.DeepNightThresholdDeg), precision: 6);
    }

    [Fact]
    public void GetDeepNightFactor_Midpoint_IsAboutHalf()
    {
        double midpoint = (ScheduleCurve.NightThresholdDeg + ScheduleCurve.DeepNightThresholdDeg) / 2.0;
        double factor = ScheduleCurve.GetDeepNightFactor(midpoint);
        Assert.InRange(factor, 0.4, 0.6);
    }

    [Fact]
    public void GetDeepNightFactor_ClampsAboveNightThreshold()
    {
        // Well into full daytime -- must not go negative or otherwise misbehave.
        double factor = ScheduleCurve.GetDeepNightFactor(50.0);
        Assert.Equal(0.0, factor, precision: 6);
    }

    [Fact]
    public void GetDeepNightFactor_ClampsBelowDeepNightThreshold()
    {
        double factor = ScheduleCurve.GetDeepNightFactor(ScheduleCurve.DeepNightThresholdDeg - 20);
        Assert.Equal(1.0, factor, precision: 6);
    }

    [Fact]
    public void GetDayFactor_AtDayThreshold_IsOne()
    {
        Assert.Equal(1.0, ScheduleCurve.GetDayFactor(ScheduleCurve.DayThresholdDeg), precision: 6);
    }

    [Fact]
    public void GetDayFactor_AtNightThreshold_IsZero()
    {
        Assert.Equal(0.0, ScheduleCurve.GetDayFactor(ScheduleCurve.NightThresholdDeg), precision: 6);
    }

    [Fact]
    public void GetDayFactor_ClampsAboveDayThreshold()
    {
        Assert.Equal(1.0, ScheduleCurve.GetDayFactor(50.0), precision: 6);
    }

    [Fact]
    public void GetDayFactor_ClampsBelowNightThreshold()
    {
        Assert.Equal(0.0, ScheduleCurve.GetDayFactor(ScheduleCurve.NightThresholdDeg - 20), precision: 6);
    }

    [Fact]
    public void GetDayFactor_MatchesTheSameBlendUsedByGetTargetBrightness()
    {
        // GetDayFactor is meant to expose the exact same 0..1 blend GetTargetBrightness already
        // uses internally -- verified indirectly here: at the same elevation, the fraction of
        // the way from NightBrightness to DayBrightness should equal GetDayFactor's value.
        double midpoint = (ScheduleCurve.DayThresholdDeg + ScheduleCurve.NightThresholdDeg) / 2.0;
        double dayFactor = ScheduleCurve.GetDayFactor(midpoint);
        double brightness = ScheduleCurve.GetTargetBrightness(midpoint, DayBrightness, NightBrightness);
        double impliedFactor = (brightness - NightBrightness) / (DayBrightness - NightBrightness);

        Assert.Equal(dayFactor, impliedFactor, precision: 6);
    }

    private static readonly TimeSpan Bedtime2200 = new(22, 0, 0);

    [Fact]
    public void GetBedtimeFactor_WellBeforeRamp_IsZero()
    {
        var now = new DateTime(2024, 1, 15, 20, 0, 0); // 2 hours before 22:00 bedtime
        Assert.Equal(0.0, ScheduleCurve.GetBedtimeFactor(now, Bedtime2200), precision: 6);
    }

    [Fact]
    public void GetBedtimeFactor_AtStartOfRamp_IsZero()
    {
        var now = new DateTime(2024, 1, 15, 20, 30, 0); // exactly 90 min before bedtime
        Assert.Equal(0.0, ScheduleCurve.GetBedtimeFactor(now, Bedtime2200), precision: 6);
    }

    [Fact]
    public void GetBedtimeFactor_HalfwayThroughRamp_IsAboutHalf()
    {
        var now = new DateTime(2024, 1, 15, 21, 15, 0); // 45 min before bedtime, half the 90-min ramp
        double factor = ScheduleCurve.GetBedtimeFactor(now, Bedtime2200);
        Assert.Equal(0.5, factor, precision: 6);
    }

    [Fact]
    public void GetBedtimeFactor_AtBedtime_IsOne()
    {
        var now = new DateTime(2024, 1, 15, 22, 0, 0);
        Assert.Equal(1.0, ScheduleCurve.GetBedtimeFactor(now, Bedtime2200), precision: 6);
    }

    [Fact]
    public void GetBedtimeFactor_WellIntoNight_IsStillOne()
    {
        var now = new DateTime(2024, 1, 15, 22, 0, 0).AddMinutes(300); // 5 hours after bedtime, within the 600-min window
        Assert.Equal(1.0, ScheduleCurve.GetBedtimeFactor(now, Bedtime2200), precision: 6);
    }

    [Fact]
    public void GetBedtimeFactor_AtEndOfHoldWindow_IsStillOne()
    {
        var now = new DateTime(2024, 1, 15, 22, 0, 0).AddMinutes(600); // exactly maxPastMinutes after bedtime
        Assert.Equal(1.0, ScheduleCurve.GetBedtimeFactor(now, Bedtime2200), precision: 6);
    }

    [Fact]
    public void GetBedtimeFactor_HalfwayThroughMorningRampDown_IsAboutHalf()
    {
        var now = new DateTime(2024, 1, 15, 22, 0, 0).AddMinutes(600 + 45); // halfway through the 90-min ramp-down
        double factor = ScheduleCurve.GetBedtimeFactor(now, Bedtime2200);
        Assert.Equal(0.5, factor, precision: 6);
    }

    [Fact]
    public void GetBedtimeFactor_PastRampDown_IsZeroAgain()
    {
        var now = new DateTime(2024, 1, 15, 22, 0, 0).AddMinutes(600 + 90); // fully past the wind-down
        Assert.Equal(0.0, ScheduleCurve.GetBedtimeFactor(now, Bedtime2200), precision: 6);
    }

    [Fact]
    public void GetBedtimeFactor_BedtimeNearMidnight_JustAfterCrossesDayBoundaryCorrectly()
    {
        // Bedtime 23:30; "now" is 00:15 -- 45 minutes later, but the clock has wrapped past
        // midnight. Without day-rollover normalization this would look like ~23 hours away.
        var bedtime = new TimeSpan(23, 30, 0);
        var now = new DateTime(2024, 1, 15, 0, 15, 0);
        Assert.Equal(1.0, ScheduleCurve.GetBedtimeFactor(now, bedtime), precision: 6);
    }

    [Fact]
    public void GetBedtimeFactor_BedtimeJustAfterMidnight_RampsUpBeforeMidnight()
    {
        // Bedtime 00:30; "now" is 23:45 the previous night -- 45 minutes before bedtime,
        // which also wraps across the day boundary.
        var bedtime = new TimeSpan(0, 30, 0);
        var now = new DateTime(2024, 1, 15, 23, 45, 0);
        double factor = ScheduleCurve.GetBedtimeFactor(now, bedtime);
        Assert.Equal(0.5, factor, precision: 6);
    }
}
