using System.Globalization;
using System.Text;

namespace MonitorWellness.Core;

/// <summary>One calendar-aligned 7-day window. Rating fields are null when no ratings fell in it.</summary>
public sealed record HistoryWeek(DateTime StartUtc, int Activations, int RatingCount, double? AverageRating);

/// <summary>
/// Pure, descriptive weekly view of the user's own local history. Deliberately makes no
/// comparison claims beyond the raw counts and never implies cause or medical meaning.
/// </summary>
public static class HistoryTrend
{
    /// <summary>Fewer rated uses than this in a week is shown as a count only, not an average.</summary>
    public const int MinimumRatingsForAverage = 3;

    /// <summary>
    /// Buckets events into <paramref name="weeks"/> consecutive 7-day windows ending at
    /// <paramref name="nowUtc"/>, oldest first. Events outside the range are ignored.
    /// </summary>
    public static IReadOnlyList<HistoryWeek> BuildWeeks(IReadOnlyList<HistoryEvent> events, DateTime nowUtc, int weeks = 8)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentOutOfRangeException.ThrowIfLessThan(weeks, 1);

        var result = new List<HistoryWeek>(weeks);
        for (int i = weeks - 1; i >= 0; i--)
        {
            DateTime end = nowUtc.AddDays(-7 * i);
            DateTime start = end.AddDays(-7);
            // (start, end] so an event on a boundary lands in exactly one window.
            var inWeek = events.Where(e => e.TimestampUtc > start && e.TimestampUtc <= end).ToList();
            var ratings = inWeek.Where(e => e.EventType == "MigraineRating" && e.Rating.HasValue)
                .Select(e => e.Rating!.Value).ToList();
            result.Add(new HistoryWeek(
                start,
                inWeek.Count(e => e.EventType == "MigraineActivated"),
                ratings.Count,
                ratings.Count > 0 ? ratings.Average() : null));
        }
        return result;
    }

    /// <summary>Plain-text, screen-reader-friendly table plus date range and sample counts.</summary>
    public static string Describe(IReadOnlyList<HistoryEvent> events, DateTime nowUtc, int weeks = 8)
    {
        var buckets = BuildWeeks(events, nowUtc, weeks);
        int totalActivations = buckets.Sum(w => w.Activations);
        int totalRatings = buckets.Sum(w => w.RatingCount);
        var culture = CultureInfo.CurrentCulture;
        string range = $"{buckets[0].StartUtc.ToLocalTime().ToString("d MMM yyyy", culture)} to {nowUtc.ToLocalTime().ToString("d MMM yyyy", culture)}";

        if (totalActivations == 0 && totalRatings == 0)
            return $"Weekly view ({range}): nothing recorded in this period.";

        var sb = new StringBuilder();
        sb.Append(CultureInfo.CurrentCulture, $"Weekly view, {range}: {totalActivations} activation(s) and {totalRatings} rating(s) in {weeks} weeks.");
        foreach (var week in buckets)
        {
            sb.AppendLine();
            sb.Append(CultureInfo.CurrentCulture, $"Week of {week.StartUtc.ToLocalTime().ToString("d MMM", culture)}: {week.Activations} activation(s)");
            if (week.AverageRating is double avg && week.RatingCount >= MinimumRatingsForAverage)
                sb.Append(CultureInfo.CurrentCulture, $", average helpfulness {avg:F1}/5 from {week.RatingCount} ratings");
            else if (week.RatingCount > 0)
                sb.Append(CultureInfo.CurrentCulture, $", {week.RatingCount} rating(s) (too few for an average)");
        }
        sb.AppendLine();
        sb.Append("These are your own records, shown for your information only. They are not medical advice and do not show what causes anything. Nothing here leaves your computer.");
        return sb.ToString();
    }
}
