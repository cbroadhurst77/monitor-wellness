using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class MemoryTrimTests
{
    [Fact]
    public void TheFirstTrimWaitsForStartupToSettle_AndLaterTrimsAreSpacedOut()
    {
        Assert.True(MemoryTrimPolicy.FirstTrimDelay >= TimeSpan.FromSeconds(10));   // after the overlay windows are prepared in idle time
        Assert.True(MemoryTrimPolicy.Interval > MemoryTrimPolicy.FirstTrimDelay);
        Assert.True(MemoryTrimPolicy.Interval >= TimeSpan.FromMinutes(1));          // never a busy loop
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]   // the settings window is in use
    public void NoTrimWhileTheSettingsWindowIsOpen(bool settingsOpen, bool expected)
        => Assert.Equal(expected, MemoryTrimPolicy.ShouldTrimNow(settingsOpen));

    [Fact]
    public void Trim_ReturnsASaneResult_AndNeverThrows()
    {
        // Use some memory so there is something to give back.
        var ballast = new byte[40 * 1024 * 1024];
        for (int i = 0; i < ballast.Length; i += 4096)
            ballast[i] = 1;

        MemoryTrimmer.TrimResult result = MemoryTrimmer.Trim();

        Assert.True(result.Trimmed);
        Assert.True(result.BeforeBytes > 0);
        Assert.True(result.AfterBytes > 0);
        Assert.True(result.AfterBytes <= result.BeforeBytes + 16 * 1024 * 1024, "trimming must not grow the working set"); // allow for background activity between the two readings
        GC.KeepAlive(ballast);
    }

    [Fact]
    public void ReleasedBytes_IsNeverNegative()
        => Assert.Equal(0, new MemoryTrimmer.TrimResult(true, 100, 150).ReleasedBytes);

    [Fact]
    public void ReleasedBytes_IsTheDifference()
        => Assert.Equal(60, new MemoryTrimmer.TrimResult(true, 200, 140).ReleasedBytes);
}
