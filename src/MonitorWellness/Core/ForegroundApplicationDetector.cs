using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MonitorWellness.Core;

/// <summary>Best-effort foreground process lookup. Failure is always treated as no matching app.</summary>
public static class ForegroundApplicationDetector
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    public static string? TryGetForegroundProcessName()
    {
        try
        {
            IntPtr window = GetForegroundWindow();
            if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out uint processId) == 0 || processId == 0)
                return null;

            using Process process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            DebugLog.Write($"Foreground application lookup failed: {ex.Message}");
            return null;
        }
    }
}
