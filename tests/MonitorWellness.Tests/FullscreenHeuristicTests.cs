using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class FullscreenHeuristicTests
{
    private const int MonitorLeft = 0, MonitorTop = 0, MonitorRight = 1920, MonitorBottom = 1080;

    [Fact]
    public void BorderlessWindowExactlyCoveringMonitor_IsFullscreen()
    {
        bool result = FullscreenHeuristic.IsFullscreenHeuristic(
            0, 0, 1920, 1080,
            MonitorLeft, MonitorTop, MonitorRight, MonitorBottom,
            hasCaptionOrBorder: false);

        Assert.True(result);
    }

    [Fact]
    public void BorderlessWindowCoveringMultipleMonitorsSpan_IsFullscreen()
    {
        // A window larger than the monitor (e.g. spanning into another monitor) still counts —
        // the check is "at least covers this monitor," not "exactly equals it."
        bool result = FullscreenHeuristic.IsFullscreenHeuristic(
            -100, -50, 2000, 1200,
            MonitorLeft, MonitorTop, MonitorRight, MonitorBottom,
            hasCaptionOrBorder: false);

        Assert.True(result);
    }

    [Fact]
    public void WindowWithCaption_IsNeverFullscreenRegardlessOfBounds()
    {
        bool result = FullscreenHeuristic.IsFullscreenHeuristic(
            0, 0, 1920, 1080,
            MonitorLeft, MonitorTop, MonitorRight, MonitorBottom,
            hasCaptionOrBorder: true);

        Assert.False(result);
    }

    [Theory]
    [InlineData(100, 0, 1920, 1080)]   // doesn't reach the left edge
    [InlineData(0, 100, 1920, 1080)]   // doesn't reach the top edge
    [InlineData(0, 0, 1800, 1080)]     // doesn't reach the right edge
    [InlineData(0, 0, 1920, 1000)]     // doesn't reach the bottom edge
    public void PartiallyCoveringWindow_IsNotFullscreen(int left, int top, int right, int bottom)
    {
        bool result = FullscreenHeuristic.IsFullscreenHeuristic(
            left, top, right, bottom,
            MonitorLeft, MonitorTop, MonitorRight, MonitorBottom,
            hasCaptionOrBorder: false);

        Assert.False(result);
    }
}
