#pragma warning disable CA1861 // small literal arrays in tests are clearer than shared static fields
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class StartupTimelineTests
{
    private sealed class FakeClock
    {
        public TimeSpan Now { get; set; }
    }

    [Fact]
    public void Each_step_gets_the_time_since_the_previous_mark()
    {
        var clock = new FakeClock();
        var timeline = new StartupTimeline(() => clock.Now);

        clock.Now = TimeSpan.FromMilliseconds(120);
        timeline.Mark("settings");
        clock.Now = TimeSpan.FromMilliseconds(150);
        timeline.Mark("display controllers");
        clock.Now = TimeSpan.FromMilliseconds(28_150);
        timeline.Mark("tray menu");

        Assert.Equal(new[] { 120.0, 30.0, 28_000.0 }, timeline.Steps.Select(step => step.Duration.TotalMilliseconds).ToArray());
        Assert.Equal(TimeSpan.FromMilliseconds(28_150), timeline.Total);
    }

    [Fact]
    public void Slow_steps_are_found_slowest_first()
    {
        var clock = new FakeClock();
        var timeline = new StartupTimeline(() => clock.Now);
        clock.Now = TimeSpan.FromMilliseconds(1_500); timeline.Mark("a");
        clock.Now += TimeSpan.FromMilliseconds(50); timeline.Mark("b");
        clock.Now += TimeSpan.FromMilliseconds(28_000); timeline.Mark("c");

        var slow = timeline.SlowSteps(TimeSpan.FromSeconds(1));

        Assert.Equal(new[] { "c", "a" }, slow.Select(step => step.Step).ToArray());
    }

    [Fact]
    public void The_summary_is_one_line_with_the_total_and_every_step()
    {
        var clock = new FakeClock();
        var timeline = new StartupTimeline(() => clock.Now);
        clock.Now = TimeSpan.FromMilliseconds(100); timeline.Mark("settings");
        clock.Now = TimeSpan.FromMilliseconds(2_400); timeline.Mark("tray menu");

        string summary = timeline.Summary();

        Assert.Equal("Startup took 2400 ms: settings 100 ms, tray menu 2300 ms", summary);
        Assert.DoesNotContain('\n', summary);
    }

    [Fact]
    public void The_real_clock_works()
    {
        var timeline = new StartupTimeline();
        timeline.Mark("first");

        Assert.Single(timeline.Steps);
        Assert.True(timeline.Total >= TimeSpan.Zero);
    }
}

public class BreakReminderStatusTextTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    private static string Describe(
        bool enabled = true, DateTime? snoozed = null, bool withinHours = true, string? from = "08:00",
        int accumulatedMinutes = 0, BreakReminderDecision? held = null)
        => BreakReminderStatusText.Describe(enabled, snoozed, withinHours, from, TimeSpan.FromMinutes(accumulatedMinutes), Interval, held);

    [Fact]
    public void Off_means_off_whatever_else_is_true()
        => Assert.Equal("Break reminders are off", Describe(enabled: false, snoozed: new DateTime(2026, 11, 11, 14, 0, 0), withinHours: false));

    [Fact]
    public void A_snooze_names_the_time_it_ends()
        => Assert.Equal("Break reminders snoozed until 14:05", Describe(snoozed: new DateTime(2026, 11, 11, 14, 5, 0)));

    [Fact]
    public void Outside_the_chosen_hours_says_when_they_begin()
    {
        Assert.Equal("Outside your reminder hours (back at 08:00)", Describe(withinHours: false));
        Assert.Equal("Outside your reminder hours", Describe(withinHours: false, from: null));
    }

    [Theory]
    [InlineData(BreakReminderDecision.SuppressedForCall, "waiting for your call to end")]
    [InlineData(BreakReminderDecision.SuppressedForFullscreen, "waiting for the full-screen app to close")]
    [InlineData(BreakReminderDecision.SuppressedForMigraine, "paused while Migraine Mode is on")]
    [InlineData(BreakReminderDecision.SuppressedForIdle, "waiting until you are back at the keyboard")]
    public void A_due_reminder_that_is_waiting_says_why(BreakReminderDecision reason, string expectedPart)
        => Assert.Contains(expectedPart, Describe(accumulatedMinutes: 30, held: reason));

    [Theory]
    [InlineData(0, "Next break in about 30 min")]
    [InlineData(18, "Next break in about 12 min")]
    [InlineData(29, "Next break in about 1 min")]
    public void Otherwise_it_counts_down_in_whole_minutes_never_showing_zero(int accumulated, string expected)
        => Assert.Equal(expected, Describe(accumulatedMinutes: accumulated));

    [Fact]
    public void Due_with_nothing_holding_it_back_says_so()
        => Assert.Equal("A break is due now", Describe(accumulatedMinutes: 30));
}

public class ReminderStatusServiceTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class Rig
    {
        public Clock Clock { get; } = new();
        public AppSettings Settings { get; } = new() { BreakReminderEnabled = true, BreakReminderIntervalMinutes = 10 };
        public bool InCall { get; set; }
        public int Prompts { get; private set; }
        public int Sounds { get; private set; }
        public BreakReminderService Service { get; }

        public Rig()
        {
            Service = new BreakReminderService(
                Settings, () => false, (_, _, _, _) => { },
                showPrompt: _ => { Prompts++; return true; },
                readIdle: () => TimeSpan.FromSeconds(1), isFullscreen: () => false, clock: Clock,
                isInCall: () => InCall, playSound: () => Sounds++);
        }

        public void StepMinutes(int minutes)
        {
            for (int i = 0; i < minutes * 4; i++)
            {
                Clock.Now += BreakReminderPolicy.SampleInterval;
                Service.SampleOnce();
            }
        }
    }

    [Fact]
    public void The_status_counts_down_then_explains_a_call_hold_then_clears_after_the_reminder()
    {
        var rig = new Rig { InCall = true };
        Assert.Equal("Next break in about 10 min", rig.Service.DescribeStatus());

        rig.StepMinutes(5);
        Assert.Equal("Next break in about 6 min", rig.Service.DescribeStatus()); // the first sample only anchors the clock, so 5 minutes counts as 4m45s

        rig.StepMinutes(6);
        Assert.Equal("A break is due — waiting for your call to end", rig.Service.DescribeStatus());

        rig.InCall = false;
        rig.StepMinutes(1);
        Assert.Equal(1, rig.Prompts);
        Assert.StartsWith("Next break in about", rig.Service.DescribeStatus());
    }

    [Fact]
    public void The_status_follows_the_settings()
    {
        var rig = new Rig();
        rig.Settings.BreakReminderEnabled = false;
        Assert.Equal("Break reminders are off", rig.Service.DescribeStatus());

        rig.Settings.BreakReminderEnabled = true;
        rig.Settings.BreakReminderLimitToHours = true;
        rig.Settings.BreakReminderFromLocal = "10:00";
        rig.Settings.BreakReminderToLocal = "18:00";
        Assert.Equal("Outside your reminder hours (back at 10:00)", rig.Service.DescribeStatus());
    }

    [Fact]
    public void A_test_reminder_arrives_like_a_real_one_without_touching_the_count()
    {
        var rig = new Rig();
        rig.Settings.BreakReminderSound = true;
        rig.StepMinutes(4);
        string before = rig.Service.DescribeStatus();

        rig.Service.ShowTestReminder();

        Assert.Equal(1, rig.Prompts);
        Assert.Equal(1, rig.Sounds);
        Assert.Equal(before, rig.Service.DescribeStatus());
    }

    [Fact]
    public void A_test_reminder_that_fails_does_not_throw()
    {
        var settings = new AppSettings { BreakReminderEnabled = true };
        var service = new BreakReminderService(settings, () => false, (_, _, _, _) => throw new InvalidOperationException("no tray"),
            showPrompt: _ => throw new InvalidOperationException("no window"), isInCall: () => false);

        Assert.Null(Record.Exception(service.ShowTestReminder));
    }
}

public class AmbientLightWarmUpTests
{
    [Fact]
    public void Asking_for_a_reading_before_the_sensor_is_open_returns_at_once_and_never_throws()
    {
        var timer = Stopwatch.StartNew();

        double? reading = AmbientLightSensor.TryGetCurrentLux(DateTime.UtcNow);

        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2));
        _ = reading; // null until the background open finishes; a real value afterwards on a machine with a sensor
        AmbientLightSensor.WarmUp();
        AmbientLightSensor.WarmUp(); // a second call is harmless
    }
}

public class DocumentationMatchesBehaviourTests
{
    private static string RepoFile(string name, [CallerFilePath] string testFile = "")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", "..", name)));

    [Fact]
    public void The_privacy_policy_says_what_call_awareness_reads_and_what_it_does_not()
    {
        string privacy = RepoFile("PRIVACY.md");

        Assert.Contains("microphone", privacy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("camera", privacy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never opens the microphone or camera", privacy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("keyboard and mouse", privacy, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_enterprise_guide_names_the_registry_keys_read_and_the_command_line_switches()
    {
        string guide = RepoFile("ENTERPRISE_DEPLOYMENT.md");

        Assert.Contains("CapabilityAccessManager", guide);
        Assert.Contains(StartupArguments.RestartWait, guide);
        Assert.Contains(StartupArguments.Restore, guide);
        Assert.Contains("BreakReminderHoldDuringCalls", guide);
    }

    [Fact]
    public void The_readme_describes_the_current_reminder_features()
    {
        string readme = RepoFile("README.md");

        Assert.Contains("call", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("test reminder", readme, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_documents_describe_the_history_and_theme_options_and_name_their_settings()
    {
        string privacy = RepoFile("PRIVACY.md");
        string enterprise = RepoFile("ENTERPRISE_DEPLOYMENT.md");
        string readme = RepoFile("README.md");

        Assert.Contains("time at the computer", privacy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("light/dark theme", privacy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AppsUseLightTheme", enterprise);
        Assert.Contains("FollowScheduleTheme", enterprise);
        Assert.Contains("BreakHistoryEnabled", enterprise);
        Assert.Contains("stand up", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("screen-time history", readme, StringComparison.OrdinalIgnoreCase);

        var properties = typeof(AppSettings).GetProperties().Select(property => property.Name).ToHashSet();
        Assert.Contains("FollowScheduleTheme", properties);
        Assert.Contains("BreakHistoryEnabled", properties);
    }

    [Fact]
    public void Every_setting_the_documentation_names_exists()
    {
        var properties = typeof(AppSettings).GetProperties().Select(property => property.Name).ToHashSet();

        Assert.Contains("BreakReminderHoldDuringCalls", properties);
    }
}
