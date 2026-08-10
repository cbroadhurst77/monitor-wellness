using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class SensoryComfortPlansTests
{
    [Fact]
    public void RecoveryPlan_UsesSafeLowStimulationValues()
    {
        var settings = new AppSettings();

        bool applied = SensoryComfortPlans.Apply(SensoryComfortPlans.Recovery, settings);

        Assert.True(applied);
        Assert.Equal(4600, settings.DayKelvin);
        Assert.Equal(0.60, settings.DayBrightness);
        Assert.Equal(0.40, settings.DeepNightBrightness);
        Assert.Equal(MigraineResponsePlans.Strong, settings.DefaultMigraineResponsePlan);
        Assert.True(AppSettingsValidator.TryValidate(settings, out _));
    }

    [Fact]
    public void UnknownPlan_LeavesSettingsUnchanged()
    {
        var settings = new AppSettings { DayBrightness = 0.91 };

        bool applied = SensoryComfortPlans.Apply("not-a-plan", settings);

        Assert.False(applied);
        Assert.Equal(0.91, settings.DayBrightness);
    }
}
