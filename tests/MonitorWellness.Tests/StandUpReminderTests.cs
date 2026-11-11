#pragma warning disable CA1861 // small literal arrays in tests are clearer than shared static fields
using System.Windows;
using System.Windows.Controls;
using MonitorWellness.Core;
using Button = System.Windows.Controls.Button;

namespace MonitorWellness.Tests;

public class StandUpReminderTests
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
        public AppSettings Settings { get; } = new()
        {
            BreakReminderEnabled = true,
            BreakReminderIntervalMinutes = 120,   // far away, so only the stand-up reminder shows unless a test says otherwise
            StandUpReminderEnabled = true,
            StandUpIntervalMinutes = 30,
        };
        public TimeSpan Idle { get; set; } = TimeSpan.FromSeconds(1);
        public bool InCall { get; set; }
        public int EyePrompts { get; private set; }
        public int StandUpPrompts { get; private set; }
        public List<string> Balloons { get; } = new();
        public Action<BreakPromptChoice>? StandUpAnswer { get; private set; }
        public BreakReminderService Service { get; }

        public Rig()
        {
            Service = new BreakReminderService(
                Settings, () => false, (_, message, _, _) => Balloons.Add(message),
                showPrompt: _ => { EyePrompts++; return true; },
                readIdle: () => Idle, isFullscreen: () => false, clock: Clock, isInCall: () => InCall,
                showStandUpPrompt: onChoice => { StandUpPrompts++; StandUpAnswer = onChoice; return true; });
        }

        public void StepMinutes(double minutes)
        {
            int samples = (int)Math.Round(minutes * 4);
            for (int i = 0; i < samples; i++)
            {
                Clock.Now += BreakReminderPolicy.SampleInterval;
                Service.SampleOnce();
            }
        }
    }

    [Fact]
    public void After_the_interval_of_screen_time_a_stand_up_reminder_is_shown_once()
    {
        var rig = new Rig();

        rig.StepMinutes(29);
        Assert.Equal(0, rig.StandUpPrompts);

        rig.StepMinutes(3);
        Assert.Equal(1, rig.StandUpPrompts);
        Assert.Equal(0, rig.EyePrompts);

        rig.StepMinutes(10);
        Assert.Equal(1, rig.StandUpPrompts);
    }

    [Fact]
    public void Switched_off_there_is_no_stand_up_reminder_and_no_status_text_for_it()
    {
        var rig = new Rig();
        rig.Settings.StandUpReminderEnabled = false;

        rig.StepMinutes(70);

        Assert.Equal(0, rig.StandUpPrompts);
        Assert.DoesNotContain("Stand-up", rig.Service.DescribeStatus());
    }

    [Fact]
    public void When_both_come_due_together_only_the_stand_up_is_shown_and_it_restarts_the_eye_count_too()
    {
        var rig = new Rig();
        rig.Settings.BreakReminderIntervalMinutes = 30;

        rig.StepMinutes(32);

        Assert.Equal(1, rig.StandUpPrompts);
        Assert.Equal(0, rig.EyePrompts);
        Assert.Contains("Next break in about", rig.Service.DescribeStatus()); // the eye count started again
    }

    [Fact]
    public void Three_minutes_away_counts_as_a_movement_break_even_though_it_is_too_short_for_the_eye_count()
    {
        var rig = new Rig();
        rig.StepMinutes(20);
        rig.Idle = TimeSpan.FromMinutes(4);
        rig.StepMinutes(1);          // seen away: the stand-up count restarts
        rig.Idle = TimeSpan.FromSeconds(1);

        rig.StepMinutes(20);         // never 30 minutes in a row

        Assert.Equal(0, rig.StandUpPrompts);
    }

    [Fact]
    public void A_pause_shorter_than_three_minutes_does_not_restart_the_stand_up_count()
    {
        var rig = new Rig();
        rig.StepMinutes(20);
        rig.Idle = TimeSpan.FromMinutes(2);   // reading
        rig.StepMinutes(2);
        rig.Idle = TimeSpan.FromSeconds(1);

        rig.StepMinutes(9);

        Assert.Equal(1, rig.StandUpPrompts);
    }

    [Fact]
    public void Taking_the_movement_break_restarts_both_counts_and_the_short_eye_break_does_not_restart_the_stand_up()
    {
        var rig = new Rig();
        rig.StepMinutes(31);
        Assert.Equal(1, rig.StandUpPrompts);
        rig.StandUpAnswer!(BreakPromptChoice.StartBreak);
        rig.StepMinutes(20);
        Assert.Equal(1, rig.StandUpPrompts);

        rig.Service.NotifyBreakTaken();              // a 20-second eye break
        rig.StepMinutes(11);
        Assert.Equal(2, rig.StandUpPrompts);         // still came on schedule, 31 minutes after the last one
    }

    [Fact]
    public void In_five_minutes_asks_again_after_five_minutes()
    {
        var rig = new Rig();
        rig.StepMinutes(31);
        rig.StandUpAnswer!(BreakPromptChoice.Snooze);

        rig.StepMinutes(3);
        Assert.Equal(1, rig.StandUpPrompts);
        rig.StepMinutes(3);
        Assert.Equal(2, rig.StandUpPrompts);
    }

    [Fact]
    public void A_call_holds_the_stand_up_reminder_too_and_the_status_says_so()
    {
        var rig = new Rig { InCall = true };

        rig.StepMinutes(40);
        Assert.Equal(0, rig.StandUpPrompts);
        Assert.Contains("waiting for your call to end", rig.Service.DescribeStatus());

        rig.InCall = false;
        rig.StepMinutes(1);
        Assert.Equal(1, rig.StandUpPrompts);
    }

    [Fact]
    public void The_notification_style_uses_a_balloon_with_its_own_wording()
    {
        var rig = new Rig();
        rig.Settings.BreakReminderStyle = BreakReminderStyles.Notification;

        rig.StepMinutes(31);

        Assert.Equal(0, rig.StandUpPrompts);
        Assert.Single(rig.Balloons);
        Assert.Contains("stand up", rig.Balloons[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_status_line_adds_the_time_to_the_next_stand_up_break()
    {
        var rig = new Rig();

        rig.StepMinutes(10);

        string status = rig.Service.DescribeStatus();
        Assert.Contains("Next break in about", status);
        Assert.Contains("Stand-up break in about 2", status); // 30 minutes less the 9m45s counted so far
    }

    [Theory]
    [InlineData(false, 0, 60, "")]
    [InlineData(true, 0, 60, " · Stand-up break in about 1 h 0 min")]
    [InlineData(true, 50, 60, " · Stand-up break in about 10 min")]
    [InlineData(true, 60, 60, " · Stand-up break due")]
    [InlineData(true, 5, 90, " · Stand-up break in about 1 h 25 min")]
    public void The_stand_up_suffix_reads_naturally(bool enabled, int accumulatedMinutes, int intervalMinutes, string expected)
        => Assert.Equal(expected, BreakReminderStatusText.StandUpSuffix(enabled, TimeSpan.FromMinutes(accumulatedMinutes), TimeSpan.FromMinutes(intervalMinutes)));
}

public class StandUpPromptWindowTests
{
    [Fact]
    public void The_stand_up_prompt_has_its_own_wording_and_its_button_reports_a_movement_break()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var choices = new List<BreakPromptChoice>();
                var window = new BreakPromptWindow(choices.Add, kind: BreakPromptKind.StandUp);

                Assert.Equal("Time to stand up", window.Title);
                var button = (Button)window.FindName("StartButton");
                Assert.Equal("I'll take a break", button.Content);
                Assert.Equal("I am taking a movement break", System.Windows.Automation.AutomationProperties.GetName(button));
                button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

                Assert.Equal(new[] { BreakPromptChoice.StartBreak }, choices);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw new InvalidOperationException("The window test failed on its UI thread.", failure);
    }
}

public class StandUpSettingsTests
{
    [Fact]
    public void The_stand_up_options_survive_clone_and_copy_and_have_safe_defaults()
    {
        var defaults = new AppSettings();
        Assert.False(defaults.StandUpReminderEnabled);
        Assert.Equal(60, defaults.StandUpIntervalMinutes);

        var original = new AppSettings { StandUpReminderEnabled = true, StandUpIntervalMinutes = 90 };
        Assert.Equal(90, original.Clone().StandUpIntervalMinutes);
        var target = new AppSettings();
        target.CopyFrom(original);
        Assert.True(target.StandUpReminderEnabled);
        Assert.Equal(90, target.StandUpIntervalMinutes);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(29)]
    [InlineData(241)]
    [InlineData(99_999)]
    public void An_out_of_range_interval_is_rejected_and_repaired_without_touching_other_settings(int minutes)
    {
        var settings = new AppSettings { StandUpIntervalMinutes = minutes, StandUpReminderEnabled = true, DayKelvin = 5900 };
        Assert.False(AppSettingsValidator.TryValidate(settings, out _));

        List<string> repaired = SettingsRepair.Repair(settings);

        Assert.Contains("stand-up reminder", repaired);
        Assert.True(AppSettingsValidator.TryValidate(settings, out _));
        Assert.Equal(5900, settings.DayKelvin);
    }
}
