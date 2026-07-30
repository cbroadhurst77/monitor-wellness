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
}
