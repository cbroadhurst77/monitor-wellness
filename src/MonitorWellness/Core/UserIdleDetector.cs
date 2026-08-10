using System.Runtime.InteropServices;

namespace MonitorWellness.Core;

/// <summary>
/// Reads the Windows last-input timestamp for reminder suppression only. Failure deliberately
/// returns zero rather than changing screen behaviour: this convenience feature must never
/// become a reason the primary comfort controls fail.
/// </summary>
public static class UserIdleDetector
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetLastInputInfo(ref LastInputInfo lastInputInfo);

    public static TimeSpan GetIdleDuration()
    {
        try
        {
            var lastInputInfo = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
            if (!GetLastInputInfo(ref lastInputInfo))
                return TimeSpan.Zero;

            uint elapsedMilliseconds = unchecked((uint)Environment.TickCount - lastInputInfo.Time);
            return TimeSpan.FromMilliseconds(elapsedMilliseconds);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or SEHException)
        {
            DebugLog.Write($"UserIdleDetector.GetIdleDuration unavailable: {ex.Message}");
            return TimeSpan.Zero;
        }
    }
}
