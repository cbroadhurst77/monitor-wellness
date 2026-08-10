using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class BrightnessSafetyTests
{
    [Fact]
    public void HighMultiplier_CannotBypassSafetyFloor()
    {
        double brightness = BrightnessSafety.CalculateEffectiveBrightness(0.05, 5.0);

        Assert.Equal(AppSettingsValidator.MinimumSafeBrightness, brightness);
    }

    [Fact]
    public void NeutralMultiplier_PreservesGlobalBrightness()
    {
        double brightness = BrightnessSafety.CalculateEffectiveBrightness(0.70, 1.0);

        Assert.Equal(0.70, brightness);
    }

    [Fact]
    public void PrimaryMonitor_HasHigherRecoveryFloor()
    {
        double brightness = BrightnessSafety.CalculateEffectiveBrightness(0.05, 5.0, isPrimaryMonitor: true);

        Assert.Equal(BrightnessSafety.MinimumPrimaryMonitorBrightness, brightness);
    }
}
