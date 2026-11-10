using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class ScheduleBrightnessTargetsTests
{
    private const string Display1 = @"\\.\DISPLAY1";
    private const string Display2 = @"\\.\DISPLAY2";
    private static readonly string[] TwoDisplays = { Display1, Display2 };
    private static readonly string[] OnlyDisplay2 = { Display2 };
    private static readonly IReadOnlySet<string> None = new HashSet<string>();
    private static readonly IReadOnlyDictionary<string, double> NoMultipliers = new Dictionary<string, double>();

    [Fact]
    public void Overlay_device_without_gamma_controller_still_receives_dimming()
    {
        // The device set models overlay windows. DISPLAY2 has an overlay but no gamma
        // controller; nothing here references gamma at all, which is the point.
        var result = ScheduleBrightnessTargets.Build(TwoDisplays, 0.5, None, NoMultipliers, None);

        Assert.Equal(0.5, result[Display2], 3);
        Assert.Equal(0.5, result[Display1], 3);
    }

    [Fact]
    public void Excluded_device_is_left_at_full_brightness()
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Display2 };

        var result = ScheduleBrightnessTargets.Build(TwoDisplays, 0.4, excluded, NoMultipliers, None);

        Assert.Equal(1.0, result[Display2]);
        Assert.True(result[Display1] < 1.0);
    }

    [Fact]
    public void Primary_monitor_keeps_safety_floor_even_with_large_multiplier()
    {
        var primary = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Display1 };
        var multipliers = new Dictionary<string, double> { [Display1] = 5.0, [Display2] = 5.0 };

        var result = ScheduleBrightnessTargets.Build(TwoDisplays, 0.1, None, multipliers, primary);

        Assert.Equal(BrightnessSafety.MinimumPrimaryMonitorBrightness, result[Display1], 3);
        Assert.Equal(AppSettingsValidator.MinimumSafeBrightness, result[Display2], 3);
    }

    [Fact]
    public void Per_monitor_multiplier_applies_to_gamma_less_display()
    {
        var multipliers = new Dictionary<string, double> { [Display2] = 0.0 };

        var result = ScheduleBrightnessTargets.Build(OnlyDisplay2, 0.5, None, multipliers, None);

        Assert.Equal(1.0, result[Display2], 3);
    }
}
