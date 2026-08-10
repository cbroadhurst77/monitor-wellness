using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MonitorWellness.Core;

/// <summary>
/// A single borderless, click-through, always-on-top window covering exactly one monitor.
/// This is the mechanism for both brightness dimming and (for migraine mode) color tint
/// beyond what the gamma ramp can reach — see the Week 1 finding in IMPLEMENTATION.md for
/// why gamma ramp alone isn't enough.
///
/// Positioning is done in physical pixels via SetWindowPos rather than WPF's logical
/// Left/Top/Width/Height, because those are DPI-scaled per-monitor and easy to get wrong
/// across mixed-DPI multi-monitor setups. Click-through is enabled via the WS_EX_TRANSPARENT
/// extended window style, applied once the Win32 handle exists.
/// </summary>
public partial class OverlayWindow : Window
{
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        int previousStyle = SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        if (previousStyle == 0 && Marshal.GetLastWin32Error() != 0)
            DebugLog.Write($"OverlayWindow: couldn't enable click-through style (Win32Error={Marshal.GetLastWin32Error()})");
    }

    /// <summary>Positions and sizes this window to exactly cover the given physical-pixel bounds.</summary>
    public void PositionOver(System.Drawing.Rectangle bounds)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        SetWindowPos(hwnd, HWND_TOPMOST, bounds.X, bounds.Y, bounds.Width, bounds.Height, SWP_NOACTIVATE);
    }

    /// <summary>Sets the overlay's tint color and opacity (0.0 = invisible, 1.0 = fully opaque).</summary>
    public void SetTint(System.Windows.Media.Color color, double opacity)
    {
        TintLayer.Background = new SolidColorBrush(color) { Opacity = Math.Clamp(opacity, 0.0, 1.0) };

        // Win32 "topmost" is a one-time z-order placement, not a persistent window style --
        // PositionOver's HWND_TOPMOST call at window creation only put this window at the top
        // of the topmost band at that instant. Any later window that also requests topmost
        // placement (a volume/brightness OSD, a notification toast, another always-on-top
        // utility, a UAC prompt, Game Bar, etc. -- all common during normal use) can end up
        // stacked above it, silently pushing the dim overlay behind other content even though
        // SetTint's own values are still logically correct -- reported live as "the dimmer
        // seems to disappear after a while." Reasserting HWND_TOPMOST on every tint update
        // (already called on every ~30s schedule tick, migraine fade tick, and Settings live
        // preview) self-heals this within one tick instead of only fixing it when Settings
        // happens to be reopened. NOMOVE/NOSIZE make this a cheap re-stack, not a resize.
        ReassertTopmost();
    }

    /// <summary>Moves this click-through overlay back to the top of the topmost window band.</summary>
    public void ReassertTopmost()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE);
    }

    /// <summary>
    /// Shows a large centered label over a dark backdrop, for visually identifying which
    /// physical monitor corresponds to which internal device name — Windows' own on-screen
    /// display numbers don't reliably match device name enumeration order, so this is the
    /// only trustworthy way to correlate the two.
    /// </summary>
    public void ShowLabel(string text)
    {
        TintLayer.Background = new SolidColorBrush(Colors.Black) { Opacity = 0.55 };
        LabelText.Text = text;
        LabelText.Visibility = Visibility.Visible;
    }

    public void HideLabel()
    {
        LabelText.Visibility = Visibility.Collapsed;
    }
}
