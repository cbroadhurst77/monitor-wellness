using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class ResetRepairThrottleTests
{
    private static readonly DateTime T0 = new(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void OccasionalRepairsNeverTripIt()
    {
        var t = new ResetRepairThrottle();
        Assert.False(t.RecordRepairs(1, T0));
        Assert.False(t.RecordRepairs(1, T0.AddMinutes(2)));
        Assert.False(t.RecordRepairs(1, T0.AddMinutes(4)));
        Assert.True(t.IsAllowed(T0.AddMinutes(4)));
    }

    [Fact]
    public void RepeatedRepairsInAMinute_StandItDownForAWhile_ThenItResumes()
    {
        var t = new ResetRepairThrottle();
        Assert.False(t.RecordRepairs(1, T0));
        Assert.False(t.RecordRepairs(1, T0.AddSeconds(10)));
        Assert.True(t.RecordRepairs(1, T0.AddSeconds(20)));
        Assert.False(t.IsAllowed(T0.AddMinutes(5)));
        Assert.True(t.IsAllowed(T0.AddSeconds(20) + ResetRepairThrottle.StandDown));
    }

    [Fact]
    public void SeveralDisplaysRepairedAtOnce_CountIndividually()
    {
        var t = new ResetRepairThrottle();
        Assert.True(t.RecordRepairs(3, T0));
    }
}
