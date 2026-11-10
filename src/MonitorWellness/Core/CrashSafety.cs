using Microsoft.Win32;

namespace MonitorWellness.Core;

/// <summary>
/// What to do about the display when the app is about to die from an unhandled exception, plus
/// the decision about which Windows session changes should make it reapply the screen.
/// </summary>
public static class CrashSafety
{
    /// <summary>
    /// Runs each step, in order, even if an earlier one throws, and returns how many failed. A
    /// process that is already terminating has nothing left to lose, so every attempt to put the
    /// screen back is worth making. Pure orchestration, testable with fake steps.
    /// </summary>
    public static int RunBestEffort(IEnumerable<(string Name, Action Step)> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        int failures = 0;
        foreach ((string name, Action step) in steps)
        {
            try
            {
                step();
            }
            catch (Exception ex)
            {
                failures++;
                DebugLog.Write($"CrashSafety: '{name}' failed while shutting down after an unhandled exception: {ex.GetType().Name}: {ex.Message}");
            }
        }
        return failures;
    }

    /// <summary>
    /// Whether a Windows session change should make the app reapply the display. Unlocking, or
    /// reconnecting a console or remote session, can leave the colour ramp reset or the device
    /// context stale; locking, logging off and the like change nothing on screen.
    /// </summary>
    public static bool ShouldReapplyAfterSessionChange(SessionSwitchReason reason)
        => reason is SessionSwitchReason.SessionUnlock
            or SessionSwitchReason.ConsoleConnect
            or SessionSwitchReason.RemoteConnect
            or SessionSwitchReason.SessionLogon;
}
