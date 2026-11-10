using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class GammaRampMathTests
{
    private static GammaRamp CalibratedLikeCurve()
    {
        // A mild calibration curve: slightly lifted mid-tones, full white, true black.
        var ramp = GammaRamp.Identity();
        for (int i = 0; i < GammaRamp.Size; i++)
        {
            double x = i / 255.0;
            ramp.Red[i] = (ushort)Math.Round(Math.Pow(x, 0.95) * 65535);
            ramp.Green[i] = (ushort)Math.Round(Math.Pow(x, 1.0) * 65535);
            ramp.Blue[i] = (ushort)Math.Round(Math.Pow(x, 1.05) * 65535);
        }
        return ramp;
    }

    [Fact]
    public void Scale_WithIdentityBaseline_MatchesThePreviousHardCodedFormula()
    {
        var factors = (0.9, 0.8, 0.6);
        GammaRamp ramp = GammaRampMath.Scale(GammaRamp.Identity(), factors, 0.9);

        for (int i = 0; i < GammaRamp.Size; i++)
        {
            Assert.Equal((ushort)Math.Clamp(i * 257.0 * 0.9 * 0.9, 0, 65535), ramp.Red[i]);
            Assert.Equal((ushort)Math.Clamp(i * 257.0 * 0.8 * 0.9, 0, 65535), ramp.Green[i]);
            Assert.Equal((ushort)Math.Clamp(i * 257.0 * 0.6 * 0.9, 0, 65535), ramp.Blue[i]);
        }
    }

    [Fact]
    public void Scale_WithNeutralFactors_ReproducesACalibratedBaselineExactly()
    {
        GammaRamp baseline = CalibratedLikeCurve();

        GammaRamp ramp = GammaRampMath.Scale(baseline, (1.0, 1.0, 1.0), 1.0);

        Assert.Equal(baseline.Red, ramp.Red);
        Assert.Equal(baseline.Green, ramp.Green);
        Assert.Equal(baseline.Blue, ramp.Blue);
    }

    [Fact]
    public void ScaleWithContrast_WithIdentityBaseline_MatchesThePreviousFormula()
    {
        var factors = (1.0, 0.9, 0.7);
        GammaRamp ramp = GammaRampMath.ScaleWithContrast(GammaRamp.Identity(), factors, 0.2);

        for (int i = 0; i < GammaRamp.Size; i++)
        {
            double compressed = ColorTemperature.ApplyContrastCompression(i / 255.0, 0.2);
            Assert.InRange(ramp.Red[i], (int)(compressed * 65535.0 * 1.0) - 1, (int)(compressed * 65535.0 * 1.0) + 1);
            Assert.InRange(ramp.Green[i], (int)(compressed * 65535.0 * 0.9) - 1, (int)(compressed * 65535.0 * 0.9) + 1);
            Assert.InRange(ramp.Blue[i], (int)(compressed * 65535.0 * 0.7) - 1, (int)(compressed * 65535.0 * 0.7) + 1);
        }
    }

    [Fact]
    public void ScaleWithContrast_WithZeroReduction_ReproducesTheBaseline()
    {
        GammaRamp baseline = CalibratedLikeCurve();

        GammaRamp ramp = GammaRampMath.ScaleWithContrast(baseline, (1.0, 1.0, 1.0), 0.0);

        for (int i = 0; i < GammaRamp.Size; i++)
        {
            Assert.InRange(ramp.Red[i], baseline.Red[i] - 1, baseline.Red[i] + 1);
            Assert.InRange(ramp.Blue[i], baseline.Blue[i] - 1, baseline.Blue[i] + 1);
        }
    }

    [Fact]
    public void IsPlausibleBaseline_AcceptsIdentityAndACalibrationLikeCurve()
    {
        Assert.True(GammaRampMath.IsPlausibleBaseline(GamaIdentity()));
        Assert.True(GammaRampMath.IsPlausibleBaseline(CalibratedLikeCurve()));
    }

    [Fact]
    public void IsPlausibleBaseline_RejectsAWarmDimOrContrastRaisedLeftover()
    {
        // The app's own night ramp (blue scaled to ~53%) must not be adopted as "native".
        Assert.False(GammaRampMath.IsPlausibleBaseline(GammaRampMath.Scale(GammaRamp.Identity(), (1.0, 0.85, 0.53), 1.0)));
        // Brightness assist (all channels scaled down).
        Assert.False(GammaRampMath.IsPlausibleBaseline(GammaRampMath.Scale(GammaRamp.Identity(), (1.0, 1.0, 1.0), 0.85)));
        // Migraine contrast reduction (raised black floor).
        Assert.False(GammaRampMath.IsPlausibleBaseline(GammaRampMath.ScaleWithContrast(GammaRamp.Identity(), (1.0, 1.0, 1.0), 0.15)));
    }

    [Fact]
    public void IsPlausibleBaseline_RejectsNullWrongShapeAndNonMonotonicRamps()
    {
        Assert.False(GammaRampMath.IsPlausibleBaseline(null));
        Assert.False(GammaRampMath.IsPlausibleBaseline(new GammaRamp(new ushort[10], new ushort[10], new ushort[10])));

        GammaRamp jagged = GammaRamp.Identity();
        jagged.Green[100] = 0;
        Assert.False(GammaRampMath.IsPlausibleBaseline(jagged));
    }

    private static GammaRamp GamaIdentity() => GammaRamp.Identity();
}
