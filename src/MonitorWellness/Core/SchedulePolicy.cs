namespace MonitorWellness.Core;

public enum ScheduleActionKind
{
    /// <summary>Something else owns the display right now (migraine mode, a settings preview, a pause); do nothing.</summary>
    Skip,
    /// <summary>A full-screen app is in front and the presentation guard is on: restore the native display.</summary>
    FullscreenGuard,
    /// <summary>A foreground-app rule says to restore the native display for this app.</summary>
    NativeDisplayRule,
    /// <summary>Apply the (possibly overridden) schedule.</summary>
    ApplySchedule,
}

public enum ScheduleOverrideSource
{
    None,
    /// <summary>The person picked a temporary comfort plan from the tray menu.</summary>
    ManualPlan,
    /// <summary>A foreground-app rule applies a comfort plan while that app is in front.</summary>
    ApplicationRule,
}

/// <param name="NativeDisplayRule">The rule to honour when <paramref name="Action"/> is NativeDisplayRule.</param>
/// <param name="Override">The comfort-plan schedule to use instead of the saved one, when <paramref name="Action"/> is ApplySchedule.</param>
/// <param name="RuleDescription">Plain-language description of an application-rule override, for the status line and log.</param>
public sealed record ScheduleDecision(
    ScheduleActionKind Action,
    ApplicationComfortRule? NativeDisplayRule = null,
    SensoryComfortSchedule? Override = null,
    ScheduleOverrideSource OverrideSource = ScheduleOverrideSource.None,
    string? RuleDescription = null);

/// <summary>
/// Everything the decision needs. The two detectors are delegates because they call Win32 and
/// must only run when an earlier, cheaper rule hasn't already settled the answer.
/// </summary>
public sealed record ScheduleDecisionInputs(
    bool MigraineSuspendsSchedule,
    bool SettingsPreviewActive,
    bool Paused,
    bool FullscreenGuardEnabled,
    Func<bool> IsForegroundFullscreen,
    Func<ApplicationComfortRule?> FindForegroundRule,
    string? ManualComfortPlanName);

/// <summary>
/// The precedence rules for what controls the display on each schedule tick, extracted from
/// App.RunScheduleTick so the order can be tested. Highest priority first:
/// 1. migraine mode / a live settings preview / a pause: leave the display alone;
/// 2. the full-screen presentation guard;
/// 3. a foreground-app rule that restores the native display;
/// 4. a comfort plan picked manually from the tray;
/// 5. a foreground-app rule that applies a comfort plan;
/// 6. the saved schedule.
/// App still owns every side effect (state flags, logging, writing to the display).
/// </summary>
public static class SchedulePolicy
{
    public static ScheduleDecision Decide(ScheduleDecisionInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        if (inputs.MigraineSuspendsSchedule || inputs.SettingsPreviewActive || inputs.Paused)
            return new ScheduleDecision(ScheduleActionKind.Skip);

        ApplicationComfortRule? rule = inputs.FindForegroundRule();

        if (inputs.FullscreenGuardEnabled && inputs.IsForegroundFullscreen())
            return new ScheduleDecision(ScheduleActionKind.FullscreenGuard);

        if (rule?.Action == ApplicationComfortActions.RestoreNativeDisplay)
            return new ScheduleDecision(ScheduleActionKind.NativeDisplayRule, NativeDisplayRule: rule);

        if (inputs.ManualComfortPlanName is not null
            && SensoryComfortPlans.TryGetSchedule(inputs.ManualComfortPlanName, out SensoryComfortSchedule manual))
        {
            return new ScheduleDecision(ScheduleActionKind.ApplySchedule, Override: manual, OverrideSource: ScheduleOverrideSource.ManualPlan);
        }

        if (rule?.Action == ApplicationComfortActions.ApplySensoryComfortPlan
            && SensoryComfortPlans.TryGetSchedule(rule.ComfortPlanName, out SensoryComfortSchedule planned))
        {
            return new ScheduleDecision(
                ScheduleActionKind.ApplySchedule,
                Override: planned,
                OverrideSource: ScheduleOverrideSource.ApplicationRule,
                RuleDescription: $"{rule.ComfortPlanName} comfort plan for {rule.ProcessName}");
        }

        return new ScheduleDecision(ScheduleActionKind.ApplySchedule);
    }
}
