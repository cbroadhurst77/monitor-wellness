using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class LogOnChangeTests
{
    private static readonly DateTime T0 = new(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FirstEventIsLogged_AndAnUnchangedRepeatIsNot()
    {
        var log = new LogOnChange(TimeSpan.FromHours(6));
        Assert.True(log.ShouldLog("show", T0));
        Assert.False(log.ShouldLog("show", T0.AddMinutes(30)));
        Assert.False(log.ShouldLog("show", T0.AddHours(5)));
    }

    [Fact]
    public void AChangeIsLoggedImmediately()
    {
        var log = new LogOnChange(TimeSpan.FromHours(6));
        log.ShouldLog("show", T0);
        Assert.True(log.ShouldLog("skip:fullscreen", T0.AddMinutes(30)));
        Assert.True(log.ShouldLog("show", T0.AddMinutes(60)));
    }

    [Fact]
    public void ARepeatIsLoggedAgainOnceTheHeartbeatElapses()
    {
        var log = new LogOnChange(TimeSpan.FromHours(6));
        log.ShouldLog("show", T0);
        Assert.True(log.ShouldLog("show", T0.AddHours(6)));
        Assert.False(log.ShouldLog("show", T0.AddHours(7)));
    }
}
