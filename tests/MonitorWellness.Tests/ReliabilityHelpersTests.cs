using System.IO;
using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class ReliabilityHelpersTests
{
    // ---- FailureThrottle ---------------------------------------------------------------

    [Fact]
    public void FailureThrottle_BacksOffAfterAFailure_AndGrowsTheWait()
    {
        var throttle = new FailureThrottle();
        var t0 = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

        Assert.True(throttle.ShouldAttempt(t0));

        throttle.RecordFailure(t0);
        Assert.False(throttle.ShouldAttempt(t0.AddSeconds(59)));
        Assert.True(throttle.ShouldAttempt(t0.AddMinutes(1)));

        throttle.RecordFailure(t0.AddMinutes(1));
        Assert.False(throttle.ShouldAttempt(t0.AddMinutes(1).AddSeconds(119)));
        Assert.True(throttle.ShouldAttempt(t0.AddMinutes(3)));
    }

    [Fact]
    public void FailureThrottle_NeverWaitsLongerThanTheLastStep()
    {
        var throttle = new FailureThrottle();
        var now = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 50; i++)
            throttle.RecordFailure(now);

        Assert.False(throttle.ShouldAttempt(now.AddMinutes(14)));
        Assert.True(throttle.ShouldAttempt(now.AddMinutes(15)));
    }

    [Fact]
    public void FailureThrottle_LogsTheFirstThreeFailuresThenEveryTwentieth_SoItCannotFloodTheLog()
    {
        var throttle = new FailureThrottle();
        var now = DateTime.UtcNow;

        var logged = Enumerable.Range(1, 100).Where(_ => throttle.RecordFailure(now)).ToList();

        Assert.Equal(1 + 1 + 1 + 5, logged.Count); // failures 1, 2, 3, then 20, 40, 60, 80, 100
        Assert.True(logged.Count < 10);            // versus 100 lines before
    }

    [Fact]
    public void FailureThrottle_RecordSuccess_ReportsWhatItEnded_AndResets()
    {
        var throttle = new FailureThrottle();
        var now = DateTime.UtcNow;
        throttle.RecordFailure(now);
        throttle.RecordFailure(now);

        Assert.Equal(2, throttle.RecordSuccess());

        Assert.Equal(0, throttle.ConsecutiveFailures);
        Assert.True(throttle.ShouldAttempt(now));
        Assert.Equal(0, throttle.RecordSuccess());
    }

    // ---- gamma rejection wording ---------------------------------------------------------

    [Theory]
    [InlineData(5400)]
    [InlineData(5000)] // the two values the real log blamed on "the hardware's safe floor"
    [InlineData(6500)]
    public void GammaRejection_ForASafeValue_IsDescribedAsTransient_NotAsAFloorProblem(int kelvin)
    {
        string text = GammaDiagnostics.DescribeRejection(kelvin, 5);

        Assert.Contains("transient", text);
        Assert.Contains("Win32 error 5", text);
        Assert.DoesNotContain("below the safe range", text);
    }

    [Fact]
    public void GammaRejection_ForAnUnsafeValue_StillSaysItIsBelowTheFloor()
    {
        string text = GammaDiagnostics.DescribeRejection(2500, 0);

        Assert.Contains("below the safe range", text);
        Assert.Contains("no Win32 error reported", text);
    }

    // ---- log location -------------------------------------------------------------------

    [Fact]
    public void LogPath_DefaultsToAppData_AndHonoursAnOverrideFolder()
    {
        Assert.Equal(Path.Combine(@"C:\Users\x\AppData\Roaming", "MonitorWellness", "debug.log"),
            DebugLog.ResolveLogPath(null, @"C:\Users\x\AppData\Roaming"));
        Assert.Equal(Path.Combine(@"C:\Users\x\AppData\Roaming", "MonitorWellness", "debug.log"),
            DebugLog.ResolveLogPath("  ", @"C:\Users\x\AppData\Roaming"));
        Assert.Equal(Path.Combine(@"D:\logs", "debug.log"),
            DebugLog.ResolveLogPath(@"D:\logs", @"C:\Users\x\AppData\Roaming"));
    }

    [Fact]
    public void TheTestRun_WritesItsLogToATempFolder_NotTheRealAppDataLog()
    {
        string realLog = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MonitorWellness", "debug.log");

        Assert.False(string.Equals(realLog, DebugLog.FilePath, StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith(Path.GetTempPath().TrimEnd('\\'), DebugLog.FilePath, StringComparison.OrdinalIgnoreCase);
    }

    // ---- auto-start check ---------------------------------------------------------------

    [Theory]
    [InlineData(true, true, 0, AutoStartCheck.Registered)]
    [InlineData(true, true, 1, AutoStartCheck.NotRegistered)]   // it ran, and the task isn't there
    [InlineData(true, false, -1, AutoStartCheck.Unknown)]       // timed out: says nothing about the task
    [InlineData(false, false, -1, AutoStartCheck.Unknown)]      // could not even start schtasks
    public void SchtasksQuery_OnlyConcludesNotRegistered_WhenItActuallyFinishedAndSaidSo(bool started, bool exited, int exitCode, AutoStartCheck expected)
        => Assert.Equal(expected, AutoStartManager.ClassifySchtasksQuery(new AutoStartManager.SchtasksResult(started, exited, exitCode)));

    // ---- Task Scheduler query -----------------------------------------------------------

    [Theory]
    [InlineData(1, "Disabled")]
    [InlineData(3, "Ready")]
    [InlineData(4, "Running")]
    [InlineData(99, "Unknown")]
    public void TaskState_IsDescribedInWords(int state, string expected)
        => Assert.Equal(expected, TaskSchedulerQuery.StateName(state));

    [Fact]
    public void ComZeroDate_MeansNever()
    {
        Assert.Null(TaskSchedulerQuery.NormalizeComDate(new DateTime(1899, 12, 30)));
        Assert.Equal(new DateTime(2026, 11, 10, 8, 39, 0), TaskSchedulerQuery.NormalizeComDate(new DateTime(2026, 11, 10, 8, 39, 0)));
    }

    [Fact]
    public void AskingForATaskThatDoesNotExist_IsNeverReportedAsFound()
    {
        TaskLookup lookup = TaskSchedulerQuery.TryGet("MonitorWellness-NoSuchTask-" + Guid.NewGuid().ToString("N"), out TaskInfo? info);

        // NotFound on a normal machine; Unavailable where the Task Scheduler service can't be
        // asked. Either way it must not claim the task exists.
        Assert.NotEqual(TaskLookup.Found, lookup);
        Assert.Null(info);
    }
}
