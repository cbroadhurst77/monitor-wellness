#pragma warning disable CA1861 // small literal arrays in tests are clearer than shared static fields
using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class SettingsRepairCoverageTests
{
    /// <summary>One way to break each rule the validator enforces. If a rule is ever added to the validator without a repair group, its line here fails.</summary>
    private static readonly (string Name, Action<AppSettings> Break)[] Breakages =
    {
        ("latitude", s => s.Latitude = 91),
        ("longitude", s => s.Longitude = -181),
        ("day kelvin", s => s.DayKelvin = 100),
        ("night kelvin", s => s.NightKelvin = 100),
        ("day brightness", s => s.DayBrightness = 0),
        ("night brightness", s => s.NightBrightness = 2),
        ("deep night brightness", s => s.DeepNightBrightness = -1),
        ("migraine opacity", s => s.MigraineOverlayOpacity = 2),
        ("migraine contrast", s => s.MigraineContrastReduction = 0.9),
        ("deep night colour", s => s.DeepNightOverlayColorHex = "not a colour"),
        ("migraine colour", s => s.MigraineOverlayColorHex = ""),
        ("response plan", s => s.DefaultMigraineResponsePlan = "bogus"),
        ("bedtime", s => s.BedtimeLocal = "25:99"),
        ("wake time", s => s.WakeTimeLocal = "later"),
        ("wake equals bedtime", s => { s.BedtimeLocal = "22:00"; s.WakeTimeLocal = "22:00"; }),
        ("schedule mode", s => s.ScheduleMode = "bogus"),
        ("fixed times", s => { s.ScheduleMode = ScheduleModes.FixedTimes; s.FixedSunriseLocal = "x"; s.FixedSunsetLocal = "y"; }),
        ("auto revert low", s => s.MigraineAutoRevertMinutes = -1),
        ("auto revert high", s => s.MigraineAutoRevertMinutes = 99_999),
        ("break interval low", s => s.BreakReminderIntervalMinutes = 0),
        ("break interval high", s => s.BreakReminderIntervalMinutes = 99_999),
        ("hotkey modifiers", s => s.MigraineHotkeyModifiers = 0),
        ("hotkey key", s => s.MigraineHotkeyKey = 0),
        ("excluded null", s => s.ExcludedMonitors = null!),
        ("colour excluded null", s => s.ColorExcludedMonitors = null!),
        ("multipliers null", s => s.MonitorDimMultiplier = null!),
        ("offsets null", s => s.MonitorKelvinOffset = null!),
        ("hardware list null", s => s.HardwareBrightnessEnabledMonitors = null!),
        ("hardware safety null", s => s.HardwareBrightnessSafetyByMonitor = null!),
        ("rules null", s => s.ApplicationComfortRules = null!),
        ("multiplier range", s => s.MonitorDimMultiplier["m"] = 50),
        ("offset range", s => s.MonitorKelvinOffset["m"] = 100_000),
        ("excluded too many", s => s.ExcludedMonitors.AddRange(Enumerable.Range(0, 80).Select(i => "m" + i))),
        ("safety approved and quarantined", s => s.HardwareBrightnessSafetyByMonitor["k"] = new HardwareBrightnessSafetyState { IsApproved = true, IsQuarantined = true }),
        ("safety failure count", s => s.HardwareBrightnessSafetyByMonitor["k"] = new HardwareBrightnessSafetyState { FailureCount = 5000 }),
        ("rule bad process", s => s.ApplicationComfortRules.Add(new ApplicationComfortRule { ProcessName = "..\\x", Action = ApplicationComfortActions.RestoreNativeDisplay })),
        ("rule bad action", s => s.ApplicationComfortRules.Add(new ApplicationComfortRule { ProcessName = "vlc.exe", Action = "bogus" })),
    };

    [Fact]
    public void Every_validator_rule_can_be_repaired_without_losing_unrelated_settings()
    {
        var failures = new List<string>();
        foreach (var (name, breakIt) in Breakages)
        {
            var settings = new AppSettings { DayKelvin = 5800, HasCompletedOnboarding = true, Latitude = 51.5, Longitude = -0.1 };
            breakIt(settings);
            if (AppSettingsValidator.TryValidate(settings, out _))
            {
                failures.Add($"{name}: the breakage did not make the settings invalid (fix the test)");
                continue;
            }

            List<string> repaired = SettingsRepair.Repair(settings);

            if (!AppSettingsValidator.TryValidate(settings, out string error))
                failures.Add($"{name}: still invalid after repair ({error})");
            if (repaired.Count == 0)
                failures.Add($"{name}: nothing was reported as repaired");
            if (!settings.HasCompletedOnboarding)
                failures.Add($"{name}: an unrelated setting (onboarding) was lost");
            if (name is not ("latitude" or "longitude" or "day kelvin") && (settings.Latitude != 51.5 || settings.Longitude != -0.1))
                failures.Add($"{name}: the location was lost");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}

public class AwayThresholdTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static int Shown(Func<int, TimeSpan> idleAtSample, int samples)
    {
        var settings = new AppSettings { BreakReminderEnabled = true, BreakReminderIntervalMinutes = 10 };
        var clock = new Clock();
        int sample = 0;
        int shown = 0;
        var service = new BreakReminderService(settings, () => false, (_, _, _, _) => shown++,
            readIdle: () => idleAtSample(sample), isFullscreen: () => false, clock: clock);
        for (sample = 1; sample <= samples; sample++)
        {
            clock.Now += BreakReminderPolicy.SampleInterval;
            service.SampleOnce();
        }
        return shown;
    }

    [Fact]
    public void Reading_with_no_input_for_a_few_minutes_at_a_time_still_gets_a_reminder()
    {
        // Typing for a minute, then three minutes of reading with no input, repeatedly, for 25 minutes.
        int shown = Shown(sample => sample % 16 < 4 ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds((sample % 16 - 3) * 15), 100);

        Assert.True(shown >= 1);
    }

    [Fact]
    public void Really_leaving_the_computer_for_ten_minutes_restarts_the_count()
    {
        // Active for 8 minutes, then gone for 10 (idle keeps growing), then active again for 8. The reminder comes
        // due while the person is already away, so it is held, and the count restarts once they have really left.
        int shown = Shown(sample =>
            sample <= 32 ? TimeSpan.FromSeconds(3)
            : sample <= 72 ? TimeSpan.FromMinutes((sample - 32) * 0.25 + 1)
            : TimeSpan.FromSeconds(3), 104);

        Assert.Equal(0, shown);
    }
}
