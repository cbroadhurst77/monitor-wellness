using System.IO;
using System.Windows.Forms;
using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class ActiveTimeTrackerTests
{
    private static readonly DateTime T0 = new(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Period = BreakReminderPolicy.SampleInterval;
    private static readonly TimeSpan Away = BreakReminderPolicy.AwayThreshold;

    [Fact]
    public void Counts_time_while_the_person_is_active()
    {
        var tracker = new ActiveTimeTracker();
        for (int i = 0; i <= 4; i++)
            tracker.Sample(T0 + Period * i, TimeSpan.FromSeconds(3), Period, Away);

        Assert.Equal(Period * 4, tracker.Accumulated); // the first sample only anchors the clock
        Assert.True(tracker.IsDue(TimeSpan.FromMinutes(1)));
        Assert.False(tracker.IsDue(TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public void Being_away_restarts_the_count()
    {
        var tracker = new ActiveTimeTracker();
        tracker.Sample(T0, TimeSpan.Zero, Period, Away);
        tracker.Sample(T0 + Period, TimeSpan.Zero, Period, Away);
        tracker.Sample(T0 + Period * 2, TimeSpan.FromMinutes(5), Period, Away);

        Assert.Equal(TimeSpan.Zero, tracker.Accumulated);
    }

    [Fact]
    public void A_long_gap_between_samples_means_the_machine_slept_and_restarts_the_count()
    {
        var tracker = new ActiveTimeTracker();
        tracker.Sample(T0, TimeSpan.Zero, Period, Away);
        tracker.Sample(T0 + Period, TimeSpan.Zero, Period, Away);
        Assert.Equal(Period, tracker.Accumulated);

        tracker.Sample(T0 + TimeSpan.FromHours(8), TimeSpan.Zero, Period, Away);

        Assert.Equal(TimeSpan.Zero, tracker.Accumulated);
    }

    [Fact]
    public void Clock_moving_backwards_counts_nothing()
    {
        var tracker = new ActiveTimeTracker();
        tracker.Sample(T0, TimeSpan.Zero, Period, Away);
        tracker.Sample(T0 + Period, TimeSpan.Zero, Period, Away);

        tracker.Sample(T0, TimeSpan.Zero, Period, Away);

        Assert.Equal(Period, tracker.Accumulated);
    }

    [Fact]
    public void Reset_starts_again_from_zero()
    {
        var tracker = new ActiveTimeTracker();
        tracker.Sample(T0, TimeSpan.Zero, Period, Away);
        tracker.Sample(T0 + Period, TimeSpan.Zero, Period, Away);

        tracker.Reset();

        Assert.Equal(TimeSpan.Zero, tracker.Accumulated);
    }
}

public class BreakReminderPolicyTimeTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(20, 20)]
    [InlineData(100000, 480)]
    [InlineData(-5, 1)]
    public void Interval_is_bounded_in_one_place(int minutes, int expected)
        => Assert.Equal(expected, BreakReminderPolicy.ClampIntervalMinutes(minutes));

    [Fact]
    public void Until_morning_is_eight_today_when_it_is_earlier_than_that()
        => Assert.Equal(new DateTime(2026, 11, 10, 8, 0, 0), BreakReminderPolicy.NextMorningLocal(new DateTime(2026, 11, 10, 1, 30, 0)));

    [Fact]
    public void Until_morning_is_eight_tomorrow_otherwise()
    {
        Assert.Equal(new DateTime(2026, 11, 11, 8, 0, 0), BreakReminderPolicy.NextMorningLocal(new DateTime(2026, 11, 10, 8, 0, 0)));
        Assert.Equal(new DateTime(2026, 11, 11, 8, 0, 0), BreakReminderPolicy.NextMorningLocal(new DateTime(2026, 11, 10, 23, 59, 0)));
    }
}

public class BreakReminderServiceTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Rig
    {
        public Clock Clock { get; } = new();
        public AppSettings Settings { get; } = new() { BreakReminderEnabled = true, BreakReminderIntervalMinutes = 5 };
        public TimeSpan Idle { get; set; } = TimeSpan.FromSeconds(2);
        public bool Migraine { get; set; }
        public bool Fullscreen { get; set; }
        public bool ThrowOnIdle { get; set; }
        public int Shown { get; private set; }
        public BreakReminderService Service { get; }

        public Rig()
        {
            Service = new BreakReminderService(
                Settings,
                () => Migraine,
                (_, _, _, _) => Shown++,
                readIdle: () => ThrowOnIdle ? throw new InvalidOperationException("boom") : Idle,
                isFullscreen: () => Fullscreen,
                clock: Clock);
        }

        public void Step(int samples)
        {
            for (int i = 0; i < samples; i++)
            {
                Clock.Now += BreakReminderPolicy.SampleInterval;
                Service.SampleOnce();
            }
        }
    }

    [Fact]
    public void Shows_once_after_the_interval_of_active_time_and_not_again_until_another_interval()
    {
        var rig = new Rig();

        rig.Step(19);          // anchor + 18 periods = 4.5 minutes
        Assert.Equal(0, rig.Shown);
        rig.Step(2);
        Assert.Equal(1, rig.Shown);
        rig.Step(10);
        Assert.Equal(1, rig.Shown);
    }

    [Fact]
    public void A_reminder_held_back_by_a_fullscreen_app_is_shown_when_it_ends()
    {
        var rig = new Rig();
        rig.Fullscreen = true;

        rig.Step(60);          // 15 minutes of fullscreen use
        Assert.Equal(0, rig.Shown);

        rig.Fullscreen = false;
        rig.Step(1);

        Assert.Equal(1, rig.Shown);
    }

    [Fact]
    public void Stepping_away_restarts_the_count_so_the_next_reminder_is_a_full_interval_later()
    {
        var rig = new Rig();
        rig.Step(15);                       // almost due
        rig.Idle = TimeSpan.FromMinutes(10);
        rig.Step(1);                        // away: count restarts
        rig.Idle = TimeSpan.FromSeconds(1);

        rig.Step(10);                       // 2.5 minutes back at the desk
        Assert.Equal(0, rig.Shown);
    }

    [Fact]
    public void A_break_taken_restarts_the_count()
    {
        var rig = new Rig();
        rig.Step(15);

        rig.Service.NotifyBreakTaken();
        rig.Step(15);

        Assert.Equal(0, rig.Shown);
    }

    [Fact]
    public void A_failing_sample_is_contained()
    {
        var rig = new Rig { ThrowOnIdle = true };

        var exception = Record.Exception(() => rig.Step(3));

        Assert.Null(exception);
        Assert.Equal(0, rig.Shown);
    }

    [Fact]
    public void Nothing_happens_when_reminders_are_off()
    {
        var rig = new Rig();
        rig.Settings.BreakReminderEnabled = false;

        rig.Step(100);

        Assert.Equal(0, rig.Shown);
    }
}

public class AppSettingsAdoptAppOwnedStateTests
{
    [Fact]
    public void Saving_a_stale_draft_does_not_revert_what_the_app_recorded_meanwhile()
    {
        var live = new AppSettings();
        AppSettings draft = live.Clone();

        live.LastUpdateCheckUtc = new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);
        live.LocationReminderShown = true;
        live.HasCompletedOnboarding = true;

        draft.AdoptAppOwnedState(live);

        Assert.Equal(live.LastUpdateCheckUtc, draft.LastUpdateCheckUtc);
        Assert.True(draft.LocationReminderShown);
        Assert.True(draft.HasCompletedOnboarding);
    }

    [Fact]
    public void A_monitor_quarantined_while_the_window_was_open_stays_quarantined()
    {
        var live = new AppSettings();
        live.HardwareBrightnessSafetyByMonitor["MON1"] = new HardwareBrightnessSafetyState { IsApproved = true };
        AppSettings draft = live.Clone();

        live.HardwareBrightnessSafetyByMonitor["MON1"] = new HardwareBrightnessSafetyState
        {
            IsQuarantined = true,
            ApprovedBeforeQuarantine = true,
            QuarantineReason = "failed",
            FailureCount = 1,
        };
        draft.HardwareBrightnessSafetyByMonitor["MON1"].IsApproved = true; // the stale draft still says approved

        draft.AdoptAppOwnedState(live);

        HardwareBrightnessSafetyState state = draft.HardwareBrightnessSafetyByMonitor["MON1"];
        Assert.True(state.IsQuarantined);
        Assert.False(state.IsApproved);
        Assert.NotSame(live.HardwareBrightnessSafetyByMonitor["MON1"], state);
    }

    [Fact]
    public void The_draft_keeps_its_own_unquarantined_edits()
    {
        var live = new AppSettings();
        live.HardwareBrightnessSafetyByMonitor["MON1"] = new HardwareBrightnessSafetyState();
        AppSettings draft = live.Clone();
        draft.HardwareBrightnessSafetyByMonitor["MON1"].IsApproved = true;

        draft.AdoptAppOwnedState(live);

        Assert.True(draft.HardwareBrightnessSafetyByMonitor["MON1"].IsApproved);
    }
}

public class AmbientLightSmootherTimeTests
{
    private static readonly DateTime T0 = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Adjustment_speed_does_not_depend_on_how_often_it_is_called()
    {
        var rare = new AmbientLightSmoother();
        var frequent = new AmbientLightSmoother();
        rare.Update(-AmbientLightAdapter.MaxAdjustment, T0);
        frequent.Update(-AmbientLightAdapter.MaxAdjustment, T0);

        double rareResult = rare.Update(AmbientLightAdapter.MaxAdjustment, T0 + TimeSpan.FromSeconds(30));
        double frequentResult = 0;
        for (int i = 1; i <= 150; i++)
            frequentResult = frequent.Update(AmbientLightAdapter.MaxAdjustment, T0 + TimeSpan.FromMilliseconds(200 * i));

        Assert.Equal(rareResult, frequentResult, precision: 6);
        Assert.Equal(-AmbientLightAdapter.MaxAdjustment + AmbientLightSmoother.MaximumStep, rareResult, precision: 6);
    }
}

public class DurableFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "mw-durable-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Writes_without_leaving_a_temporary_file_and_without_a_byte_order_mark()
    {
        string path = Path.Combine(_folder, "a.json");

        DurableFile.WriteAllText(path, "{}");

        Assert.Equal("{}", File.ReadAllText(path));
        Assert.Equal((byte)'{', File.ReadAllBytes(path)[0]);
        Assert.Single(Directory.GetFileSystemEntries(_folder));
    }

    [Fact]
    public void Keeps_the_previous_contents_as_a_backup_when_asked()
    {
        string path = Path.Combine(_folder, "a.json");
        DurableFile.WriteAllText(path, "old");

        DurableFile.WriteAllText(path, "new", keepBackup: true);

        Assert.Equal("new", File.ReadAllText(path));
        Assert.Equal("old", File.ReadAllText(DurableFile.BackupPath(path)));
    }
}
