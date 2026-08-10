using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class HardwareBrightnessSafetyTests
{
    [Fact]
    public void VerifiedPhysicalMonitor_RemainsApprovedWhenWindowsDisplayNameChanges()
    {
        var settings = new AppSettings();
        var firstConnection = new MonitorInfo(@"\\.\DISPLAY1", "Example monitor", false, "MONITOR\\ACME123\\5&ABC&0&UID42");
        var reconnected = firstConnection with { DeviceName = @"\\.\DISPLAY3" };

        Assert.True(HardwareBrightnessSafety.MarkVerified(settings, firstConnection));

        Assert.True(HardwareBrightnessSafety.IsApproved(settings, reconnected));
    }

    [Fact]
    public void QuarantinedMonitor_IsNotApprovedUntilItIsRetested()
    {
        var settings = new AppSettings();
        var monitor = new MonitorInfo(@"\\.\DISPLAY1", "Example monitor", false, "MONITOR\\ACME123\\5&ABC&0&UID42");
        HardwareBrightnessSafety.MarkVerified(settings, monitor);

        Assert.True(HardwareBrightnessSafety.Quarantine(settings, monitor, "The monitor stopped responding."));

        Assert.False(HardwareBrightnessSafety.IsApproved(settings, monitor));
        Assert.True(HardwareBrightnessSafety.IsQuarantined(settings, monitor, out string reason));
        Assert.Contains("stopped responding", reason);
    }

    [Fact]
    public void MonitorWithoutHardwareIdentity_FailsClosed()
    {
        var settings = new AppSettings { HardwareBrightnessEnabledMonitors = new List<string> { @"\\.\DISPLAY1" } };
        var monitor = new MonitorInfo(@"\\.\DISPLAY1", "Example monitor", false, "");

        Assert.False(HardwareBrightnessSafety.IsApproved(settings, monitor));
        Assert.False(HardwareBrightnessSafety.MarkVerified(settings, monitor));
    }

    [Fact]
    public void AmbiguousHardwareIdentity_IsRemovedForEveryMatchingDisplay()
    {
        var monitors = new[]
        {
            new MonitorInfo(@"\\.\DISPLAY1", "First", true, "MONITOR\\DUPLICATE"),
            new MonitorInfo(@"\\.\DISPLAY2", "Second", false, "monitor\\duplicate"),
            new MonitorInfo(@"\\.\DISPLAY3", "Third", false, "MONITOR\\UNIQUE"),
        };

        List<MonitorInfo> filtered = MonitorEnumerator.RemoveAmbiguousHardwareIdentities(monitors);

        Assert.Equal(string.Empty, filtered[0].HardwareDeviceId);
        Assert.Equal(string.Empty, filtered[1].HardwareDeviceId);
        Assert.Equal("MONITOR\\UNIQUE", filtered[2].HardwareDeviceId);
    }
}
