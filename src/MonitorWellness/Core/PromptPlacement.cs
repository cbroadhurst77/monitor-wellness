using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MonitorWellness.Core;

/// <summary>Where the break prompt goes: the corner of the monitor the person is looking at.</summary>
public static class PromptPlacement
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    /// <summary>
    /// Top-left (physical pixels) that puts a window of the given size in the bottom-right corner of the
    /// work area, <paramref name="margin"/> from the edges, and never off the left or top of it.
    /// </summary>
    public static Point BottomRight(Rectangle workArea, Size window, int margin)
        => new(
            Math.Max(workArea.Left, workArea.Right - window.Width - margin),
            Math.Max(workArea.Top, workArea.Bottom - window.Height - margin));

    /// <summary>
    /// The monitor with the window the person is working in, or the one under the mouse when the
    /// foreground window is this app's own or cannot be found, or the primary monitor.
    /// </summary>
    public static Screen ActiveScreen()
    {
        try
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground != IntPtr.Zero
                && GetWindowThreadProcessId(foreground, out uint processId) != 0
                && processId != (uint)Environment.ProcessId)
            {
                return Screen.FromHandle(foreground);
            }

            return Screen.FromPoint(Control.MousePosition);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            DebugLog.Write($"PromptPlacement: couldn't find the active monitor: {ex.Message}");
            return Screen.PrimaryScreen ?? Screen.AllScreens[0];
        }
    }
}
