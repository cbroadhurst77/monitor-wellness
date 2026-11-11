#pragma warning disable CA1861 // small literal arrays in tests are clearer than shared static fields
using System.Windows.Forms;
using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class BalloonClickStateTests
{
    [Fact]
    public void A_click_runs_the_action_of_the_balloon_on_screen_once()
    {
        var state = new BalloonClickState();
        Action action = () => { };
        state.Set(null, action);

        var first = state.TakeClick();
        var second = state.TakeClick();

        Assert.Same(action, first.Action);
        Assert.Null(first.Url);
        Assert.Null(second.Action);
        Assert.Null(second.Url);
    }

    [Fact]
    public void A_link_is_given_when_there_is_no_action()
    {
        var state = new BalloonClickState();
        state.Set("https://example.test/release", null);

        Assert.Equal("https://example.test/release", state.TakeClick().Url);
    }

    [Fact]
    public void An_action_wins_over_a_link()
    {
        var state = new BalloonClickState();
        state.Set("https://example.test", () => { });

        var click = state.TakeClick();

        Assert.NotNull(click.Action);
        Assert.Null(click.Url);
    }

    [Fact]
    public void A_later_balloon_that_wants_nothing_wipes_what_an_earlier_one_wanted()
    {
        // The release-page balloon, then an unrelated message: clicking the second must not open the first's page.
        var state = new BalloonClickState();
        state.Set("https://example.test/release", null);
        state.Set(null, null);

        var click = state.TakeClick();

        Assert.Null(click.Url);
        Assert.Null(click.Action);
    }

    [Fact]
    public void Closing_the_balloon_forgets_what_it_wanted()
    {
        var state = new BalloonClickState();
        state.Set("https://example.test", () => { });

        state.Clear();

        var click = state.TakeClick();
        Assert.Null(click.Url);
        Assert.Null(click.Action);
    }
}

public class StartupBalloonsTests
{
    private static string[] Messages(params StartupBalloon?[] balloons) => balloons.Select(b => b?.Message ?? "(none)").ToArray();

    [Fact]
    public void Nothing_shows_until_the_queue_is_released_then_one_at_a_time_in_order()
    {
        var queue = new StartupBalloons();
        queue.Enqueue("t", "first", ToolTipIcon.Info);
        queue.Enqueue("t", "second", ToolTipIcon.Warning);
        queue.Enqueue("t", "third", ToolTipIcon.Info);
        Assert.False(queue.Released);
        Assert.False(queue.Waiting);

        StartupBalloon? one = queue.Release();
        Assert.True(queue.Waiting);                       // more to come: the gap timer is running
        StartupBalloon? two = queue.OnSpacingElapsed();
        Assert.True(queue.Waiting);
        StartupBalloon? three = queue.OnSpacingElapsed();
        Assert.False(queue.Waiting);                      // the last one: no further gap
        StartupBalloon? none = queue.OnSpacingElapsed();

        Assert.Equal(new[] { "first", "second", "third", "(none)" }, Messages(one, two, three, none));
        Assert.Equal(ToolTipIcon.Warning, two!.Icon);
    }

    [Fact]
    public void A_single_notice_is_shown_without_a_gap_and_an_empty_queue_shows_nothing()
    {
        var single = new StartupBalloons();
        single.Enqueue("t", "only", ToolTipIcon.Info);
        Assert.Equal("only", single.Release()!.Message);
        Assert.False(single.Waiting);

        var empty = new StartupBalloons();
        Assert.Null(empty.Release());
        Assert.True(empty.Released);
        Assert.False(empty.Waiting);
    }

    [Fact]
    public void A_late_notice_waits_if_the_queue_is_not_released_yet_for_instance_during_the_welcome()
    {
        var queue = new StartupBalloons();

        Assert.Null(queue.EnqueueLate("t", "late", ToolTipIcon.Warning));
        Assert.Equal(1, queue.Count);
        Assert.Equal("late", queue.Release()!.Message);   // shown when the welcome is out of the way
    }

    [Fact]
    public void A_late_notice_after_release_is_shown_at_once_when_idle_but_queued_behind_a_running_gap()
    {
        var queue = new StartupBalloons();
        queue.Release();

        Assert.Equal("now", queue.EnqueueLate("t", "now", ToolTipIcon.Info)!.Message);
        Assert.False(queue.Waiting);

        queue.Enqueue("t", "a", ToolTipIcon.Info);
        queue.Enqueue("t", "b", ToolTipIcon.Info);
        Assert.Equal("a", queue.Release()!.Message);       // the gap is now running
        Assert.True(queue.Waiting);

        Assert.Null(queue.EnqueueLate("t", "behind", ToolTipIcon.Info));
        Assert.Equal(new[] { "b", "behind", "(none)" },
            Messages(queue.OnSpacingElapsed(), queue.OnSpacingElapsed(), queue.OnSpacingElapsed()));
    }
}

public class ComfortPlanCoordinatorTests
{
    private sealed class FakeStore : IComfortPlanStore
    {
        public ComfortPlanState? State { get; set; }
        public int Saves { get; private set; }
        public ComfortPlanState? Load() => State;
        public void Save(ComfortPlanState state) { State = state; Saves++; }
    }

    private sealed class Rig
    {
        public FakeStore Store { get; } = new();
        public ScheduleOverrideState Overrides { get; private set; } = new();
        public string DefaultPlan { get; set; } = "";
        public DateOnly Today { get; set; } = new(2026, 11, 11);
        public List<string> Log { get; } = new();
        public ComfortPlanCoordinator Coordinator { get; private set; }

        public Rig() => Coordinator = Create();

        private ComfortPlanCoordinator Create()
            => new(Overrides, Store, () => DefaultPlan, () => Today, Log.Add);

        /// <summary>The app is closed and started again: new in-memory state, same file, same day (unless Today was moved).</summary>
        public void Restart()
        {
            Overrides = new ScheduleOverrideState();
            Coordinator = Create();
            Coordinator.RestoreAtStart();
        }
    }

    [Fact]
    public void With_nothing_remembered_and_no_default_the_normal_schedule_runs()
    {
        var rig = new Rig();

        rig.Coordinator.RestoreAtStart();

        Assert.Null(rig.Coordinator.Current);
    }

    [Fact]
    public void The_daily_default_applies_at_start_when_nothing_was_chosen_today()
    {
        var rig = new Rig { DefaultPlan = "Reading" };

        rig.Coordinator.RestoreAtStart();

        Assert.Equal("Reading", rig.Coordinator.Current);
        Assert.Contains("Comfort plan restored at start: Reading.", rig.Log);
        Assert.Equal(0, rig.Store.Saves);   // applying the default is not the person's choice and is not remembered as one
    }

    [Fact]
    public void A_plan_picked_from_the_tray_is_still_there_after_a_restart()
    {
        var rig = new Rig();

        Assert.True(rig.Coordinator.Select("Recovery"));
        rig.Restart();

        Assert.Equal("Recovery", rig.Coordinator.Current);
    }

    [Fact]
    public void An_unknown_plan_is_refused_and_nothing_is_saved()
    {
        var rig = new Rig();

        Assert.False(rig.Coordinator.Select("Turbo"));

        Assert.Null(rig.Coordinator.Current);
        Assert.Equal(0, rig.Store.Saves);
    }

    [Fact]
    public void Returning_to_the_saved_schedule_stays_in_force_across_a_restart_even_with_a_daily_default()
    {
        var rig = new Rig { DefaultPlan = "Reading" };
        rig.Coordinator.RestoreAtStart();

        Assert.True(rig.Coordinator.ReturnToSavedSchedule());
        rig.Restart();

        Assert.Null(rig.Coordinator.Current);
    }

    [Fact]
    public void Returning_to_the_saved_schedule_when_no_plan_is_active_does_nothing()
    {
        var rig = new Rig();

        Assert.False(rig.Coordinator.ReturnToSavedSchedule());
        Assert.Equal(0, rig.Store.Saves);
    }

    [Fact]
    public void Emergency_restore_ends_the_plan_for_the_day_and_is_remembered()
    {
        var rig = new Rig { DefaultPlan = "Reading" };
        rig.Coordinator.RestoreAtStart();

        Assert.True(rig.Coordinator.EmergencyRestore());
        Assert.Null(rig.Coordinator.Current);
        rig.Restart();

        Assert.Null(rig.Coordinator.Current);
        Assert.False(new Rig().Coordinator.EmergencyRestore()); // nothing to end when no plan is active
    }

    [Fact]
    public void At_midnight_yesterdays_choice_ends_and_the_default_applies_again()
    {
        var rig = new Rig { DefaultPlan = "Reading" };
        rig.Coordinator.RestoreAtStart();
        rig.Coordinator.Select("Recovery");

        Assert.False(rig.Coordinator.OnTick());           // still the same day
        rig.Today = rig.Today.AddDays(1);
        Assert.True(rig.Coordinator.OnTick());

        Assert.Equal("Reading", rig.Coordinator.Current);
        Assert.False(rig.Coordinator.OnTick());           // already handled
    }

    [Fact]
    public void At_midnight_with_no_default_the_plan_simply_ends_and_with_no_change_nothing_is_reported()
    {
        var withNoDefault = new Rig();
        withNoDefault.Coordinator.Select("Reading");
        withNoDefault.Today = withNoDefault.Today.AddDays(1);
        Assert.True(withNoDefault.Coordinator.OnTick());
        Assert.Null(withNoDefault.Coordinator.Current);

        var same = new Rig { DefaultPlan = "Reading" };
        same.Coordinator.RestoreAtStart();
        same.Today = same.Today.AddDays(1);
        Assert.False(same.Coordinator.OnTick());          // the default was already in force
    }

    [Fact]
    public void Yesterdays_remembered_choice_does_not_survive_into_a_new_day_after_a_restart()
    {
        var rig = new Rig { DefaultPlan = "Reading" };
        rig.Coordinator.Select("Recovery");

        rig.Today = rig.Today.AddDays(1);
        rig.Restart();

        Assert.Equal("Reading", rig.Coordinator.Current);
    }

    [Fact]
    public void Changing_the_default_in_settings_applies_at_once_unless_something_was_chosen_today()
    {
        var rig = new Rig();
        rig.Coordinator.RestoreAtStart();

        rig.DefaultPlan = "Balanced";
        Assert.True(rig.Coordinator.ApplyDefaultIfNoChoiceToday());
        Assert.Equal("Balanced", rig.Coordinator.Current);
        Assert.False(rig.Coordinator.ApplyDefaultIfNoChoiceToday());   // no change the second time

        rig.Coordinator.Select("Recovery");
        rig.DefaultPlan = "Reading";
        Assert.False(rig.Coordinator.ApplyDefaultIfNoChoiceToday());   // the person chose today; leave it
        Assert.Equal("Recovery", rig.Coordinator.Current);
    }

    [Fact]
    public void Clearing_the_default_in_settings_ends_a_default_plan_that_is_in_force()
    {
        var rig = new Rig { DefaultPlan = "Reading" };
        rig.Coordinator.RestoreAtStart();

        rig.DefaultPlan = "";

        Assert.True(rig.Coordinator.ApplyDefaultIfNoChoiceToday());
        Assert.Null(rig.Coordinator.Current);
        Assert.Contains("Default comfort plan cleared.", rig.Log);
    }

    [Fact]
    public void The_status_line_shows_the_plan_the_coordinator_set()
    {
        var rig = new Rig();

        rig.Coordinator.Select("Reading");

        Assert.Contains("Reading comfort plan selected temporarily", rig.Overrides.DescribeStatus());
    }
}
