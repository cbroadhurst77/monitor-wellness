namespace MonitorWellness.Core;

/// <summary>
/// When a display needs its dim/tint overlay window on screen at all. In full daytime the
/// schedule's dim amount is zero, and a fully transparent topmost window above every monitor does
/// nothing except cost memory and make Windows compose it over whatever is underneath (a
/// full-screen game or video included). So the window is hidden whenever there is nothing to show.
/// A small gap between the show and hide thresholds keeps it from flapping when the amount hovers
/// near zero (for example ambient-light adjustments in the daytime). Pure and testable.
/// </summary>
public static class OverlayVisibilityPolicy
{
    /// <summary>Dim amounts below this (0.4% darkening) are invisible; don't show an overlay for them.</summary>
    public const double ShowAtOrAbove = 0.004;

    /// <summary>Once shown, the overlay stays until the amount falls below this.</summary>
    public const double HideBelow = 0.002;

    public static bool NeedsOverlay(double opacity, bool currentlyShown)
        => currentlyShown ? opacity >= HideBelow : opacity >= ShowAtOrAbove;
}
