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

    public const int MinimumIntervalMinutes = 1;
    public const int MaximumIntervalMinutes = 8 * 60;

    /// <summary>How often the reminder looks at keyboard/mouse activity.</summary>
    public static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(15);

    /// <summary>The one place the reminder interval is bounded; the validator and the service both use it.</summary>
    public static int ClampIntervalMinutes(int minutes) => Math.Clamp(minutes, MinimumIntervalMinutes, MaximumIntervalMinutes);

    /// <summary>"Until morning": 08:00 today if it is earlier than that now, otherwise 08:00 tomorrow.</summary>
    public static DateTime NextMorningLocal(DateTime nowLocal)
    {
        DateTime today = nowLocal.Date.AddHours(8);
        return nowLocal < today ? today : today.AddDays(1);
    }

    /// <summary>Session-only snoozes are active strictly until their stored UTC expiry.</summary>
    public static bool IsSnoozed(DateTime nowUtc, DateTime? snoozedUntilUtc) =>
        snoozedUntilUtc is DateTime until && until > nowUtc;

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
