using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class AmbientLightSmootherTests
{
    [Fact]
    public void FirstReading_AppliesItsBoundedTarget()
    {
        var smoother = new AmbientLightSmoother();

        Assert.Equal(0.10, smoother.Update(0.10), precision: 10);
    }

    [Fact]
    public void SuddenChange_IsLimitedToMaximumStep()
    {
        var smoother = new AmbientLightSmoother();
        smoother.Update(-AmbientLightAdapter.MaxAdjustment);

        double adjustment = smoother.Update(AmbientLightAdapter.MaxAdjustment);

        Assert.Equal(-AmbientLightAdapter.MaxAdjustment + AmbientLightSmoother.MaximumStep, adjustment, precision: 10);
    }

    [Fact]
    public void Reset_AllowsNextReadingToBecomeNewBaseline()
    {
        var smoother = new AmbientLightSmoother();
        smoother.Update(-AmbientLightAdapter.MaxAdjustment);
        smoother.Reset();

        Assert.Equal(AmbientLightAdapter.MaxAdjustment, smoother.Update(AmbientLightAdapter.MaxAdjustment), precision: 10);
    }
}
