using MonitorWellness.Core;

namespace MonitorWellness.Tests;

/// <summary>
/// IsSafeForGammaRamp's expected true/false values below are not theoretical -- they're
/// pinned to what tools/GammaCheck actually confirmed against real hardware during this
/// project (3400K passes, 3000K and 2500K are rejected by the driver). If this threshold or
/// the underlying blackbody approximation ever changes, these tests catching a regression
/// against real observed hardware behavior matters more than internal consistency alone.
/// </summary>
public class ColorTemperatureTests
{
    [Fact]
    public void At6500K_FactorsAreNearIdentity()
    {
        var (r, g, b) = ColorTemperature.KelvinToRgbFactors(6500);
        Assert.Equal(1.0, r, precision: 2);
        Assert.InRange(g, 0.95, 1.0);
        Assert.InRange(b, 0.95, 1.0);
    }

    [Fact]
    public void BlueFactor_DecreasesAsKelvinDecreases()
    {
        var (_, _, blueAt6500) = ColorTemperature.KelvinToRgbFactors(6500);
        var (_, _, blueAt4000) = ColorTemperature.KelvinToRgbFactors(4000);
        var (_, _, blueAt2500) = ColorTemperature.KelvinToRgbFactors(2500);

        Assert.True(blueAt6500 > blueAt4000, "Blue factor should drop as color temp warms");
        Assert.True(blueAt4000 > blueAt2500, "Blue factor should keep dropping as color temp warms further");
    }

    [Fact]
    public void RedFactor_StaysAtMaximumAcrossWarmValues()
    {
        // The blackbody approximation clamps red to full at any temp <= 6600K -- warming
        // shifts by reducing green/blue, not increasing red beyond identity.
        foreach (int kelvin in new[] { 6500, 4000, 3400, 2500 })
        {
            var (r, _, _) = ColorTemperature.KelvinToRgbFactors(kelvin);
            Assert.Equal(1.0, r, precision: 3);
        }
    }

    [Theory]
    [InlineData(6500, true)]
    [InlineData(4000, true)]
    [InlineData(3400, true)]  // confirmed safe floor on real hardware (tools/GammaCheck)
    [InlineData(3000, false)] // confirmed rejected on real hardware
    [InlineData(2500, false)] // confirmed rejected on real hardware
    public void IsSafeForGammaRamp_MatchesConfirmedRealHardwareBehavior(int kelvin, bool expectedSafe)
    {
        Assert.Equal(expectedSafe, ColorTemperature.IsSafeForGammaRamp(kelvin));
    }

    [Fact]
    public void ApplyContrastCompression_ZeroReduction_IsIdentity()
    {
        Assert.Equal(0.0, ColorTemperature.ApplyContrastCompression(0.0, 0.0), precision: 6);
        Assert.Equal(0.5, ColorTemperature.ApplyContrastCompression(0.5, 0.0), precision: 6);
        Assert.Equal(1.0, ColorTemperature.ApplyContrastCompression(1.0, 0.0), precision: 6);
    }

    [Fact]
    public void ApplyContrastCompression_RaisesFloorWithoutTouchingCeiling()
    {
        // At any reduction amount, input 0 should rise to exactly the reduction fraction,
        // while input 1 (the ceiling) should stay untouched at 1 -- confirmed on real
        // hardware (tools/GammaCheck) that this specific shape is what the driver accepts,
        // as opposed to uniformly scaling the whole range down.
        foreach (double reduction in new[] { 0.1, 0.15, 0.2, 0.3 })
        {
            Assert.Equal(reduction, ColorTemperature.ApplyContrastCompression(0.0, reduction), precision: 6);
            Assert.Equal(1.0, ColorTemperature.ApplyContrastCompression(1.0, reduction), precision: 6);
        }
    }

    [Fact]
    public void ApplyContrastCompression_IsMonotonicIncreasing()
    {
        double previous = ColorTemperature.ApplyContrastCompression(0.0, 0.2);
        for (double n = 0.1; n <= 1.0; n += 0.1)
        {
            double value = ColorTemperature.ApplyContrastCompression(n, 0.2);
            Assert.True(value > previous, "Contrast-compressed ramp must stay monotonically increasing");
            previous = value;
        }
    }
}
