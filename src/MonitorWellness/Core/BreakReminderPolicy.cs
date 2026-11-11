namespace MonitorWellness.Core;

/// <summary>
/// Pure decision logic for an optional 20-20-20 reminder. Keeping this separate from the
/// tray and Win32 code makes the safety rules explicit and testable: a reminder must not
/// interrupt migraine recovery, a full-screen task, or someone who has already stepped away.
/// </summary>
public static class BreakReminderPolicy
{
    /// <summary>
    /// A person with no keyboard or mouse input for this long is taken to have left. It is
    /// deliberately not shorter: reading, watching a video or sitting in a call produce no input for
    /// minutes at a time and are exactly the screen use the reminder is for, so treating those pauses
    /// as breaks would mean the reminder rarely appears for the people who need it.
    /// Away this long counts as a break: the count restarts and a due reminder is not shown on return.
    /// </summary>
    public static readonly TimeSpan AwayThreshold = TimeSpan.FromMinutes(5);

    /// <summary>
    /// A due reminder is not shown while the last input is this old: the person is reading or has
    /// stepped away, and a prompt would appear on an empty screen. It stays due and is shown when they
    /// are back at the keyboard, unless they stay away for <see cref="AwayThreshold"/> (a break).
    /// </summary>
    public static readonly TimeSpan ShowIdleLimit = TimeSpan.FromMinutes(2);

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
        if (idleDuration >= ShowIdleLimit)
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
