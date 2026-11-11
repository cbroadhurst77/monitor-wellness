namespace MonitorWellness.Core;

/// <summary>What the machine currently looks like to a break reminder tick.</summary>
public readonly record struct BreakReminderSignals(bool MigraineActive, bool LikelyFullscreen, TimeSpan IdleDuration, bool InCall = false);

public enum BreakReminderTickOutcome
{
    /// <summary>A session snooze is still active; no signals were read.</summary>
    SkippedSnoozed,
    Show,
    Suppressed,
}

/// <param name="Outcome">What the caller should do.</param>
/// <param name="Decision">The policy decision; only meaningful when the tick was not snoozed.</param>
/// <param name="SnoozedUntilUtc">The active snooze expiry when <paramref name="Outcome"/> is SkippedSnoozed.</param>
/// <param name="SnoozeExpired">True when this tick cleared an elapsed snooze.</param>
public sealed record BreakReminderTick(
    BreakReminderTickOutcome Outcome,
    BreakReminderDecision Decision,
    DateTime? SnoozedUntilUtc,
    bool SnoozeExpired);

/// <summary>
/// The session-only snooze state and tick decision for the break reminder, extracted from
/// App so it can be tested without a tray icon, dispatcher timer or Win32 calls. App still owns
/// the timer, the balloon and the logging; this owns only the rules.
/// </summary>
public sealed class BreakReminderState
{
    public DateTime? SnoozedUntilUtc { get; private set; }

    public bool IsSnoozed(DateTime nowUtc) => BreakReminderPolicy.IsSnoozed(nowUtc, SnoozedUntilUtc);

    /// <summary>Starts a snooze. Ignored when reminders are off or the duration is not positive.</summary>
    public bool TrySnooze(bool remindersEnabled, DateTime nowUtc, TimeSpan duration)
    {
        if (!remindersEnabled || duration <= TimeSpan.Zero)
            return false;

        SnoozedUntilUtc = nowUtc + duration;
        return true;
    }

    /// <summary>Clears any snooze. Returns false when there was nothing to clear.</summary>
    public bool Resume()
    {
        if (SnoozedUntilUtc is null)
            return false;

        SnoozedUntilUtc = null;
        return true;
    }

    /// <summary>Called when reminders are turned off: a stale snooze must not survive re-enabling.</summary>
    public void Reset() => SnoozedUntilUtc = null;

    /// <summary>
    /// Evaluates one timer tick. <paramref name="readSignals"/> is only invoked when the tick
    /// is not snoozed, so a snoozed tick never touches the (Win32-backed) detectors.
    /// </summary>
    public BreakReminderTick Tick(DateTime nowUtc, Func<BreakReminderSignals> readSignals)
    {
        ArgumentNullException.ThrowIfNull(readSignals);

        if (IsSnoozed(nowUtc))
            return new BreakReminderTick(BreakReminderTickOutcome.SkippedSnoozed, BreakReminderDecision.Show, SnoozedUntilUtc, false);

        bool expired = SnoozedUntilUtc is not null;
        if (expired)
            SnoozedUntilUtc = null;

        BreakReminderSignals signals = readSignals();
        BreakReminderDecision decision = BreakReminderPolicy.Decide(signals.MigraineActive, signals.LikelyFullscreen, signals.IdleDuration, signals.InCall);
        return new BreakReminderTick(
            decision == BreakReminderDecision.Show ? BreakReminderTickOutcome.Show : BreakReminderTickOutcome.Suppressed,
            decision,
            null,
            expired);
    }
}
