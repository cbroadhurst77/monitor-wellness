namespace MonitorWellness.Core;

/// <summary>
/// Pure decision logic for an optional 20-20-20 reminder. Keeping this separate from the
/// tray and Win32 code makes the safety rules explicit and testable: a reminder must not
/// interrupt migraine recovery, a full-screen task, or someone who has already stepped away.
/// </summary>
public static class BreakReminderPolicy
{
    /// <summary>
    /// A person away from the keyboard for this long has already had a meaningful screen
    /// break. Skipping the next prompt avoids a delayed interruption immediately on return.
    /// </summary>
    public static readonly TimeSpan AwayThreshold = TimeSpan.FromMinutes(2);

    public static BreakReminderDecision Decide(bool migraineActive, bool likelyFullscreen, TimeSpan idleDuration)
    {
        if (migraineActive)
            return BreakReminderDecision.SuppressedForMigraine;
        if (likelyFullscreen)
            return BreakReminderDecision.SuppressedForFullscreen;
        if (idleDuration >= AwayThreshold)
            return BreakReminderDecision.SuppressedForIdle;

        return BreakReminderDecision.Show;
    }
}

public enum BreakReminderDecision
{
    Show,
    SuppressedForMigraine,
    SuppressedForFullscreen,
    SuppressedForIdle,
}
