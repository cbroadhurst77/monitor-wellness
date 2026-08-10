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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, [Out] char[] text, int maximumCount);

    public sealed record ForegroundApplicationInfo(string ProcessName, string WindowTitle);

    public static string? TryGetForegroundProcessName()
        => TryGetForegroundApplication()?.ProcessName;

    public static ForegroundApplicationInfo? TryGetForegroundApplication()
    {
        try
        {
            IntPtr window = GetForegroundWindow();
            if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out uint processId) == 0 || processId == 0)
                return null;

            using Process process = Process.GetProcessById((int)processId);
            var titleBuffer = new char[1024];
            _ = GetWindowText(window, titleBuffer, titleBuffer.Length);
            return new ForegroundApplicationInfo(process.ProcessName, new string(titleBuffer).TrimEnd('\0'));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            DebugLog.Write($"Foreground application lookup failed: {ex.Message}");
            return null;
        }
    }
}
