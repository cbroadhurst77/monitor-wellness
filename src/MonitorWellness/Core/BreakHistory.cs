using System.Globalization;
using System.Text;

namespace MonitorWellness.Core;

/// <summary>One local calendar day of the person's own records. Counts are zero on days with nothing recorded.</summary>
public sealed record DailyActivity(
    DateOnly Date,
    int ActiveMinutes,
    int RemindersShown,
    int BreaksTaken,
    int MovementBreaks,
    int RemindersSkipped,
    int RemindersSnoozed,
    int MigraineActivations,
    int MigraineMinutes,
    double? AverageHelpfulness);

/// <summary>
/// Turns the local event history (reminders shown, breaks taken, time at the computer, Migraine Mode use)
/// into per-day rows, a plain summary, a CSV, and one careful sentence comparing days with and without
/// Migraine Mode. Pure and descriptive: it states what the person's own records show and never claims a
/// cause or a medical meaning.
/// </summary>
public static class BreakHistory
{
    // Event types written by the break reminder (see BreakReminderService); Migraine* come from MigraineModeController.
    public const string EyeReminderShown = "EyeReminderShown";
    public const string StandUpReminderShown = "StandUpReminderShown";
    public const string BreakTaken = "BreakTaken";
    public const string MovementBreakTaken = "MovementBreakTaken";
    public const string ReminderSkipped = "ReminderSkipped";
    public const string ReminderTimedOut = "ReminderTimedOut";
    public const string ReminderSnoozed = "ReminderSnoozed";
    public const string ActiveTime = "ActiveTime";

    /// <summary>The comparison needs this many days with time at the computer recorded, and this many of each kind, before it says anything.</summary>
    public const int MinimumDaysForComparison = 14;
    public const int MinimumDaysPerGroup = 3;

    public static IReadOnlyList<DailyActivity> BuildDaily(IReadOnlyList<HistoryEvent> events, DateOnly from, DateOnly to, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(zone);

        var byDay = new Dictionary<DateOnly, List<HistoryEvent>>();
        foreach (HistoryEvent evt in events)
        {
            DateOnly day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(evt.TimestampUtc, DateTimeKind.Utc), zone));
            if (day < from || day > to)
                continue;
            if (!byDay.TryGetValue(day, out List<HistoryEvent>? list))
                byDay[day] = list = new List<HistoryEvent>();
            list.Add(evt);
        }

        var rows = new List<DailyActivity>();
        for (DateOnly day = from; day <= to; day = day.AddDays(1))
        {
            List<HistoryEvent> dayEvents = byDay.GetValueOrDefault(day) ?? new List<HistoryEvent>();
            var ratings = dayEvents.Where(e => e.EventType == "MigraineRating" && e.Rating.HasValue).Select(e => e.Rating!.Value).ToList();
            rows.Add(new DailyActivity(
                day,
                ActiveMinutes: (int)Math.Round(dayEvents.Where(e => e.EventType == ActiveTime).Sum(e => e.DurationSeconds ?? 0) / 60.0),
                RemindersShown: dayEvents.Count(e => e.EventType is EyeReminderShown or StandUpReminderShown),
                BreaksTaken: dayEvents.Count(e => e.EventType is BreakTaken or MovementBreakTaken),
                MovementBreaks: dayEvents.Count(e => e.EventType == MovementBreakTaken),
                RemindersSkipped: dayEvents.Count(e => e.EventType is ReminderSkipped or ReminderTimedOut),
                RemindersSnoozed: dayEvents.Count(e => e.EventType == ReminderSnoozed),
                MigraineActivations: dayEvents.Count(e => e.EventType == "MigraineActivated"),
                MigraineMinutes: (int)Math.Round(dayEvents.Where(e => e.EventType == "MigraineDeactivated").Sum(e => e.DurationSeconds ?? 0) / 60.0),
                AverageHelpfulness: ratings.Count > 0 ? ratings.Average() : null));
        }
        return rows;
    }

    /// <summary>One row per day, with a header, in a form a spreadsheet opens directly. Numbers use the invariant culture.</summary>
    public static string ToCsv(IReadOnlyList<DailyActivity> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var builder = new StringBuilder();
        builder.AppendLine("Date,ActiveMinutes,RemindersShown,BreaksTaken,MovementBreaks,RemindersSkipped,RemindersSnoozed,MigraineActivations,MigraineMinutes,AverageHelpfulness");
        foreach (DailyActivity row in rows)
        {
            builder.AppendLine(string.Join(",",
                row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                row.ActiveMinutes.ToString(CultureInfo.InvariantCulture),
                row.RemindersShown.ToString(CultureInfo.InvariantCulture),
                row.BreaksTaken.ToString(CultureInfo.InvariantCulture),
                row.MovementBreaks.ToString(CultureInfo.InvariantCulture),
                row.RemindersSkipped.ToString(CultureInfo.InvariantCulture),
                row.RemindersSnoozed.ToString(CultureInfo.InvariantCulture),
                row.MigraineActivations.ToString(CultureInfo.InvariantCulture),
                row.MigraineMinutes.ToString(CultureInfo.InvariantCulture),
                row.AverageHelpfulness?.ToString("0.0", CultureInfo.InvariantCulture) ?? ""));
        }
        return builder.ToString();
    }

    /// <summary>The first and last day worth exporting: from the first recorded event to today. Null when there are no events.</summary>
    public static (DateOnly From, DateOnly To)? Range(IReadOnlyList<HistoryEvent> events, DateTime nowUtc, TimeZoneInfo zone)
    {
        if (events.Count == 0)
            return null;

        DateTime first = events.Min(e => e.TimestampUtc);
        return (DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(first, DateTimeKind.Utc), zone)),
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), zone)));
    }

    /// <summary>The text shown in Settings: the last seven days, then the migraine-day comparison if there is enough data.</summary>
    public static string Describe(IReadOnlyList<HistoryEvent> events, DateTime nowUtc, TimeZoneInfo zone)
    {
        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), zone));
        IReadOnlyList<DailyActivity> week = BuildDaily(events, today.AddDays(-6), today, zone);

        int shown = week.Sum(d => d.RemindersShown);
        int taken = week.Sum(d => d.BreaksTaken);
        int skipped = week.Sum(d => d.RemindersSkipped);
        int snoozed = week.Sum(d => d.RemindersSnoozed);
        int movement = week.Sum(d => d.MovementBreaks);
        var daysWithTime = week.Where(d => d.ActiveMinutes > 0).ToList();

        if (shown == 0 && taken == 0 && daysWithTime.Count == 0)
            return "Nothing recorded in the last 7 days yet. It fills in as break reminders run.";

        var text = new StringBuilder();
        if (daysWithTime.Count > 0)
            text.Append(CultureInfo.InvariantCulture, $"Last 7 days: about {daysWithTime.Average(d => d.ActiveMinutes) / 60.0:0.0} hours a day at the computer (on the {daysWithTime.Count} day(s) recorded). ");
        else
            text.Append("Last 7 days: ");

        text.Append(CultureInfo.InvariantCulture, $"{shown} reminder(s) shown, {taken} break(s) taken");
        if (movement > 0)
            text.Append(CultureInfo.InvariantCulture, $" ({movement} of them movement breaks)");
        text.Append(CultureInfo.InvariantCulture, $", {skipped} skipped, {snoozed} snoozed.");

        string? link = DescribeMigraineLink(BuildDaily(events, today.AddDays(-59), today, zone));
        if (link is not null)
            text.Append(' ').Append(link);
        return text.ToString();
    }

    /// <summary>
    /// Compares days with Migraine Mode against other days, if (and only if) there is enough data for that
    /// to mean anything. Describes the records and says plainly that it is not a finding about cause.
    /// </summary>
    public static string? DescribeMigraineLink(IReadOnlyList<DailyActivity> rows)
    {
        var recorded = rows.Where(d => d.ActiveMinutes > 0).ToList();
        var migraineDays = recorded.Where(d => d.MigraineActivations > 0).ToList();
        var otherDays = recorded.Where(d => d.MigraineActivations == 0).ToList();

        if (recorded.Count < MinimumDaysForComparison || migraineDays.Count < MinimumDaysPerGroup || otherDays.Count < MinimumDaysPerGroup)
        {
            return recorded.Count == 0 || rows.All(d => d.MigraineActivations == 0)
                ? null
                : "Not enough days recorded yet to compare days with Migraine Mode against other days (it needs about two weeks, with at least three of each kind).";
        }

        double migraineHours = migraineDays.Average(d => d.ActiveMinutes) / 60.0;
        double otherHours = otherDays.Average(d => d.ActiveMinutes) / 60.0;
        double migraineBreaks = BreaksPerHour(migraineDays);
        double otherBreaks = BreaksPerHour(otherDays);

        return FormattableString.Invariant(
            $"On the {migraineDays.Count} day(s) you used Migraine Mode you spent about {migraineHours:0.0} hours at the computer and took {migraineBreaks:0.0} breaks an hour; on the other {otherDays.Count} day(s), {otherHours:0.0} hours and {otherBreaks:0.0} breaks an hour. ")
            + "This is a pattern in your own records, not a finding about what causes migraines.";
    }

    private static double BreaksPerHour(IReadOnlyList<DailyActivity> days)
    {
        double hours = days.Sum(d => d.ActiveMinutes) / 60.0;
        return hours <= 0 ? 0 : days.Sum(d => d.BreaksTaken) / hours;
    }
}
