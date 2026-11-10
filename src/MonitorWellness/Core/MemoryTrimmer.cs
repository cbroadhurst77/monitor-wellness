using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MonitorWellness.Core;

/// <summary>When the app gives its idle memory back to Windows. Pure, so the schedule can be tested.</summary>
public static class MemoryTrimPolicy
{
    /// <summary>The first trim, after startup has settled (the hidden overlay windows are prepared in idle time just before it).</summary>
    public static readonly TimeSpan FirstTrimDelay = TimeSpan.FromSeconds(20);

    /// <summary>Then once in a while: a tray app sits idle almost all the time, and the pages it touched since the last trim are few.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    /// <summary>Not while the settings window is open: it is being used, and trimming would only make it page its own controls back in.</summary>
    public static bool ShouldTrimNow(bool settingsWindowOpen) => !settingsWindowOpen;
}

/// <summary>
/// Returns the app's idle working set to Windows. A WPF app that has just started holds on to far
/// more resident memory than it needs while idle (assemblies it only touched once, the first-show
/// work of the overlay windows, and, in the compressed single-file build, the unpacked assemblies).
/// Measured on a real run: Task Manager's "Memory" for the idle app dropped from 188 MB to 66 MB
/// after one trim and 46 MB half a minute later, because an idle tray app doesn't touch those
/// pages again. It changes only what is kept in RAM, never what the app does; anything it does
/// need comes back on demand.
/// </summary>
public static class MemoryTrimmer
{
    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr process);

    public readonly record struct TrimResult(bool Trimmed, long BeforeBytes, long AfterBytes)
    {
        public long ReleasedBytes => Math.Max(0, BeforeBytes - AfterBytes);
    }

    /// <summary>Trims this process's working set. Never throws: a failed trim just leaves things as they were.</summary>
    public static TrimResult Trim()
    {
        try
        {
            using Process process = Process.GetCurrentProcess();
            long before = process.WorkingSet64;
            bool trimmed = EmptyWorkingSet(process.Handle);
            process.Refresh();
            return new TrimResult(trimmed, before, process.WorkingSet64);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException)
        {
            DebugLog.Write($"MemoryTrimmer: couldn't trim the working set: {ex.Message}");
            return new TrimResult(false, 0, 0);
        }
    }
}
