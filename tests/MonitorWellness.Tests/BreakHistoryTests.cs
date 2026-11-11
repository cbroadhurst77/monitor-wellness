#pragma warning disable CA1861 // small literal arrays in tests are clearer than shared static fields
using System.Globalization;
using System.IO;
using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class BreakHistoryAggregationTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly TimeZoneInfo Sydney = TimeZoneInfo.CreateCustomTimeZone("test+10", TimeSpan.FromHours(10), "test+10", "test+10");

    private static HistoryEvent At(string utc, string type, int? seconds = null, int? rating = null)
        => new(DateTime.Parse(utc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal), type, null, seconds, rating);

    private static readonly DateOnly Day = new(2026, 11, 10);

    [Fact]
    public void Events_are_counted_into_the_right_columns_for_their_day()
    {
        var events = new[]
        {
            At("2026-11-10T09:00:00", BreakHistory.EyeReminderShown),
            At("2026-11-10T09:00:10", BreakHistory.BreakTaken),
            At("2026-11-10T10:00:00", BreakHistory.StandUpReminderShown),
            At("2026-11-10T10:00:05", BreakHistory.MovementBreakTaken),
            At("2026-11-10T11:00:00", BreakHistory.EyeReminderShown),
            At("2026-11-10T11:00:05", BreakHistory.ReminderSkipped),
            At("2026-11-10T12:00:00", BreakHistory.EyeReminderShown),
            At("2026-11-10T12:00:30", BreakHistory.ReminderTimedOut),
            At("2026-11-10T13:00:00", BreakHistory.EyeReminderShown),
            At("2026-11-10T13:00:05", BreakHistory.ReminderSnoozed),
            At("2026-11-10T09:30:00", BreakHistory.ActiveTime, seconds: 1800),
            At("2026-11-10T10:30:00", BreakHistory.ActiveTime, seconds: 3000),
            At("2026-11-10T14:00:00", "MigraineActivated"),
            At("2026-11-10T14:40:00", "MigraineDeactivated", seconds: 2400),
            At("2026-11-10T14:41:00", "MigraineRating", rating: 4),
            At("2026-11-10T15:41:00", "MigraineRating", rating: 5),
        };

        DailyActivity row = Assert.Single(BreakHistory.BuildDaily(events, Day, Day, Utc));

        Assert.Equal(80, row.ActiveMinutes);
        Assert.Equal(5, row.RemindersShown);
        Assert.Equal(2, row.BreaksTaken);
        Assert.Equal(1, row.MovementBreaks);
        Assert.Equal(2, row.RemindersSkipped);
        Assert.Equal(1, row.RemindersSnoozed);
        Assert.Equal(1, row.MigraineActivations);
        Assert.Equal(40, row.MigraineMinutes);
        Assert.Equal(4.5, row.AverageHelpfulness);
    }

    [Fact]
    public void Days_follow_the_local_calendar_not_utc_and_every_day_in_the_range_gets_a_row()
    {
        // 15:30 UTC is 01:30 the next day at UTC+10.
        var events = new[] { At("2026-11-10T15:30:00", BreakHistory.ActiveTime, seconds: 600) };

        var rows = BreakHistory.BuildDaily(events, Day, Day.AddDays(2), Sydney);

        Assert.Equal(3, rows.Count);
        Assert.Equal(0, rows[0].ActiveMinutes);
        Assert.Equal(10, rows[1].ActiveMinutes);
        Assert.Equal(Day.AddDays(1), rows[1].Date);
        Assert.Equal(0, rows[2].ActiveMinutes);
    }

    [Fact]
    public void Events_outside_the_range_are_ignored()
    {
        var events = new[] { At("2026-11-09T12:00:00", BreakHistory.EyeReminderShown), At("2026-11-11T12:00:00", BreakHistory.EyeReminderShown) };

        Assert.Equal(0, BreakHistory.BuildDaily(events, Day, Day, Utc).Single().RemindersShown);
    }

    [Fact]
    public void The_csv_has_a_header_one_row_per_day_and_always_uses_the_invariant_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE"); // decimal comma
            var rows = BreakHistory.BuildDaily(new[] { At("2026-11-10T09:00:00", "MigraineRating", rating: 4), At("2026-11-10T10:00:00", "MigraineRating", rating: 5) }, Day, Day.AddDays(1), Utc);

            string[] lines = BreakHistory.ToCsv(rows).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

            Assert.Equal("Date,ActiveMinutes,RemindersShown,BreaksTaken,MovementBreaks,RemindersSkipped,RemindersSnoozed,MigraineActivations,MigraineMinutes,AverageHelpfulness", lines[0]);
            Assert.Equal("2026-11-10,0,0,0,0,0,0,0,0,4.5", lines[1]);
            Assert.Equal("2026-11-11,0,0,0,0,0,0,0,0,", lines[2]);
            Assert.Equal(3, lines.Length);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void The_export_range_runs_from_the_first_event_to_today_and_is_empty_without_events()
    {
        var now = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

        Assert.Null(BreakHistory.Range(Array.Empty<HistoryEvent>(), now, Utc));
        var range = BreakHistory.Range(new[] { At("2026-11-10T09:00:00", BreakHistory.EyeReminderShown) }, now, Utc);
        Assert.Equal((Day, new DateOnly(2026, 11, 20)), range);
    }
}

public class BreakHistoryDescriptionTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    private static HistoryEvent At(DateTime utc, string type, int? seconds = null) => new(utc, type, null, seconds);

    [Fact]
    public void With_nothing_recorded_it_says_so_and_does_not_make_up_numbers()
        => Assert.Contains("Nothing recorded", BreakHistory.Describe(Array.Empty<HistoryEvent>(), Now, Utc));

    [Fact]
    public void The_last_seven_days_are_summarised_in_plain_words()
    {
        var events = new List<HistoryEvent>();
        for (int d = 0; d < 3; d++)
        {
            DateTime day = Now.Date.AddDays(-d);
            events.Add(At(day.AddHours(9), BreakHistory.ActiveTime, 4 * 3600));
            events.Add(At(day.AddHours(10), BreakHistory.EyeReminderShown));
            events.Add(At(day.AddHours(10).AddSeconds(5), BreakHistory.BreakTaken));
            events.Add(At(day.AddHours(11), BreakHistory.EyeReminderShown));
            events.Add(At(day.AddHours(11).AddSeconds(5), BreakHistory.ReminderSkipped));
        }

        string text = BreakHistory.Describe(events, Now, Utc);

        Assert.Contains("about 4.0 hours a day", text);
        Assert.Contains("6 reminder(s) shown, 3 break(s) taken", text);
        Assert.Contains("3 skipped", text);
        Assert.DoesNotContain("Migraine", text);
    }

    private static List<HistoryEvent> TwoWeeks(int migraineDays, int activeMinutesOnMigraineDays, int activeMinutesOtherDays)
    {
        var events = new List<HistoryEvent>();
        for (int d = 0; d < 20; d++)
        {
            DateTime day = Now.Date.AddDays(-d);
            bool migraine = d < migraineDays;
            events.Add(At(day.AddHours(9), BreakHistory.ActiveTime, (migraine ? activeMinutesOnMigraineDays : activeMinutesOtherDays) * 60));
            events.Add(At(day.AddHours(10), BreakHistory.BreakTaken));
            if (migraine)
                events.Add(At(day.AddHours(12), "MigraineActivated"));
        }
        return events;
    }

    [Fact]
    public void With_enough_days_it_compares_migraine_days_with_other_days_and_says_it_is_not_a_cause()
    {
        string text = BreakHistory.Describe(TwoWeeks(migraineDays: 5, activeMinutesOnMigraineDays: 480, activeMinutesOtherDays: 360), Now, Utc);

        Assert.Contains("On the 5 day(s) you used Migraine Mode you spent about 8.0 hours", text);
        Assert.Contains("on the other 15 day(s), 6.0 hours", text);
        Assert.Contains("not a finding about what causes migraines", text);
    }

    [Theory]
    [InlineData(2)]   // too few migraine days
    [InlineData(18)]  // too few other days
    public void With_too_few_days_of_either_kind_it_says_it_needs_more_instead_of_comparing(int migraineDays)
    {
        string text = BreakHistory.Describe(TwoWeeks(migraineDays, 480, 360), Now, Utc);

        Assert.Contains("Not enough days recorded yet", text);
        Assert.DoesNotContain("hours at the computer and took", text);
    }

    [Fact]
    public void With_no_migraine_use_at_all_there_is_nothing_to_compare_and_nothing_is_said_about_it()
    {
        string text = BreakHistory.Describe(TwoWeeks(migraineDays: 0, 480, 360), Now, Utc);

        Assert.DoesNotContain("Migraine", text);
        Assert.DoesNotContain("Not enough days", text);
    }
}

public class ActiveTimeTrackerCountedTests
{
    private static readonly DateTime T0 = new(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Period = TimeSpan.FromSeconds(15);

    [Fact]
    public void Each_sample_reports_how_much_it_added_and_zero_when_it_only_anchors_or_the_person_is_away()
    {
        var tracker = new ActiveTimeTracker();
        tracker.Sample(T0, TimeSpan.Zero, Period, TimeSpan.FromMinutes(5));
        Assert.Equal(TimeSpan.Zero, tracker.LastCounted);

        tracker.Sample(T0 + Period, TimeSpan.Zero, Period, TimeSpan.FromMinutes(5));
        Assert.Equal(Period, tracker.LastCounted);

        tracker.Sample(T0 + Period * 2, TimeSpan.FromMinutes(6), Period, TimeSpan.FromMinutes(5));
        Assert.Equal(TimeSpan.Zero, tracker.LastCounted);
    }
}

public class BreakHistoryRecordingTests
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
        public List<HistoryEvent> Recorded { get; } = new();
        public Action<BreakPromptChoice>? Answer { get; private set; }
        public bool RecorderThrows { get; set; }
        public BreakReminderService Service { get; }

        public Rig()
        {
            Service = new BreakReminderService(
                Settings, () => false, (_, _, _, _) => { },
                showPrompt: onChoice => { Answer = onChoice; return true; },
                readIdle: () => TimeSpan.FromSeconds(1), isFullscreen: () => false, clock: Clock, isInCall: () => false,
                recordHistory: evt => { if (RecorderThrows) throw new IOException("disk full"); Recorded.Add(evt); });
        }

        public void StepMinutes(double minutes)
        {
            for (int i = 0; i < (int)Math.Round(minutes * 4); i++)
            {
                Clock.Now += BreakReminderPolicy.SampleInterval;
                Service.SampleOnce();
            }
        }

        public string[] Types => Recorded.Select(e => e.EventType).ToArray();
    }

    [Fact]
    public void With_the_history_off_nothing_is_recorded()
    {
        var rig = new Rig();

        rig.StepMinutes(25);
        rig.Answer!(BreakPromptChoice.Skip);
        rig.Service.NotifyBreakTaken();
        rig.Service.Dispose();

        Assert.Empty(rig.Recorded);
    }

    [Fact]
    public void A_reminder_and_what_the_person_did_with_it_are_recorded()
    {
        var rig = new Rig();
        rig.Settings.BreakHistoryEnabled = true;

        rig.StepMinutes(11);
        rig.Answer!(BreakPromptChoice.Snooze);
        rig.StepMinutes(6);
        rig.Answer!(BreakPromptChoice.Skip);
        rig.StepMinutes(11);
        rig.Answer!(BreakPromptChoice.TimedOut);
        rig.Service.NotifyBreakTaken();
        rig.Service.NotifyBreakTaken(movement: true);

        Assert.Equal(
            new[]
            {
                BreakHistory.EyeReminderShown, BreakHistory.ReminderSnoozed,
                BreakHistory.EyeReminderShown, BreakHistory.ReminderSkipped,
                BreakHistory.EyeReminderShown, BreakHistory.ReminderTimedOut,
                BreakHistory.BreakTaken, BreakHistory.MovementBreakTaken,
            },
            rig.Types.Where(t => t != BreakHistory.ActiveTime).ToArray());
    }

    [Fact]
    public void A_test_reminder_is_not_a_real_one_and_is_not_recorded()
    {
        var rig = new Rig();
        rig.Settings.BreakHistoryEnabled = true;

        rig.Service.ShowTestReminder();

        Assert.Empty(rig.Recorded);
    }

    [Fact]
    public void Time_at_the_computer_is_written_in_ten_minute_chunks_and_the_rest_on_exit()
    {
        var rig = new Rig();
        rig.Settings.BreakHistoryEnabled = true;

        rig.StepMinutes(25);
        var chunks = rig.Recorded.Where(e => e.EventType == BreakHistory.ActiveTime).ToList();
        Assert.Equal(2, chunks.Count);
        Assert.All(chunks, chunk => Assert.InRange(chunk.DurationSeconds!.Value, 600, 615));

        rig.Service.Dispose();
        var all = rig.Recorded.Where(e => e.EventType == BreakHistory.ActiveTime).ToList();
        Assert.Equal(3, all.Count);
        Assert.InRange(all.Sum(e => e.DurationSeconds!.Value), 24 * 60, 25 * 60); // about 25 minutes (the first sample only anchors the clock)
    }

    [Fact]
    public void Time_before_and_after_midnight_goes_to_the_right_day()
    {
        var rig = new Rig();
        rig.Settings.BreakHistoryEnabled = true;
        rig.Clock.Now = new DateTimeOffset(2026, 11, 11, 23, 50, 0, TimeSpan.Zero);

        rig.StepMinutes(20);
        rig.Service.Dispose();

        var chunks = rig.Recorded.Where(e => e.EventType == BreakHistory.ActiveTime).ToList();
        var days = chunks.Select(c => c.TimestampUtc.Date).Distinct().ToList();
        Assert.Equal(2, days.Count);
        Assert.All(chunks.Where(c => c.TimestampUtc.Date == new DateTime(2026, 11, 11)), c => Assert.True(c.TimestampUtc < new DateTime(2026, 11, 12)));
    }

    [Fact]
    public void Being_away_adds_no_time_at_the_computer()
    {
        var rig = new Rig();
        rig.Settings.BreakHistoryEnabled = true;
        var service = new BreakReminderService(rig.Settings, () => false, (_, _, _, _) => { },
            readIdle: () => TimeSpan.FromMinutes(30), isFullscreen: () => false, clock: rig.Clock, isInCall: () => false,
            recordHistory: rig.Recorded.Add);

        for (int i = 0; i < 100; i++)
        {
            rig.Clock.Now += BreakReminderPolicy.SampleInterval;
            service.SampleOnce();
        }
        service.Dispose();

        Assert.DoesNotContain(rig.Recorded, e => e.EventType == BreakHistory.ActiveTime);
    }

    [Fact]
    public void A_history_that_cannot_be_written_never_breaks_the_reminder()
    {
        var rig = new Rig { RecorderThrows = true };
        rig.Settings.BreakHistoryEnabled = true;

        Assert.Null(Record.Exception(() => rig.StepMinutes(25)));
        Assert.Null(Record.Exception(() => rig.Service.NotifyBreakTaken()));
        Assert.Null(Record.Exception(rig.Service.Dispose));
        Assert.NotNull(rig.Answer); // the reminders still came
    }

    [Fact]
    public void The_break_history_option_has_a_safe_default_and_survives_clone_and_copy()
    {
        Assert.False(new AppSettings().BreakHistoryEnabled);

        var original = new AppSettings { BreakHistoryEnabled = true };
        Assert.True(original.Clone().BreakHistoryEnabled);
        var target = new AppSettings();
        target.CopyFrom(original);
        Assert.True(target.BreakHistoryEnabled);
    }
}
