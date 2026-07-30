namespace MonitorWellness.Core;

/// <summary>
/// Pure decision logic for FullscreenDetector, kept in its own file with no Win32/WinForms
/// dependency specifically so it can be included in the test project (which targets plain
/// net8.0, not net8.0-windows) — the same reasoning behind AutoStartManager's split between
/// its pure argument-building methods and its live Process.Start calls.
/// </summary>
public static class FullscreenHeuristic
{
    /// <summary>
    /// True only when the window has neither a caption nor a resizable border AND its bounds
    /// fully cover the given monitor bounds — a heuristic for "probably an exclusive-fullscreen
    /// surface," not a certainty. See FullscreenDetector's doc comment for why a general,
    /// reliable detector isn't possible here, and why an occasional false positive is an
    /// accepted cost.
    /// </summary>
    public static bool IsFullscreenHeuristic(
        int windowLeft, int windowTop, int windowRight, int windowBottom,
        int monitorLeft, int monitorTop, int monitorRight, int monitorBottom,
        bool hasCaptionOrBorder)
    {
        if (hasCaptionOrBorder)
            return false;

        return windowLeft <= monitorLeft && windowTop <= monitorTop
            && windowRight >= monitorRight && windowBottom >= monitorBottom;
    }
}
