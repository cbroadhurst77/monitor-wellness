using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MonitorWellness.Core;

/// <summary>
/// Best-effort heuristic for "the foreground window is probably covering the whole screen with
/// no border" — see TECHNICAL_UX_REVIEW.md §1.3. A true exclusive-fullscreen surface (an older
/// game or video player using D3D exclusive fullscreen, not borderless-windowed) bypasses the
/// desktop compositor and topmost windows entirely, so OverlayWindow's tint/dim can silently
/// fail to render right when migraine mode matters most, with nothing distinguishing that from
/// "working correctly." There is no fully reliable, general way to detect true exclusive
/// fullscreen from outside the app doing it — this is a heuristic (borderless + covers the
/// entire monitor, see FullscreenHeuristic), not a certainty, and can also fire for a harmless
/// borderless-windowed fullscreen app where the overlay actually works fine. That's an accepted
/// false-positive cost for an occasional, dismissable warning rather than a silent failure with
/// no explanation at all.
/// </summary>
public static class FullscreenDetector
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    private const int GWL_STYLE = -16;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>
    /// Live check against the current foreground window. Any failure (no foreground window,
    /// API error) is treated as "not fullscreen" — this is purely advisory and must never be a
    /// reason migraine mode's activation itself fails or behaves differently.
    /// </summary>
    public static bool IsForegroundWindowLikelyFullscreen()
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out RECT windowRect))
                return false;

            int style = GetWindowLong(hwnd, GWL_STYLE);
            bool hasCaptionOrBorder = (style & (WS_CAPTION | WS_THICKFRAME)) != 0;

            var screen = Screen.FromHandle(hwnd);
            var bounds = screen.Bounds;

            return FullscreenHeuristic.IsFullscreenHeuristic(
                windowRect.Left, windowRect.Top, windowRect.Right, windowRect.Bottom,
                bounds.Left, bounds.Top, bounds.Right, bounds.Bottom,
                hasCaptionOrBorder);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            DebugLog.Write($"FullscreenDetector.IsForegroundWindowLikelyFullscreen check failed: {ex.Message}");
            return false;
        }
    }
}
