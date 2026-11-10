using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class SchedulePauseTests
{
    [Fact]
    public void ComputeUntilTomorrowLocal_FromAfternoon_ResolvesTo8amNextDay()
    {
        var now = new DateTime(2026, 7, 30, 14, 30, 0);
        var result = SchedulePause.ComputeUntilTomorrowLocal(now);
        Assert.Equal(new DateTime(2026, 7, 31, 8, 0, 0), result);
    }

    [Fact]
    public void ComputeUntilTomorrowLocal_FromEarlyMorning_StillResolvesToNextDay()
    {
        // Even if "now" is before 8am today, "until tomorrow" always means the *next*
        // calendar day's 8am, not today's -- simpler and more predictable than trying to
        // special-case "is it already past 8am today."
        var now = new DateTime(2026, 7, 30, 5, 0, 0);
        var result = SchedulePause.ComputeUntilTomorrowLocal(now);
        Assert.Equal(new DateTime(2026, 7, 31, 8, 0, 0), result);
    }

    [Fact]
    public void ComputeUntilTomorrowLocal_HandlesMonthBoundary()
    {
        var now = new DateTime(2026, 7, 31, 20, 0, 0);
        var result = SchedulePause.ComputeUntilTomorrowLocal(now);
        Assert.Equal(new DateTime(2026, 8, 1, 8, 0, 0), result);
    }

    [Fact]
    public void IsExpired_IsFalseWhenNoPauseIsActive()
        => Assert.False(SchedulePause.IsExpired(null, DateTime.UtcNow));

    [Fact]
    public void IsExpired_IsJudgedByTheClock_SoAPauseThatEndedDuringSleepIsDetected()
    {
        var until = new DateTime(2026, 11, 11, 8, 0, 0, DateTimeKind.Utc);

        Assert.False(SchedulePause.IsExpired(until, until.AddSeconds(-1)));
        Assert.True(SchedulePause.IsExpired(until, until));
        Assert.True(SchedulePause.IsExpired(until, until.AddHours(9))); // laptop woke the next afternoon
    }
}
