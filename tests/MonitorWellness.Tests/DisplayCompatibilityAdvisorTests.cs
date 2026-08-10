using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class DisplayCompatibilityAdvisorTests
{
    [Theory]
    [InlineData("Microsoft Remote Display Adapter", "")]
    [InlineData("Generic PnP Monitor", "MONITOR\\DisplayLink\\UID42")]
    [InlineData("Virtual Display", "")]
    public void TryGetOverlayOnlyReason_RecognizesExplicitCompatibilityPaths(string deviceString, string hardwareId)
    {
        var monitor = new MonitorInfo(@"\\.\DISPLAY1", deviceString, true, hardwareId);

        Assert.True(DisplayCompatibilityAdvisor.TryGetOverlayOnlyReason(monitor, out string reason));
        Assert.NotEmpty(reason);
    }

    [Fact]
    public void TryGetOverlayOnlyReason_DoesNotGuessForOrdinaryMonitor()
    {
        var monitor = new MonitorInfo(@"\\.\DISPLAY1", "Philips 328E1CA", true, "MONITOR\\PHL093F\\UID123");

        Assert.False(DisplayCompatibilityAdvisor.TryGetOverlayOnlyReason(monitor, out string reason));
        Assert.Empty(reason);
    }
}
