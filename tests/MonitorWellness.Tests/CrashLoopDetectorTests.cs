using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class CrashLoopDetectorTests
{
    [Fact]
    public void SingleOccurrence_IsNotLooping()
    {
        var detector = new CrashLoopDetector(threshold: 5, window: TimeSpan.FromMinutes(1));
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        Assert.False(detector.RecordAndCheckIsLooping(now));
    }

    [Fact]
    public void ThresholdReachedWithinWindow_IsLooping()
    {
        var detector = new CrashLoopDetector(threshold: 3, window: TimeSpan.FromMinutes(1));
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        Assert.False(detector.RecordAndCheckIsLooping(start));
        Assert.False(detector.RecordAndCheckIsLooping(start.AddSeconds(10)));
        Assert.True(detector.RecordAndCheckIsLooping(start.AddSeconds(20)));
    }

    [Fact]
    public void OccurrencesSpreadOutsideWindow_NeverLoop()
    {
        var detector = new CrashLoopDetector(threshold: 3, window: TimeSpan.FromMinutes(1));
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        Assert.False(detector.RecordAndCheckIsLooping(start));
        Assert.False(detector.RecordAndCheckIsLooping(start.AddMinutes(2)));
        Assert.False(detector.RecordAndCheckIsLooping(start.AddMinutes(4)));
    }

    [Fact]
    public void OldOccurrencesAgeOutOfTheWindow()
    {
        var detector = new CrashLoopDetector(threshold: 3, window: TimeSpan.FromMinutes(1));
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        Assert.False(detector.RecordAndCheckIsLooping(start));
        Assert.False(detector.RecordAndCheckIsLooping(start.AddSeconds(10)));
        // This one lands more than a minute after the first two, so they should have aged out —
        // only 1 (this one) + none of the earlier two should remain in the window.
        Assert.False(detector.RecordAndCheckIsLooping(start.AddSeconds(80)));
    }

    [Fact]
    public void ExactlyAtThreshold_IsLooping()
    {
        var detector = new CrashLoopDetector(threshold: 2, window: TimeSpan.FromMinutes(1));
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        Assert.False(detector.RecordAndCheckIsLooping(start));
        Assert.True(detector.RecordAndCheckIsLooping(start.AddSeconds(1)));
    }
}
