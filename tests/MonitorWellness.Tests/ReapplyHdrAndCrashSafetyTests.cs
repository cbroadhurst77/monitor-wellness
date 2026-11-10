using Microsoft.Win32;
using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class ReapplyHdrAndCrashSafetyTests
{
    // ---- crash safety -----------------------------------------------------------------------

    [Fact]
    public void RunBestEffort_RunsEveryStep_EvenWhenAnEarlierOneThrows()
    {
        var ran = new List<string>();

        int failures = CrashSafety.RunBestEffort(new (string, Action)[]
        {
            ("first", () => ran.Add("first")),
            ("broken", () => throw new InvalidOperationException("boom")),
            ("last", () => ran.Add("last")),
        });

        Assert.Equal(1, failures);
        string[] expectedRuns = { "first", "last" };
        Assert.Equal(expectedRuns, ran);
    }

    [Fact]
    public void RunBestEffort_WithNothingToDo_ReportsNoFailures()
        => Assert.Equal(0, CrashSafety.RunBestEffort(Array.Empty<(string, Action)>()));

    // ---- when to reapply --------------------------------------------------------------------

    [Theory]
    [InlineData(SessionSwitchReason.SessionUnlock, true)]
    [InlineData(SessionSwitchReason.ConsoleConnect, true)]
    [InlineData(SessionSwitchReason.RemoteConnect, true)]
    [InlineData(SessionSwitchReason.SessionLogon, true)]
    [InlineData(SessionSwitchReason.SessionLock, false)]
    [InlineData(SessionSwitchReason.SessionLogoff, false)]
    [InlineData(SessionSwitchReason.ConsoleDisconnect, false)]
    public void SessionChanges_ThatCanResetTheScreen_TriggerAReapply(SessionSwitchReason reason, bool expected)
        => Assert.Equal(expected, CrashSafety.ShouldReapplyAfterSessionChange(reason));

    // ---- HDR per display --------------------------------------------------------------------

    [Fact]
    public void AnHdrDisplay_IsRecommendedOverlayOnly_WithAnHdrReason()
    {
        var hdr = new MonitorInfo(@"\\.\DISPLAY2", "Philips 328E1CA", false, @"\\?\DISPLAY#PHL#1", IsHdr: true);

        Assert.True(DisplayCompatibilityAdvisor.TryGetOverlayOnlyReason(hdr, out string reason));
        Assert.Contains("HDR", reason);
    }

    [Fact]
    public void AnOrdinaryDisplay_IsNotAffected_AndIsHdrDefaultsToFalse()
    {
        var normal = new MonitorInfo(@"\\.\DISPLAY1", "Philips 328E1CA", true, @"\\?\DISPLAY#PHL#1");

        Assert.False(normal.IsHdr);
        Assert.False(DisplayCompatibilityAdvisor.TryGetOverlayOnlyReason(normal, out _));
    }

    [Fact]
    public void TheHdrFlag_SurvivesTheAmbiguousIdentityCleanUp()
    {
        var monitors = new[]
        {
            new MonitorInfo(@"\\.\DISPLAY1", "A", true, "SAME", IsHdr: true),
            new MonitorInfo(@"\\.\DISPLAY2", "B", false, "same"),
        };

        List<MonitorInfo> cleaned = MonitorEnumerator.RemoveAmbiguousHardwareIdentities(monitors);

        Assert.True(cleaned[0].IsHdr);
        Assert.Equal("", cleaned[0].HardwareDeviceId);
    }

    [Fact]
    public void HdrDetection_IsConsistentBetweenTheTwoEntryPoints_AndNeverThrows()
    {
        // Whatever this machine's displays are doing, the boolean and the per-display set must agree.
        var targets = HdrDetector.GetHdrEnabledTargets();

        Assert.Equal(targets.Count > 0, HdrDetector.IsAnyDisplayHdrEnabled());
        Assert.NotNull(MonitorEnumerator.GetActiveMonitors(forceRefresh: true));
    }
}
