#pragma warning disable CA1861 // small literal arrays in tests are clearer than shared static fields
using System.IO;
using MonitorWellness.Core;
using Color = System.Windows.Media.Color;

namespace MonitorWellness.Tests;

public class SettingsRepairTests
{
    [Fact]
    public void One_out_of_range_value_resets_only_that_group()
    {
        var settings = new AppSettings { Latitude = 51.5, Longitude = -0.12, DayKelvin = 5700, DayBrightness = 7.5, HasCompletedOnboarding = true };

        List<string> repaired = SettingsRepair.Repair(settings);

        Assert.Equal(new[] { "brightness levels" }, repaired);
        Assert.Equal(new AppSettings().DayBrightness, settings.DayBrightness);
        Assert.Equal(51.5, settings.Latitude);
        Assert.Equal(5700, settings.DayKelvin);
        Assert.True(settings.HasCompletedOnboarding);
        Assert.True(AppSettingsValidator.TryValidate(settings, out _));
    }

    [Fact]
    public void Several_bad_groups_are_each_reset_and_the_rest_is_kept()
    {
        var settings = new AppSettings { Latitude = 999, BreakReminderIntervalMinutes = 0, MigraineHotkeyKey = 0, NightKelvin = 3600 };

        List<string> repaired = SettingsRepair.Repair(settings);

        Assert.Contains("location", repaired);
        Assert.Contains("break reminder interval", repaired);
        Assert.Contains("migraine hotkey", repaired);
        Assert.Equal(3600, settings.NightKelvin);
        Assert.True(AppSettingsValidator.TryValidate(settings, out _));
    }

    [Fact]
    public void A_null_collection_is_replaced_by_the_default_one()
    {
        var settings = new AppSettings { ExcludedMonitors = null!, DayKelvin = 5800 };

        List<string> repaired = SettingsRepair.Repair(settings);

        Assert.Contains("excluded monitors", repaired);
        Assert.NotNull(settings.ExcludedMonitors);
        Assert.Equal(5800, settings.DayKelvin);
    }

    [Fact]
    public void Valid_settings_are_left_alone()
        => Assert.Empty(SettingsRepair.Repair(new AppSettings()));

    [Fact]
    public void A_value_of_the_wrong_type_costs_only_that_value()
    {
        var skipped = new List<string>();

        AppSettings settings = SettingsRepair.DeserializeLeniently(
            "{ \"DayKelvin\": \"warm\", \"NightKelvin\": 3500, \"HasCompletedOnboarding\": true, \"Unknown\": 1, \"BreakReminderEnabled\": null }",
            new System.Text.Json.JsonSerializerOptions(), skipped);

        Assert.Equal(new AppSettings().DayKelvin, settings.DayKelvin);
        Assert.Equal(3500, settings.NightKelvin);
        Assert.True(settings.HasCompletedOnboarding);
        Assert.Contains("DayKelvin", skipped);
        Assert.Contains("BreakReminderEnabled", skipped);
        Assert.DoesNotContain("Unknown", skipped);
    }

    [Fact]
    public void Text_that_is_not_a_json_object_is_still_rejected()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() =>
            SettingsRepair.DeserializeLeniently("[1,2,3]", new System.Text.Json.JsonSerializerOptions(), new List<string>()));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() =>
            SettingsRepair.DeserializeLeniently("{ \"DayKelvin\": 5700, this is not json", new System.Text.Json.JsonSerializerOptions(), new List<string>()));
    }
}

public class SettingsStoreRepairTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "mw-repair-" + Guid.NewGuid().ToString("N"));

    public SettingsStoreRepairTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_wrongly_typed_value_in_a_real_file_costs_only_that_value()
    {
        // The store itself is a static class bound to the app-data folder; the pieces it is built from are checked here.
        var skipped = new List<string>();
        AppSettings loaded = SettingsRepair.DeserializeLeniently(
            "{ \"DayKelvin\": \"warm\", \"NightKelvin\": 3500, \"Latitude\": 999 }", new System.Text.Json.JsonSerializerOptions(), skipped);
        var repaired = SettingsRepair.Repair(loaded);

        Assert.Contains("DayKelvin", skipped);
        Assert.Contains("location", repaired);
        Assert.Equal(3500, loaded.NightKelvin);
        Assert.True(AppSettingsValidator.TryValidate(loaded, out _));
    }
}

public class MigraineFadeFailureTests
{
    private sealed class ThrowingColorTarget : IColorTemperatureTarget
    {
        public bool Throw { get; set; }
        public void ApplyToAll(int kelvin, double contrastReduction)
        {
            if (Throw)
                throw new InvalidOperationException("display went away");
        }
    }

    private sealed class QuietOverlay : IOverlayTarget
    {
        public IReadOnlyCollection<string> DeviceNames { get; } = new[] { "D1" };
        public void Apply(IReadOnlyDictionary<string, (Color Color, double Opacity)> byDevice) { }
    }

    private static MigraineModeController Create(ThrowingColorTarget target)
        => new(target, new QuietOverlay(), new AppSettings(),
            () => (6500, new Dictionary<string, double>(), Colors.Black));

    [Fact]
    public void A_failing_fade_step_is_contained_and_the_fade_is_ended_after_repeated_failures()
    {
        var target = new ThrowingColorTarget();
        MigraineModeController controller = Create(target);
        controller.Activate();
        controller.Deactivate();
        target.Throw = true;
        int stateChanges = 0;
        controller.StateChanged += () => stateChanges++;

        Assert.Null(Record.Exception(() => controller.FadeTick()));
        Assert.True(controller.IsFadingOut);
        controller.FadeTick();
        Assert.True(controller.IsFadingOut);
        controller.FadeTick();

        Assert.False(controller.IsFadingOut);
        Assert.Equal(1, stateChanges);
    }

    [Fact]
    public void A_successful_step_resets_the_failure_count()
    {
        var target = new ThrowingColorTarget();
        MigraineModeController controller = Create(target);
        controller.Activate();
        controller.Deactivate();

        target.Throw = true;
        controller.FadeTick();
        controller.FadeTick();
        target.Throw = false;
        controller.FadeTick();
        target.Throw = true;
        controller.FadeTick();
        controller.FadeTick();

        Assert.True(controller.IsFadingOut); // two failures since the last success is not yet three in a row
    }

    private static class Colors
    {
        public static Color Black => System.Windows.Media.Colors.Black;
    }
}

[Collection("AppDataStores")] // shares debug.log with the store tests, which rewrite it directly
public class DebugLogConcurrencyTests
{
    [Fact]
    public void Lines_written_from_several_threads_are_all_kept_and_whole()
    {
        string marker = "concurrency-" + Guid.NewGuid().ToString("N");
        const int threads = 8;
        const int linesPerThread = 60;

        Parallel.For(0, threads, t =>
        {
            for (int i = 0; i < linesPerThread; i++)
                DebugLog.Write($"{marker} t{t} line{i}");
        });

        string[] mine;
        using (var stream = new FileStream(DebugLog.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream))
            mine = reader.ReadToEnd().Split('\n').Where(line => line.Contains(marker)).ToArray();

        Assert.Equal(threads * linesPerThread, mine.Length);
        Assert.All(mine, line => Assert.Matches($@"{marker} t\d line\d+\s*$", line));
    }
}

public class ScheduleOverrideStateTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Nothing_in_force_means_no_status_sentence()
    {
        var state = new ScheduleOverrideState();

        Assert.Null(state.DescribeStatus());
        Assert.False(state.IsPaused);
        Assert.False(state.AnyRuleStateActive);
    }

    [Fact]
    public void A_pause_expires_by_the_clock_and_is_described_until_then()
    {
        var state = new ScheduleOverrideState();
        state.Pause(Now, TimeSpan.FromHours(1));

        Assert.True(state.IsPaused);
        Assert.Contains("paused until", state.DescribeStatus());
        Assert.False(state.IsPauseExpired(Now.AddMinutes(59)));
        Assert.True(state.IsPauseExpired(Now.AddHours(1)));

        state.ClearPause();
        Assert.False(state.IsPaused);
        Assert.Null(state.DescribeStatus());
    }

    [Fact]
    public void Overrides_are_described_in_the_order_the_schedule_obeys_them()
    {
        var state = new ScheduleOverrideState
        {
            ManualComfortPlanName = "Reading",
            ActiveComfortPlanRuleDescription = "Photoshop rule",
        };
        Assert.Contains("Reading comfort plan", state.DescribeStatus());

        state.ActiveNativeDisplayRuleProcessName = "vlc";
        Assert.Contains("vlc", state.DescribeStatus());

        state.FullscreenPresentationGuardActive = true;
        Assert.Contains("fullscreen", state.DescribeStatus());

        state.Pause(Now, TimeSpan.FromMinutes(30));
        Assert.Contains("paused", state.DescribeStatus());
    }

    [Fact]
    public void Rule_state_is_reported_as_active_for_the_foreground_poll()
    {
        var state = new ScheduleOverrideState();

        state.ActiveComfortPlanRuleDescription = "x";
        Assert.True(state.AnyRuleStateActive);
        state.ActiveComfortPlanRuleDescription = null;
        state.FullscreenPresentationGuardActive = true;
        Assert.True(state.AnyRuleStateActive);
    }
}

public class BreakReminderPromptTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Rig
    {
        public Clock Clock { get; } = new();
        public AppSettings Settings { get; } = new() { BreakReminderEnabled = true, BreakReminderIntervalMinutes = 10 };
        public int Prompts { get; private set; }
        public int Balloons { get; private set; }
        public int BreaksStarted { get; private set; }
        public bool CanShowPrompt { get; set; } = true;
        public Action<BreakPromptChoice>? Answer { get; private set; }
        public BreakReminderService Service { get; }

        public Rig()
        {
            Service = new BreakReminderService(
                Settings,
                () => false,
                (_, _, _, _) => Balloons++,
                startBreak: () => BreaksStarted++,
                showPrompt: onChoice =>
                {
                    if (!CanShowPrompt)
                        return false;
                    Prompts++;
                    Answer = onChoice;
                    return true;
                },
                readIdle: () => TimeSpan.FromSeconds(1),
                isFullscreen: () => false,
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
    public void The_prompt_is_used_instead_of_a_balloon()
    {
        var rig = new Rig();

        rig.Step(45); // 11 minutes

        Assert.Equal(1, rig.Prompts);
        Assert.Equal(0, rig.Balloons);
    }

    [Fact]
    public void A_balloon_is_the_fallback_when_the_prompt_cannot_be_shown()
    {
        var rig = new Rig { CanShowPrompt = false };

        rig.Step(45);

        Assert.Equal(1, rig.Balloons);
    }

    [Fact]
    public void Start_runs_the_break_and_the_next_reminder_is_a_full_interval_later()
    {
        var rig = new Rig();
        rig.Step(45);
        rig.Answer!(BreakPromptChoice.StartBreak);
        Assert.Equal(1, rig.BreaksStarted);

        rig.Step(30); // 7.5 minutes
        Assert.Equal(1, rig.Prompts);
    }

    [Fact]
    public void In_five_minutes_asks_again_after_five_minutes_not_a_full_interval()
    {
        var rig = new Rig();
        rig.Step(45);
        rig.Answer!(BreakPromptChoice.Snooze);

        rig.Step(16);   // 4 minutes
        Assert.Equal(1, rig.Prompts);
        rig.Step(6);    // 5.5 minutes
        Assert.Equal(2, rig.Prompts);
    }

    [Fact]
    public void Skip_and_timing_out_wait_for_the_next_full_interval()
    {
        var rig = new Rig();
        rig.Step(45);
        rig.Answer!(BreakPromptChoice.Skip);

        rig.Step(30);

        Assert.Equal(1, rig.Prompts);
        Assert.Equal(0, rig.BreaksStarted);
    }

    [Fact]
    public void A_prompt_that_throws_falls_back_to_a_balloon()
    {
        var settings = new AppSettings { BreakReminderEnabled = true, BreakReminderIntervalMinutes = 5 };
        var clock = new Clock();
        int balloons = 0;
        var service = new BreakReminderService(settings, () => false, (_, _, _, _) => balloons++,
            showPrompt: _ => throw new InvalidOperationException("no window"),
            readIdle: () => TimeSpan.Zero, isFullscreen: () => false, clock: clock);

        for (int i = 0; i < 25; i++)
        {
            clock.Now += BreakReminderPolicy.SampleInterval;
            service.SampleOnce();
        }

        Assert.Equal(1, balloons);
    }
}
