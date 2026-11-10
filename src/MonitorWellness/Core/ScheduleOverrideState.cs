namespace MonitorWellness.Core;

/// <summary>
/// Everything that can currently hold the normal day/night schedule back or replace it, in one
/// place instead of six loose fields in App: an explicit pause, a temporary comfort plan, an
/// application rule, the fullscreen guard and a live Settings preview. (Migraine mode keeps its own
/// state in MigraineModeController.) The pure parts live here so they can be tested.
/// </summary>
public sealed class ScheduleOverrideState
{
    /// <summary>When an explicit "Pause Schedule" ends, or null when not paused.</summary>
    public DateTime? PauseUntilUtc { get; set; }

    /// <summary>A comfort plan picked from the tray menu for now.</summary>
    public string? ManualComfortPlanName { get; set; }

    /// <summary>Process name of the application rule that has restored the native display, or null.</summary>
    public string? ActiveNativeDisplayRuleProcessName { get; set; }

    /// <summary>Description of the application rule whose comfort plan is applied, or null.</summary>
    public string? ActiveComfortPlanRuleDescription { get; set; }

    /// <summary>True while a fullscreen app has caused the native display to be restored.</summary>
    public bool FullscreenPresentationGuardActive { get; set; }

    /// <summary>True while the Settings window is open and previewing on screen.</summary>
    public bool SettingsPreviewActive { get; set; }

    public bool IsPaused => PauseUntilUtc.HasValue;

    /// <summary>True if a rule or the fullscreen guard currently holds something that must be cleared when it ends.</summary>
    public bool AnyRuleStateActive
        => ActiveNativeDisplayRuleProcessName is not null
            || ActiveComfortPlanRuleDescription is not null
            || FullscreenPresentationGuardActive;

    public void Pause(DateTime nowUtc, TimeSpan duration) => PauseUntilUtc = nowUtc + duration;

    public void ClearPause() => PauseUntilUtc = null;

    /// <summary>True when a pause is set and the clock has reached its end (the caller then clears it and re-applies the schedule).</summary>
    public bool IsPauseExpired(DateTime nowUtc) => SchedulePause.IsExpired(PauseUntilUtc, nowUtc);

    /// <summary>
    /// A plain-language sentence for whichever override is in force, in the order of precedence
    /// the schedule uses, or null when the normal schedule is running.
    /// </summary>
    public string? DescribeStatus()
    {
        if (PauseUntilUtc is DateTime pauseUntil)
            return $"Currently: Schedule paused until {pauseUntil.ToLocalTime():h:mm tt}.";
        if (FullscreenPresentationGuardActive)
            return "Currently: Native display restored for a fullscreen presentation or app.";
        if (ActiveNativeDisplayRuleProcessName is not null)
            return $"Currently: Native display restored for {ActiveNativeDisplayRuleProcessName}.";
        if (ManualComfortPlanName is not null)
            return $"Currently: {ManualComfortPlanName} comfort plan selected temporarily.";
        if (ActiveComfortPlanRuleDescription is not null)
            return $"Currently: {ActiveComfortPlanRuleDescription}.";
        return null;
    }
}
