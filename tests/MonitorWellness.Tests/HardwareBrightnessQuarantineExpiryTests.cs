using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class HardwareBrightnessQuarantineExpiryTests
{
    private static readonly MonitorInfo Monitor = new(@"\\.\DISPLAY1", "Example monitor", false, "MONITOR\\ACME123\\5&ABC&0&UID42");

    private static (AppSettings Settings, DateTime FailedAt) ApprovedThenFailed()
    {
        var settings = new AppSettings();
        HardwareBrightnessSafety.MarkVerified(settings, Monitor);
        HardwareBrightnessSafety.Quarantine(settings, Monitor, "failed");
        var state = settings.HardwareBrightnessSafetyByMonitor.Values.Single();
        return (settings, state.QuarantinedUtc!.Value);
    }

    [Fact]
    public void ApprovedMonitor_IsNotRetriedBeforeItsBackoff_ButIsAfterIt()
    {
        var (settings, failedAt) = ApprovedThenFailed();

        Assert.False(HardwareBrightnessSafety.IsApproved(settings, Monitor, failedAt.AddMinutes(29)));
        Assert.True(HardwareBrightnessSafety.IsApproved(settings, Monitor, failedAt.AddMinutes(31)));
    }

    [Fact]
    public void MonitorThatWasNeverApproved_IsNeverRetriedAutomatically()
    {
        var settings = new AppSettings();
        HardwareBrightnessSafety.Quarantine(settings, Monitor, "failed during first test");
        var failedAt = settings.HardwareBrightnessSafetyByMonitor.Values.Single().QuarantinedUtc!.Value;

        Assert.False(HardwareBrightnessSafety.IsApproved(settings, Monitor, failedAt.AddDays(30)));
    }

    [Fact]
    public void RepeatedFailures_LengthenTheBackoff_ThenStopRetryingAfterTheBudget()
    {
        var (settings, _) = ApprovedThenFailed();

        // Failures 2 and 3 (each only happens because a retry was due).
        HardwareBrightnessSafety.Quarantine(settings, Monitor, "failed again");
        var state = settings.HardwareBrightnessSafetyByMonitor.Values.Single();
        Assert.Equal(2, state.FailureCount);
        Assert.True(state.ApprovedBeforeQuarantine);
        Assert.False(HardwareBrightnessSafety.IsApproved(settings, Monitor, state.QuarantinedUtc!.Value.AddMinutes(60)));
        Assert.True(HardwareBrightnessSafety.IsApproved(settings, Monitor, state.QuarantinedUtc!.Value.AddHours(2).AddMinutes(1)));

        HardwareBrightnessSafety.Quarantine(settings, Monitor, "and again");
        HardwareBrightnessSafety.Quarantine(settings, Monitor, "fourth");
        state = settings.HardwareBrightnessSafetyByMonitor.Values.Single();
        Assert.True(state.FailureCount > HardwareBrightnessSafety.MaximumAutomaticRetries);
        Assert.False(HardwareBrightnessSafety.IsApproved(settings, Monitor, state.QuarantinedUtc!.Value.AddDays(30)));
    }

    [Fact]
    public void ClearQuarantineAfterRecovery_RestoresApprovalAndResetsTheRetryBudget()
    {
        var (settings, failedAt) = ApprovedThenFailed();

        Assert.True(HardwareBrightnessSafety.ClearQuarantineAfterRecovery(settings, Monitor));

        Assert.True(HardwareBrightnessSafety.IsApproved(settings, Monitor, failedAt));
        Assert.False(HardwareBrightnessSafety.IsQuarantined(settings, Monitor, out _));
        var state = settings.HardwareBrightnessSafetyByMonitor.Values.Single();
        Assert.Equal(0, state.FailureCount);
        Assert.False(state.ApprovedBeforeQuarantine);
        Assert.False(HardwareBrightnessSafety.ClearQuarantineAfterRecovery(settings, Monitor)); // nothing left to clear
    }

    [Fact]
    public void QuarantineBookkeeping_SurvivesCloningAndValidation()
    {
        var (settings, _) = ApprovedThenFailed();

        var clone = settings.Clone();

        var state = clone.HardwareBrightnessSafetyByMonitor.Values.Single();
        Assert.True(state.ApprovedBeforeQuarantine);
        Assert.Equal(1, state.FailureCount);
        Assert.True(AppSettingsValidator.TryValidate(clone, out _));
    }
}
