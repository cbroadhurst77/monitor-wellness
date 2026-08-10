namespace MonitorWellness.Core;

/// <summary>
/// Decides whether a foreground-window change needs to touch display state. Most application
/// switches have no comfort rule, so reapplying gamma and a topmost overlay for every one is
/// unnecessary churn and can be visually disruptive on some drivers.
/// </summary>
public static class ApplicationRuleRefreshPolicy
{
    public static bool ShouldRefresh(
        ApplicationComfortRule? matchingRule,
        bool nativeDisplayRuleWasActive,
        bool comfortPlanRuleWasActive) =>
        matchingRule is not null || nativeDisplayRuleWasActive || comfortPlanRuleWasActive;
}
