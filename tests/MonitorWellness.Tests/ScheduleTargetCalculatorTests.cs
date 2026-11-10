using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class ScheduleTargetCalculatorTests
{
    private static readonly DateTime Noon = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Local);

    private static ScheduleTargetInputs Inputs(double elevation, string? bedtime = null, DateTime? now = null) => new(
        DayKelvin: 6500,
        NightKelvin: 3400,
        DayBrightness: 1.0,
        NightBrightness: 0.8,
        DeepNightBrightness: 0.6,
        SolarElevationDegrees: elevation,
        BedtimeLocal: bedtime,
        NowLocal: now ?? Noon,
        DeepNightOverlayColorHex: "#190C04");

    [Fact]
    public void Full_day_uses_day_values_and_a_black_overlay()
    {
        var target = ScheduleTargetCalculator.Compute(Inputs(45));

        Assert.Equal(6500, target.Kelvin);
        Assert.Equal(1.0, target.GlobalBrightness, 3);
        Assert.Equal(System.Windows.Media.Colors.Black, target.DimColor);
    }

    [Fact]
    public void Deep_night_uses_deep_night_brightness_and_the_warm_overlay()
    {
        var target = ScheduleTargetCalculator.Compute(Inputs(-30));

        Assert.Equal(3400, target.Kelvin);
        Assert.Equal(0.6, target.GlobalBrightness, 3);
        Assert.Equal(0x19, target.DimColor.R);
        Assert.Equal(0x0C, target.DimColor.G);
        Assert.Equal(0x04, target.DimColor.B);
    }

    [Fact]
    public void Bedtime_can_force_deep_night_while_the_sun_is_still_up()
    {
        var atBedtime = new DateTime(2026, 11, 10, 22, 30, 0, DateTimeKind.Local);

        var target = ScheduleTargetCalculator.Compute(Inputs(45, "22:30", atBedtime));

        Assert.Equal(0.6, target.GlobalBrightness, 3);
    }

    [Fact]
    public void Unparseable_bedtime_is_ignored()
    {
        var target = ScheduleTargetCalculator.Compute(Inputs(45, "not a time"));

        Assert.Equal(1.0, target.GlobalBrightness, 3);
    }

    [Fact]
    public void Ambient_adjustment_is_added_and_clamped_to_one()
    {
        double? seenDayFactor = null;

        var target = ScheduleTargetCalculator.Compute(Inputs(-30), df => { seenDayFactor = df; return 0.5; });

        Assert.Equal(0.0, seenDayFactor!.Value, 3);
        Assert.Equal(1.0, target.GlobalBrightness, 3);
    }

    [Fact]
    public void Ambient_without_a_reading_leaves_brightness_untouched()
    {
        var target = ScheduleTargetCalculator.Compute(Inputs(-30), _ => null);

        Assert.Equal(0.6, target.GlobalBrightness, 3);
    }

    [Fact]
    public void Ambient_callback_is_not_invoked_when_matching_is_off()
    {
        // Null means "off": nothing to call, nothing to smooth.
        var target = ScheduleTargetCalculator.Compute(Inputs(-30), null);

        Assert.Equal(0.6, target.GlobalBrightness, 3);
    }
}
