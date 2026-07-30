using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class AmbientLightAdapterTests
{
    [Fact]
    public void AtOrBelowDimLux_ReturnsMaxNegativeAdjustment()
    {
        Assert.Equal(-AmbientLightAdapter.MaxAdjustment, AmbientLightAdapter.ComputeBrightnessAdjustment(AmbientLightAdapter.DimLux));
        Assert.Equal(-AmbientLightAdapter.MaxAdjustment, AmbientLightAdapter.ComputeBrightnessAdjustment(0));
        Assert.Equal(-AmbientLightAdapter.MaxAdjustment, AmbientLightAdapter.ComputeBrightnessAdjustment(-5)); // defensive: a bogus negative reading shouldn't exceed the floor
    }

    [Fact]
    public void AtOrAboveBrightLux_ReturnsMaxPositiveAdjustment()
    {
        Assert.Equal(AmbientLightAdapter.MaxAdjustment, AmbientLightAdapter.ComputeBrightnessAdjustment(AmbientLightAdapter.BrightLux));
        Assert.Equal(AmbientLightAdapter.MaxAdjustment, AmbientLightAdapter.ComputeBrightnessAdjustment(50_000));
    }

    [Fact]
    public void AtReferenceLux_AdjustmentIsZero()
    {
        Assert.Equal(0.0, AmbientLightAdapter.ComputeBrightnessAdjustment(AmbientLightAdapter.ReferenceLux), precision: 10);
    }

    [Fact]
    public void BelowReference_AdjustmentIsNegative()
    {
        double adjustment = AmbientLightAdapter.ComputeBrightnessAdjustment(100);
        Assert.True(adjustment < 0);
        Assert.True(adjustment >= -AmbientLightAdapter.MaxAdjustment);
    }

    [Fact]
    public void AboveReference_AdjustmentIsPositive()
    {
        double adjustment = AmbientLightAdapter.ComputeBrightnessAdjustment(1000);
        Assert.True(adjustment > 0);
        Assert.True(adjustment <= AmbientLightAdapter.MaxAdjustment);
    }

    [Fact]
    public void AdjustmentIsMonotonicallyIncreasingWithLux()
    {
        double[] luxSamples = { 0, 10, 20, 50, 150, 300, 600, 1200, 2000, 5000 };
        double previous = double.NegativeInfinity;
        foreach (double lux in luxSamples)
        {
            double adjustment = AmbientLightAdapter.ComputeBrightnessAdjustment(lux);
            Assert.True(adjustment >= previous, $"Adjustment at {lux} lux ({adjustment}) should be >= previous ({previous})");
            previous = adjustment;
        }
    }

    [Fact]
    public void AdjustmentNeverExceedsMaxAdjustmentInEitherDirection()
    {
        foreach (double lux in new[] { -100, 0, 10, 20, 300, 2000, 10000, 1_000_000 })
        {
            double adjustment = AmbientLightAdapter.ComputeBrightnessAdjustment(lux);
            Assert.InRange(adjustment, -AmbientLightAdapter.MaxAdjustment, AmbientLightAdapter.MaxAdjustment);
        }
    }
}
