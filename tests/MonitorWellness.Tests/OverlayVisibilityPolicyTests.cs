using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class OverlayVisibilityPolicyTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.001)]
    [InlineData(0.0039)]
    public void NothingIsShown_WhenThereIsNoVisibleDimming(double opacity)
        => Assert.False(OverlayVisibilityPolicy.NeedsOverlay(opacity, currentlyShown: false));

    [Theory]
    [InlineData(0.004)]
    [InlineData(0.15)]
    [InlineData(0.72)]   // migraine mode
    [InlineData(1.0)]
    public void AVisibleAmountOfDimming_NeedsTheOverlay(double opacity)
        => Assert.True(OverlayVisibilityPolicy.NeedsOverlay(opacity, currentlyShown: false));

    [Fact]
    public void OnceShown_TheOverlayStaysThroughSmallDips_ButGoesWhenTheAmountIsGone()
    {
        Assert.True(OverlayVisibilityPolicy.NeedsOverlay(0.003, currentlyShown: true));   // inside the gap: keep showing
        Assert.False(OverlayVisibilityPolicy.NeedsOverlay(0.003, currentlyShown: false)); // ...but don't start showing there
        Assert.False(OverlayVisibilityPolicy.NeedsOverlay(0.0019, currentlyShown: true));
        Assert.False(OverlayVisibilityPolicy.NeedsOverlay(0.0, currentlyShown: true));
    }

    [Fact]
    public void AnAmountHoveringNearZero_DoesNotFlap()
    {
        // Ambient-light adjustments can make the daytime dim amount wobble around the threshold.
        bool shown = false;
        int changes = 0;
        foreach (double opacity in new[] { 0.0035, 0.0045, 0.0030, 0.0042, 0.0025, 0.0041, 0.0021, 0.0030 })
        {
            bool needed = OverlayVisibilityPolicy.NeedsOverlay(opacity, shown);
            if (needed != shown)
                changes++;
            shown = needed;
        }

        Assert.Equal(1, changes); // came on once (at 0.0045) and stayed on through every dip above the hide threshold
    }

    [Fact]
    public void TheThresholds_AreOrdered_SoThereIsAGapAndNotAnOverlap()
        => Assert.True(OverlayVisibilityPolicy.HideBelow < OverlayVisibilityPolicy.ShowAtOrAbove);
}
