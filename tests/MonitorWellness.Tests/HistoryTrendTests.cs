using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class HistoryTrendTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void BuildWeeks_returns_requested_number_oldest_first()
    {
        var weeks = HistoryTrend.BuildWeeks(Array.Empty<HistoryEvent>(), Now, 4);

        Assert.Equal(4, weeks.Count);
        Assert.True(weeks[0].StartUtc < weeks[3].StartUtc);
    }

    [Fact]
    public void BuildWeeks_assigns_activations_to_the_right_week_and_ignores_older()
    {
        var events = new[]
        {
            new HistoryEvent(Now.AddDays(-1), "MigraineActivated", false, null),
            new HistoryEvent(Now.AddDays(-2), "MigraineActivated", true, null),
            new HistoryEvent(Now.AddDays(-9), "MigraineActivated", false, null),
            new HistoryEvent(Now.AddDays(-200), "MigraineActivated", false, null),
        };

        var weeks = HistoryTrend.BuildWeeks(events, Now, 2);

        Assert.Equal(1, weeks[0].Activations);
        Assert.Equal(2, weeks[1].Activations);
    }

    [Fact]
    public void BuildWeeks_boundary_event_lands_in_exactly_one_week()
    {
        var events = new[] { new HistoryEvent(Now.AddDays(-7), "MigraineActivated", false, null) };

        var weeks = HistoryTrend.BuildWeeks(events, Now, 2);

        Assert.Equal(1, weeks.Sum(w => w.Activations));
    }

    [Fact]
    public void Describe_empty_history_says_nothing_recorded()
    {
        string text = HistoryTrend.Describe(Array.Empty<HistoryEvent>(), Now);

        Assert.Contains("nothing recorded", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_sparse_ratings_are_not_averaged()
    {
        var events = new[]
        {
            new HistoryEvent(Now.AddDays(-1), "MigraineActivated", false, null),
            new HistoryEvent(Now.AddDays(-1), "MigraineRating", null, null, 5),
        };

        string text = HistoryTrend.Describe(events, Now);

        Assert.Contains("too few for an average", text, StringComparison.Ordinal);
        Assert.DoesNotContain("average helpfulness", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_enough_ratings_shows_average_and_sample_count()
    {
        var events = new[]
        {
            new HistoryEvent(Now.AddDays(-1), "MigraineRating", null, null, 4),
            new HistoryEvent(Now.AddDays(-2), "MigraineRating", null, null, 2),
            new HistoryEvent(Now.AddDays(-3), "MigraineRating", null, null, 3),
        };

        string text = HistoryTrend.Describe(events, Now);

        Assert.Contains("average helpfulness 3.0/5 from 3 ratings", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_always_disclaims_medical_meaning()
    {
        var events = new[] { new HistoryEvent(Now.AddDays(-1), "MigraineActivated", false, null) };

        string text = HistoryTrend.Describe(events, Now);

        Assert.Contains("not medical advice", text, StringComparison.Ordinal);
    }
}
