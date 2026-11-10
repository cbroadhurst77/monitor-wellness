using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class FixedTimeAndWakeTimeTests
{
    private static TimeSpan T(string hhmm) => TimeSpan.Parse(hhmm, System.Globalization.CultureInfo.InvariantCulture);
    private static DateTime At(string hhmm) => new DateTime(2026, 11, 10).Add(T(hhmm));

    // ---- fixed-time validation --------------------------------------------------------------

    [Fact]
    public void Validate_AcceptsOrdinaryTimes_AndANightShiftThatWrapsPastMidnight()
    {
        Assert.Null(FixedTimeSchedule.Validate("07:00", "19:00"));
        Assert.Null(FixedTimeSchedule.Validate("20:00", "08:00")); // works nights: the "day" starts at 20:00
    }

    [Theory]
    [InlineData("7am", "19:00")]
    [InlineData("07:00", "")]
    [InlineData("07:00", "25:00")]
    [InlineData(null, "19:00")]
    public void Validate_RejectsUnreadableTimes(string? sunrise, string? sunset)
        => Assert.NotNull(FixedTimeSchedule.Validate(sunrise, sunset));

    [Theory]
    [InlineData("07:00", "09:00")]   // 2 h of day: too short for the twilight ramps
    [InlineData("07:00", "07:00")]
    [InlineData("04:00", "03:00")]   // 23 h of day: no room for a night
    public void Validate_RejectsADayThatIsTooShortOrTooLong(string sunrise, string sunset)
        => Assert.Contains("between 4 and 20 hours", FixedTimeSchedule.Validate(sunrise, sunset));

    // ---- the synthetic elevation ------------------------------------------------------------

    [Theory]
    [InlineData("12:00", ScheduleCurve.DayThresholdDeg)]       // middle of the day: full day
    [InlineData("03:00", ScheduleCurve.DeepNightThresholdDeg)] // small hours: deep night
    [InlineData("07:00", -1.5)]                                 // the centre of the morning transition
    [InlineData("19:00", -1.5)]                                 // the centre of the evening transition
    [InlineData("20:00", -8.0)]                                 // a third of the way into the post-sunset deepening
    public void SyntheticElevation_HasTheSunsShapeAtKeyTimes(string time, double expected)
        => Assert.Equal(expected, FixedTimeSchedule.GetSyntheticElevationDegrees(T(time), T("07:00"), T("19:00")), precision: 6);

    [Fact]
    public void SyntheticElevation_ChangesSmoothly_AllDayAndAcrossMidnight()
    {
        double previous = FixedTimeSchedule.GetSyntheticElevationDegrees(TimeSpan.Zero, T("07:00"), T("19:00"));
        for (int minute = 1; minute <= 1440; minute++)
        {
            double current = FixedTimeSchedule.GetSyntheticElevationDegrees(TimeSpan.FromMinutes(minute % 1440), T("07:00"), T("19:00"));
            Assert.True(Math.Abs(current - previous) <= 0.2, $"jump of {Math.Abs(current - previous):F2} degrees at minute {minute}");
            previous = current;
        }
    }

    [Fact]
    public void SyntheticElevation_WorksForANightShift()
    {
        // Day starts at 20:00 and the evening at 08:00.
        Assert.Equal(ScheduleCurve.DayThresholdDeg, FixedTimeSchedule.GetSyntheticElevationDegrees(T("02:00"), T("20:00"), T("08:00")), precision: 6);
        Assert.Equal(ScheduleCurve.DeepNightThresholdDeg, FixedTimeSchedule.GetSyntheticElevationDegrees(T("14:00"), T("20:00"), T("08:00")), precision: 6);
    }

    [Fact]
    public void SyntheticElevation_DrivesTheNormalScheduleCurve()
    {
        double noon = FixedTimeSchedule.GetSyntheticElevationDegrees(T("12:00"), T("07:00"), T("19:00"));
        double night = FixedTimeSchedule.GetSyntheticElevationDegrees(T("03:00"), T("07:00"), T("19:00"));
        double sunrise = FixedTimeSchedule.GetSyntheticElevationDegrees(T("07:00"), T("07:00"), T("19:00"));

        Assert.Equal(6500, ScheduleCurve.GetTargetKelvin(noon, 6500, 3400));
        Assert.Equal(3400, ScheduleCurve.GetTargetKelvin(night, 6500, 3400));
        Assert.Equal(1.0, ScheduleCurve.GetDeepNightFactor(night));          // deep night comes by itself, as with the sun
        Assert.InRange(ScheduleCurve.GetTargetKelvin(sunrise, 6500, 3400), 4900, 5000); // halfway through the transition
    }

    // ---- choosing the source -----------------------------------------------------------------

    [Fact]
    public void FixedTimes_UseTheLocalClock_NotTheUtcInstant()
    {
        var settings = new AppSettings { ScheduleMode = ScheduleModes.FixedTimes };
        DateTime unrelatedUtc = new(2026, 11, 10, 3, 0, 0, DateTimeKind.Utc);

        double elevation = ScheduleSource.GetElevationDegrees(settings, unrelatedUtc, At("12:00"));

        Assert.Equal(ScheduleCurve.DayThresholdDeg, elevation, precision: 6);
        Assert.True(ScheduleSource.UsesFixedTimes(settings));
    }

    [Fact]
    public void SunMode_StillUsesTheRealSun_AndAnInvalidSavedPairFallsBackToIt()
    {
        DateTime utc = new(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
        double sun = SolarCalculator.GetSolarElevationDegrees(utc, 51.5072, -0.1276);

        Assert.Equal(sun, ScheduleSource.GetElevationDegrees(new AppSettings(), utc, At("03:00")));

        var broken = new AppSettings { ScheduleMode = ScheduleModes.FixedTimes, FixedSunriseLocal = "nonsense" };
        Assert.False(ScheduleSource.UsesFixedTimes(broken));
        Assert.Equal(sun, ScheduleSource.GetElevationDegrees(broken, utc, At("03:00")));
    }

    [Fact]
    public void FixedTimes_NeedNoLocation_SoTheLondonPlaceholderIsNotFlagged()
    {
        Assert.True(LocationStatus.IsUsingUnconfirmedDefault(new AppSettings()));
        Assert.False(LocationStatus.IsUsingUnconfirmedDefault(new AppSettings { ScheduleMode = ScheduleModes.FixedTimes }));
    }

    // ---- settings -----------------------------------------------------------------------------

    [Fact]
    public void Settings_DefaultToTheSun_AndTheNewFieldsRoundTrip()
    {
        var defaults = new AppSettings();
        Assert.Equal(ScheduleModes.Sun, defaults.ScheduleMode);
        Assert.Null(defaults.WakeTimeLocal);

        var custom = new AppSettings
        {
            ScheduleMode = ScheduleModes.FixedTimes,
            FixedSunriseLocal = "06:30",
            FixedSunsetLocal = "20:15",
            BedtimeLocal = "23:00",
            WakeTimeLocal = "07:15",
        };
        var clone = custom.Clone();
        Assert.Equal(("FixedTimes", "06:30", "20:15", "07:15"), (clone.ScheduleMode, clone.FixedSunriseLocal, clone.FixedSunsetLocal, clone.WakeTimeLocal));

        var target = new AppSettings();
        target.CopyFrom(custom);
        Assert.Equal("07:15", target.WakeTimeLocal);
        Assert.Equal("20:15", target.FixedSunsetLocal);
    }

    [Fact]
    public void Validator_ChecksTheNewFields()
    {
        Assert.True(AppSettingsValidator.TryValidate(new AppSettings { BedtimeLocal = "22:30", WakeTimeLocal = "06:30" }, out _));
        Assert.True(AppSettingsValidator.TryValidate(new AppSettings { WakeTimeLocal = "06:30" }, out _)); // no bedtime: simply unused
        Assert.True(AppSettingsValidator.TryValidate(new AppSettings { ScheduleMode = ScheduleModes.FixedTimes, FixedSunriseLocal = "06:00", FixedSunsetLocal = "21:00" }, out _));

        Assert.False(AppSettingsValidator.TryValidate(new AppSettings { BedtimeLocal = "22:30", WakeTimeLocal = "late" }, out string wakeError));
        Assert.Contains("Wake-up time", wakeError);
        Assert.False(AppSettingsValidator.TryValidate(new AppSettings { BedtimeLocal = "22:30", WakeTimeLocal = "22:30" }, out string sameError));
        Assert.Contains("different from bedtime", sameError);
        Assert.False(AppSettingsValidator.TryValidate(new AppSettings { ScheduleMode = "Moon" }, out _));
        Assert.False(AppSettingsValidator.TryValidate(new AppSettings { ScheduleMode = ScheduleModes.FixedTimes, FixedSunriseLocal = "07:00", FixedSunsetLocal = "08:00" }, out string dayError));
        Assert.Contains("between 4 and 20 hours", dayError);
    }

    // ---- bedtime with a wake-up time ----------------------------------------------------------

    [Theory]
    [InlineData("22:30", 1.0)]    // bedtime
    [InlineData("02:00", 1.0)]    // asleep
    [InlineData("06:29", 1.0)]    // just before waking
    [InlineData("07:00", 0.0)]    // 30 minutes after waking: fully eased off
    [InlineData("12:00", 0.0)]    // midday
    [InlineData("20:59", 0.0)]    // more than 90 minutes before bedtime
    public void WakeFactor_HoldsUntilTheWakeTime_ThenStops(string time, double expected)
        => Assert.Equal(expected, ScheduleCurve.GetBedtimeWakeFactor(At(time), T("22:30"), T("06:30")), precision: 6);

    [Fact]
    public void WakeFactor_EasesOffOverHalfAnHourAfterWaking_AndRampsUpBeforeBedtime()
    {
        Assert.Equal(0.5, ScheduleCurve.GetBedtimeWakeFactor(At("06:45"), T("22:30"), T("06:30")), precision: 6);
        double beforeBed = ScheduleCurve.GetBedtimeWakeFactor(At("21:45"), T("22:30"), T("06:30"));
        Assert.InRange(beforeBed, 0.1, 0.9);
        Assert.True(ScheduleCurve.GetBedtimeWakeFactor(At("22:20"), T("22:30"), T("06:30")) > beforeBed);
    }

    [Fact]
    public void WakeFactor_FixesTheMorningTheTenHourGuessGotWrong()
    {
        // Up at 06:30 and out the door: the old ten-hour guess still had the screen at night level at 08:00.
        Assert.Equal(1.0, ScheduleCurve.GetBedtimeFactor(At("08:00"), T("22:30")), precision: 6);
        Assert.Equal(0.0, ScheduleCurve.GetBedtimeWakeFactor(At("08:00"), T("22:30"), T("06:30")), precision: 6);
    }

    [Fact]
    public void WakeFactor_WorksForAnEarlyBedtimeAfterMidnight()
    {
        // Bedtime 01:00, wake 09:00.
        Assert.Equal(1.0, ScheduleCurve.GetBedtimeWakeFactor(At("05:00"), T("01:00"), T("09:00")), precision: 6);
        Assert.Equal(0.0, ScheduleCurve.GetBedtimeWakeFactor(At("12:00"), T("01:00"), T("09:00")), precision: 6);
        Assert.Equal(1.0, ScheduleCurve.GetBedtimeWakeFactor(At("00:59"), T("01:00"), T("09:00")), precision: 1);
    }

    [Fact]
    public void TargetCalculator_UsesTheWakeTime_WhenOneIsSet()
    {
        ScheduleTargetInputs Inputs(string? wake) => new(
            DayKelvin: 6500, NightKelvin: 3400, DayBrightness: 1.0, NightBrightness: 0.85, DeepNightBrightness: 0.7,
            SolarElevationDegrees: 25, BedtimeLocal: "22:30", NowLocal: At("08:00"),
            DeepNightOverlayColorHex: "#190C04", WakeTimeLocal: wake);

        ScheduleTarget guessed = ScheduleTargetCalculator.Compute(Inputs(null));
        ScheduleTarget withWake = ScheduleTargetCalculator.Compute(Inputs("06:30"));

        Assert.Equal(0.7, guessed.GlobalBrightness, precision: 6);   // old behaviour: still dimmed at 08:00 in full sun
        Assert.Equal(1.0, withWake.GlobalBrightness, precision: 6);  // with a wake time: back to full daytime brightness
    }
}
