using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class HistorySummarizerTests
{
    private static readonly DateTime Now = new(2026, 1, 31, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EmptyHistory_ProducesZeroedSummary()
    {
        var summary = HistorySummarizer.Summarize(Array.Empty<HistoryEvent>(), Now);

        Assert.Equal(0, summary.TotalActivations);
        Assert.Equal(0, summary.ActivationsLast7Days);
        Assert.Equal(0, summary.ActivationsPrevious7Days);
        Assert.Equal(0, summary.ActivationsLast30Days);
        Assert.Null(summary.AverageDurationMinutes);
        Assert.Equal(0, summary.MildCount);
        Assert.Equal(0, summary.FullCount);
        Assert.Equal(0, summary.PauseCount);
        Assert.Null(summary.AverageRating);
        Assert.Equal(0, summary.RatingCount);
    }

    [Fact]
    public void AverageRatingOnlyCountsRatingEventsWithAValue()
    {
        var events = new[]
        {
            new HistoryEvent(Now, "MigraineRating", null, null, 4),
            new HistoryEvent(Now, "MigraineRating", null, null, 2),
            new HistoryEvent(Now, "MigraineRating", null, null, null), // skipped by the user — excluded
            new HistoryEvent(Now, "MigraineActivated", false, null),   // wrong event type — excluded
        };

        var summary = HistorySummarizer.Summarize(events, Now);

        Assert.Equal(2, summary.RatingCount);
        Assert.NotNull(summary.AverageRating);
        Assert.Equal(3.0, summary.AverageRating!.Value, precision: 5);
    }

    [Fact]
    public void CountsActivationsWithinLast7And30Days()
    {
        var events = new[]
        {
            new HistoryEvent(Now.AddDays(-1), "MigraineActivated", false, null),   // within 7 and 30
            new HistoryEvent(Now.AddDays(-10), "MigraineActivated", false, null),  // within 30 only
            new HistoryEvent(Now.AddDays(-40), "MigraineActivated", false, null),  // outside both
        };

        var summary = HistorySummarizer.Summarize(events, Now);

        Assert.Equal(3, summary.TotalActivations);
        Assert.Equal(1, summary.ActivationsLast7Days);
        Assert.Equal(1, summary.ActivationsPrevious7Days);
        Assert.Equal(2, summary.ActivationsLast30Days);
    }

    [Fact]
    public void SeparatesCurrentWeekFromPreviousWeek()
    {
        var events = new[]
        {
            new HistoryEvent(Now.AddDays(-2), "MigraineActivated", false, null),
            new HistoryEvent(Now.AddDays(-8), "MigraineActivated", false, null),
            new HistoryEvent(Now.AddDays(-14), "MigraineActivated", false, null),
            new HistoryEvent(Now.AddDays(-15), "MigraineActivated", false, null),
        };

        var summary = HistorySummarizer.Summarize(events, Now);

        Assert.Equal(1, summary.ActivationsLast7Days);
        Assert.Equal(2, summary.ActivationsPrevious7Days);
    }

    [Fact]
    public void SeparatesMildFromFullActivations()
    {
        var events = new[]
        {
            new HistoryEvent(Now, "MigraineActivated", true, null),
            new HistoryEvent(Now, "MigraineActivated", true, null),
            new HistoryEvent(Now, "MigraineActivated", false, null),
        };

        var summary = HistorySummarizer.Summarize(events, Now);

        Assert.Equal(2, summary.MildCount);
        Assert.Equal(1, summary.FullCount);
    }

    [Fact]
    public void AverageDurationOnlyCountsDeactivationEventsWithADuration()
    {
        var events = new[]
        {
            new HistoryEvent(Now, "MigraineDeactivated", false, 60),
            new HistoryEvent(Now, "MigraineDeactivated", false, 120),
            new HistoryEvent(Now, "MigraineDeactivated", false, null), // no duration recorded — excluded
        };

        var summary = HistorySummarizer.Summarize(events, Now);

        Assert.NotNull(summary.AverageDurationMinutes);
        Assert.Equal(1.5, summary.AverageDurationMinutes!.Value, precision: 5); // (60+120)/2 seconds = 90s = 1.5 min
    }

    [Fact]
    public void CountsPauseEventsSeparatelyFromActivations()
    {
        var events = new[]
        {
            new HistoryEvent(Now, "SchedulePaused", null, 1800),
            new HistoryEvent(Now, "SchedulePaused", null, 3600),
            new HistoryEvent(Now, "MigraineActivated", false, null),
        };

        var summary = HistorySummarizer.Summarize(events, Now);

        Assert.Equal(2, summary.PauseCount);
        Assert.Equal(1, summary.TotalActivations);
    }

    [Fact]
    public void FutureTimestampIsNotCountedAsWithinTheTrailingWindow()
    {
        // Defensive: a clock skew or malformed entry landing in the future shouldn't count as
        // "recent" via a negative-days match.
        var events = new[] { new HistoryEvent(Now.AddDays(1), "MigraineActivated", false, null) };

        var summary = HistorySummarizer.Summarize(events, Now);

        Assert.Equal(0, summary.ActivationsLast7Days);
        Assert.Equal(0, summary.ActivationsLast30Days);
    }
}
