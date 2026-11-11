#pragma warning disable CA1861 // small literal arrays in tests are clearer than shared static fields
using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class ScheduleThemeTests
{
    private sealed class FakeStore : IWindowsThemeStore
    {
        public bool? IsLight { get; set; } = true;
        public List<bool> Sets { get; } = new();
        public Exception? ThrowOnSet { get; set; }

        public void SetLight(bool light)
        {
            if (ThrowOnSet is not null)
                throw ThrowOnSet;
            Sets.Add(light);
            IsLight = light;
        }
    }

    [Theory]
    [InlineData(40.0, true)]
    [InlineData(3.0, true)]
    [InlineData(2.9, null)]
    [InlineData(0.0, null)]
    [InlineData(-5.9, null)]
    [InlineData(-6.0, false)]
    [InlineData(-30.0, false)]
    public void The_sun_decides_with_a_twilight_band_where_nothing_changes(double elevation, bool? expectedLight)
        => Assert.Equal(expectedLight, ScheduleTheme.WantsLight(elevation));

    [Fact]
    public void The_thresholds_are_the_colour_schedules_own()
    {
        Assert.Equal(ScheduleCurve.DayThresholdDeg, 3.0);
        Assert.Equal(ScheduleCurve.NightThresholdDeg, -6.0);
    }

    [Fact]
    public void Day_to_night_to_day_switches_once_each_way_and_twilight_never_flaps()
    {
        var store = new FakeStore();
        var follower = new ScheduleThemeFollower(store);

        follower.Update(true, 30);    // midday, already light
        follower.Update(true, 1);     // twilight
        follower.Update(true, -4);    // twilight
        Assert.Empty(store.Sets);

        follower.Update(true, -7);    // night
        follower.Update(true, -20);
        follower.Update(true, -5);    // back into twilight: stays dark
        follower.Update(true, -7);    // and out again: still no second change
        Assert.Equal(new[] { false }, store.Sets);

        follower.Update(true, 4);     // morning
        follower.Update(true, 20);
        Assert.Equal(new[] { false, true }, store.Sets);
    }

    [Fact]
    public void A_manual_switch_in_between_is_left_alone_until_the_next_change_of_phase()
    {
        var store = new FakeStore();
        var follower = new ScheduleThemeFollower(store);
        follower.Update(true, 30);
        follower.Update(true, -10);   // night: dark
        store.IsLight = true;         // the person switches back to light by hand

        follower.Update(true, -12);
        follower.Update(true, -15);

        Assert.Equal(new[] { false }, store.Sets);   // not forced back to dark every half minute
        Assert.True(store.IsLight);

        store.IsLight = false;        // switches to dark by hand during the next day
        follower.Update(true, 10);    // the morning arrives: that is a change of phase, so light is applied
        Assert.Equal(new[] { false, true }, store.Sets);
    }

    [Fact]
    public void Already_in_the_right_theme_nothing_is_written()
    {
        var store = new FakeStore { IsLight = false };
        var follower = new ScheduleThemeFollower(store);

        follower.Update(true, -20);

        Assert.Empty(store.Sets);
    }

    [Fact]
    public void Switched_off_nothing_changes_and_turning_it_on_applies_the_current_phase()
    {
        var store = new FakeStore();
        var follower = new ScheduleThemeFollower(store);

        follower.Update(false, -20);
        Assert.Empty(store.Sets);

        follower.Update(true, -20);
        Assert.Equal(new[] { false }, store.Sets);

        follower.Update(false, -20);
        store.IsLight = true;
        follower.Update(true, -20);                    // off, then on again at night: applied again
        Assert.Equal(new[] { false, false }, store.Sets);
    }

    [Fact]
    public void High_contrast_or_an_unreadable_theme_is_never_touched()
    {
        var store = new FakeStore { IsLight = null };
        var follower = new ScheduleThemeFollower(store);

        follower.Update(true, -20);
        follower.Update(true, 20);

        Assert.Empty(store.Sets);
    }

    [Fact]
    public void A_failed_change_is_contained_and_tried_again_at_the_next_update()
    {
        var store = new FakeStore { ThrowOnSet = new UnauthorizedAccessException("policy") };
        var follower = new ScheduleThemeFollower(store);

        Assert.Null(Record.Exception(() => follower.Update(true, -20)));
        Assert.Empty(store.Sets);

        store.ThrowOnSet = null;
        follower.Update(true, -20);

        Assert.Equal(new[] { false }, store.Sets);
    }

    [Fact]
    public void The_option_is_off_by_default_and_survives_clone_and_copy()
    {
        Assert.False(new AppSettings().FollowScheduleTheme);

        var original = new AppSettings { FollowScheduleTheme = true };
        Assert.True(original.Clone().FollowScheduleTheme);
        var target = new AppSettings();
        target.CopyFrom(original);
        Assert.True(target.FollowScheduleTheme);
    }
}
