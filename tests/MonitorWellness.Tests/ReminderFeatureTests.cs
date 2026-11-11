#pragma warning disable CA1861 // small literal arrays in tests are clearer than shared static fields
using System.Drawing;
using System.Text.Json;
using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class CallActivityDetectorTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
    private static long Ticks(DateTime utc) => utc.ToFileTimeUtc();

    [Fact]
    public void A_device_that_started_and_has_not_stopped_is_in_use()
    {
        Assert.True(CallActivityDetector.IsInUse(Ticks(Now.AddMinutes(-20)), 0, Now));
        Assert.True(CallActivityDetector.IsInUse(Ticks(Now.AddMinutes(-20)), Ticks(Now.AddMinutes(-60)), Now)); // stopped before it started again
    }

    [Fact]
    public void A_device_whose_last_use_has_ended_or_that_was_never_used_is_not_in_use()
    {
        Assert.False(CallActivityDetector.IsInUse(Ticks(Now.AddMinutes(-30)), Ticks(Now.AddMinutes(-10)), Now));
        Assert.False(CallActivityDetector.IsInUse(0, 0, Now));
        Assert.False(CallActivityDetector.IsInUse(-5, 0, Now));
    }

    [Fact]
    public void A_record_that_never_closed_is_stale_after_twelve_hours_and_future_or_invalid_times_are_ignored()
    {
        Assert.True(CallActivityDetector.IsInUse(Ticks(Now.AddHours(-11)), 0, Now));
        Assert.False(CallActivityDetector.IsInUse(Ticks(Now.AddHours(-13)), 0, Now));
        Assert.False(CallActivityDetector.IsInUse(Ticks(Now.AddHours(2)), 0, Now));
        Assert.False(CallActivityDetector.IsInUse(long.MaxValue, 0, Now));
    }

    [Theory]
    [InlineData("MSTeams_8wekyb3d8bbwe", true)]
    [InlineData("C:#Users#chris#AppData#Roaming#Zoom#bin#Zoom.exe", true)]
    [InlineData("C:#Program Files#Google#Chrome#Application#chrome.exe", true)]
    [InlineData("C:#Program Files#obs-studio#bin#64bit#obs64.exe", false)]
    [InlineData("C:#Python314#python.exe", false)]
    [InlineData("C:#Windows#System32#SoundRecorder.exe", false)]
    [InlineData("", false)]
    public void Only_calling_apps_count_for_the_microphone(string recordedName, bool expected)
        => Assert.Equal(expected, CallActivityDetector.IsCallApp(recordedName));
}

public class ActiveHoursTests
{
    private static DateTime At(int hour, int minute) => new(2026, 11, 11, hour, minute, 0);

    [Fact]
    public void Without_the_limit_every_time_is_allowed()
        => Assert.True(BreakReminderPolicy.IsWithinActiveHours(At(3, 0), false, "08:00", "18:00"));

    [Theory]
    [InlineData(8, 0, true)]
    [InlineData(12, 30, true)]
    [InlineData(17, 59, true)]
    [InlineData(18, 0, false)]
    [InlineData(7, 59, false)]
    [InlineData(23, 0, false)]
    public void Daytime_hours_include_the_start_and_exclude_the_end(int hour, int minute, bool expected)
        => Assert.Equal(expected, BreakReminderPolicy.IsWithinActiveHours(At(hour, minute), true, "08:00", "18:00"));

    [Theory]
    [InlineData(22, 0, true)]
    [InlineData(23, 59, true)]
    [InlineData(1, 30, true)]
    [InlineData(5, 59, true)]
    [InlineData(6, 0, false)]
    [InlineData(12, 0, false)]
    [InlineData(21, 59, false)]
    public void Hours_that_run_past_midnight_work(int hour, int minute, bool expected)
        => Assert.Equal(expected, BreakReminderPolicy.IsWithinActiveHours(At(hour, minute), true, "22:00", "06:00"));

    [Theory]
    [InlineData("08:00", "08:00")]
    [InlineData("nonsense", "18:00")]
    [InlineData("08:00", null)]
    public void Equal_or_unreadable_times_mean_no_limit_rather_than_never_reminding(string? from, string? to)
        => Assert.True(BreakReminderPolicy.IsWithinActiveHours(At(3, 0), true, from, to));

    [Fact]
    public void A_call_holds_the_reminder_after_migraine_and_fullscreen_but_before_idle()
    {
        Assert.Equal(BreakReminderDecision.SuppressedForCall, BreakReminderPolicy.Decide(false, false, TimeSpan.Zero, inCall: true));
        Assert.Equal(BreakReminderDecision.SuppressedForMigraine, BreakReminderPolicy.Decide(true, false, TimeSpan.Zero, inCall: true));
        Assert.Equal(BreakReminderDecision.SuppressedForFullscreen, BreakReminderPolicy.Decide(false, true, TimeSpan.Zero, inCall: true));
        Assert.Equal(BreakReminderDecision.Show, BreakReminderPolicy.Decide(false, false, TimeSpan.Zero));
    }
}

public class ReminderOptionsServiceTests
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
        public int Balloons { get; private set; }
        public int Sounds { get; private set; }
        public string? LastBalloon { get; private set; }
        public BreakReminderService Service { get; }

        public Rig()
        {
            Service = new BreakReminderService(
                Settings,
                () => false,
                (_, message, _, _) => { Balloons++; LastBalloon = message; },
                showPrompt: _ => { Prompts++; return true; },
                readIdle: () => TimeSpan.FromSeconds(1),
                isFullscreen: () => false,
                clock: Clock,
                isInCall: () => InCall,
                playSound: () => Sounds++);
        }

        public void Step(int samples)
        {
            for (int i = 0; i < samples; i++)
            {
                Clock.Now += BreakReminderPolicy.SampleInterval;
                Service.SampleOnce();
            }
        }

        public void StepMinutes(int minutes) => Step(minutes * 4);
    }

    [Fact]
    public void A_reminder_due_during_a_call_is_shown_when_the_call_ends()
    {
        var rig = new Rig { InCall = true };

        rig.StepMinutes(25);
        Assert.Equal(0, rig.Prompts);

        rig.InCall = false;
        rig.Step(1);

        Assert.Equal(1, rig.Prompts);
    }

    [Fact]
    public void A_call_that_lasts_longer_than_the_cap_does_not_hold_the_reminder_for_ever()
    {
        var rig = new Rig { InCall = true };

        rig.StepMinutes(10 + 89);
        Assert.Equal(0, rig.Prompts);

        rig.StepMinutes(3);
        Assert.Equal(1, rig.Prompts);
    }

    [Fact]
    public void With_the_option_off_a_call_does_not_hold_anything_and_the_detector_is_not_even_asked()
    {
        var rig = new Rig { InCall = true };
        rig.Settings.BreakReminderHoldDuringCalls = false;

        rig.StepMinutes(11);

        Assert.Equal(1, rig.Prompts);
    }

    [Fact]
    public void Nothing_counts_outside_the_chosen_hours_so_the_first_reminder_comes_a_full_interval_after_they_begin()
    {
        var rig = new Rig();
        rig.Settings.BreakReminderLimitToHours = true;
        rig.Settings.BreakReminderFromLocal = "10:00";
        rig.Settings.BreakReminderToLocal = "18:00";
        // The clock starts at 09:00: an hour before the hours begin.

        rig.StepMinutes(59);
        Assert.Equal(0, rig.Prompts);

        rig.StepMinutes(9);   // 09:59 + 9 minutes = 10:08, inside the hours for 8 minutes
        Assert.Equal(0, rig.Prompts);

        rig.StepMinutes(4);   // 10:12 — more than ten minutes inside the hours
        Assert.Equal(1, rig.Prompts);
    }

    [Fact]
    public void The_notification_style_never_opens_the_prompt()
    {
        var rig = new Rig();
        rig.Settings.BreakReminderStyle = BreakReminderStyles.Notification;

        rig.StepMinutes(11);

        Assert.Equal(0, rig.Prompts);
        Assert.Equal(1, rig.Balloons);
    }

    [Fact]
    public void The_sound_plays_only_when_asked_for()
    {
        var quiet = new Rig();
        quiet.StepMinutes(11);
        Assert.Equal(0, quiet.Sounds);

        var loud = new Rig();
        loud.Settings.BreakReminderSound = true;
        loud.StepMinutes(11);
        Assert.Equal(1, loud.Sounds);
        Assert.Equal(1, loud.Prompts);
    }

    [Fact]
    public void The_break_length_setting_is_used_in_the_notification_text_and_kept_in_bounds()
    {
        var rig = new Rig();
        rig.Settings.BreakReminderStyle = BreakReminderStyles.Notification;
        rig.Settings.BreakFocusSeconds = 45;
        rig.StepMinutes(11);
        Assert.Contains("45 seconds", rig.LastBalloon);

        var wild = new Rig();
        wild.Settings.BreakReminderStyle = BreakReminderStyles.Notification;
        wild.Settings.BreakFocusSeconds = 9999;
        wild.StepMinutes(11);
        Assert.Contains($"{BreakReminderStyles.MaximumFocusSeconds} seconds", wild.LastBalloon);
    }

    [Fact]
    public void A_sound_that_fails_does_not_stop_the_reminder()
    {
        var settings = new AppSettings { BreakReminderEnabled = true, BreakReminderIntervalMinutes = 10, BreakReminderSound = true };
        var clock = new Clock();
        int prompts = 0;
        var service = new BreakReminderService(settings, () => false, (_, _, _, _) => { },
            showPrompt: _ => { prompts++; return true; }, readIdle: () => TimeSpan.Zero, isFullscreen: () => false,
            clock: clock, isInCall: () => false, playSound: () => throw new InvalidOperationException("no audio device"));

        for (int i = 0; i < 45; i++)
        {
            clock.Now += BreakReminderPolicy.SampleInterval;
            service.SampleOnce();
        }

        Assert.Equal(1, prompts);
    }
}

public class SettingsFieldCoverageTests
{
    [Fact]
    public void Clone_and_CopyFrom_carry_every_setting_including_the_reminder_options()
    {
        var original = new AppSettings
        {
            BreakReminderEnabled = true,
            BreakReminderIntervalMinutes = 35,
            BreakReminderHoldDuringCalls = false,
            BreakReminderLimitToHours = true,
            BreakReminderFromLocal = "09:30",
            BreakReminderToLocal = "17:15",
            BreakReminderStyle = BreakReminderStyles.Notification,
            BreakReminderSound = true,
            BreakFocusSeconds = 45,
        };
        string expected = JsonSerializer.Serialize(original);

        Assert.Equal(expected, JsonSerializer.Serialize(original.Clone()));

        var target = new AppSettings();
        target.CopyFrom(original);
        Assert.Equal(expected, JsonSerializer.Serialize(target));
    }

    [Fact]
    public void An_old_settings_file_without_the_new_options_gets_sensible_defaults()
    {
        AppSettings loaded = JsonSerializer.Deserialize<AppSettings>("{ \"BreakReminderEnabled\": true, \"BreakReminderIntervalMinutes\": 25 }")!;

        Assert.True(loaded.BreakReminderHoldDuringCalls);
        Assert.False(loaded.BreakReminderLimitToHours);
        Assert.Equal("08:00", loaded.BreakReminderFromLocal);
        Assert.Equal(BreakReminderStyles.Window, loaded.BreakReminderStyle);
        Assert.False(loaded.BreakReminderSound);
        Assert.Equal(20, loaded.BreakFocusSeconds);
        Assert.True(AppSettingsValidator.TryValidate(loaded, out _));
    }

    [Theory]
    [InlineData("25:00", "18:00", "Window", 20)]
    [InlineData("08:00", "soon", "Window", 20)]
    [InlineData("08:00", "18:00", "Popup", 20)]
    [InlineData("08:00", "18:00", "Window", 5)]
    [InlineData("08:00", "18:00", "Window", 500)]
    public void Invalid_reminder_options_are_rejected_and_then_repaired_without_touching_other_settings(string from, string to, string style, int seconds)
    {
        var settings = new AppSettings
        {
            BreakReminderFromLocal = from, BreakReminderToLocal = to, BreakReminderStyle = style, BreakFocusSeconds = seconds,
            BreakReminderIntervalMinutes = 40, DayKelvin = 5900,
        };
        Assert.False(AppSettingsValidator.TryValidate(settings, out _));

        List<string> repaired = SettingsRepair.Repair(settings);

        Assert.NotEmpty(repaired);
        Assert.True(AppSettingsValidator.TryValidate(settings, out _));
        Assert.Equal(40, settings.BreakReminderIntervalMinutes);
        Assert.Equal(5900, settings.DayKelvin);
    }
}

public class RestartAndPlacementTests
{
    [Fact]
    public void The_restart_switch_carries_the_process_id_to_wait_for()
    {
        string arguments = StartupArguments.BuildRestartArguments(4321);
        string[] args = arguments.Split(' ');

        Assert.True(StartupArguments.TryGetRestartWaitProcessId(args, out int pid));
        Assert.Equal(4321, pid);
    }

    [Theory]
    [InlineData("--restart-wait")]
    [InlineData("--restart-wait", "abc")]
    [InlineData("--restart-wait", "-5")]
    [InlineData("--restart-wait", "0")]
    [InlineData("--other", "123")]
    public void A_missing_or_bad_process_id_is_not_a_restart(params string[] args)
        => Assert.False(StartupArguments.TryGetRestartWaitProcessId(args, out _));

    [Fact]
    public void Switches_are_found_whatever_their_case()
    {
        Assert.True(StartupArguments.HasSwitch(new[] { "--RESTORE" }, StartupArguments.Restore));
        Assert.False(StartupArguments.HasSwitch(new[] { "--restart-wait", "12" }, StartupArguments.Restore));
    }

    [Fact]
    public void The_prompt_sits_in_the_bottom_right_corner_of_the_work_area()
    {
        var workArea = new Rectangle(3440, 0, 1920, 1040);

        Point topLeft = PromptPlacement.BottomRight(workArea, new Size(400, 150), 16);

        Assert.Equal(new Point(3440 + 1920 - 400 - 16, 1040 - 150 - 16), topLeft);
    }

    [Fact]
    public void A_work_area_smaller_than_the_window_still_keeps_its_top_left_on_screen()
    {
        var workArea = new Rectangle(-1920, 100, 300, 100);

        Point topLeft = PromptPlacement.BottomRight(workArea, new Size(400, 150), 16);

        Assert.Equal(new Point(-1920, 100), topLeft);
    }
}
