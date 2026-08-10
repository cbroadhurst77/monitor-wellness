using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class DisplayCapabilityReporterTests
{
    [Fact]
    public void CreateDisplayCapability_UsesOverlayWhenIdentityIsAmbiguous()
    {
        var monitor = new MonitorInfo(@"\\.\DISPLAY1", "Example", true, "");
        var capability = DisplayCapabilityReporter.CreateDisplayCapability(
            new AppSettings(),
            monitor,
            new Dictionary<string, DdcCiBrightnessCapability>
            {
                [monitor.DeviceName] = new DdcCiBrightnessCapability(monitor.DeviceName, true, true, "Available"),
            });

        Assert.Equal("Stable overlay fallback", capability.RecommendedBrightnessBackend);
        Assert.Equal("Overlay only", capability.SafetyStatus);
        Assert.Contains("ambiguous", capability.HardwareIdentity, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateDisplayCapability_ReportsVerifiedHardwareOnlyWhenApproved()
    {
        var monitor = new MonitorInfo(@"\\.\DISPLAY1", "Example", true, "MONITOR\\ACME123\\UID42");
        var settings = new AppSettings();
        HardwareBrightnessSafety.MarkVerified(settings, monitor);

        var capability = DisplayCapabilityReporter.CreateDisplayCapability(
            settings,
            monitor,
            new Dictionary<string, DdcCiBrightnessCapability>
            {
                [monitor.DeviceName] = new DdcCiBrightnessCapability(monitor.DeviceName, true, true, "Available"),
            });

        Assert.Equal("Verified hardware brightness", capability.RecommendedBrightnessBackend);
        Assert.Equal("Hardware brightness approved", capability.SafetyStatus);
    }
}
