namespace MonitorWellness.Core;

/// <summary>
/// Decides whether a foreground-window change needs to touch display state. Most application
/// switches have no comfort rule, so reapplying gamma and a topmost overlay for every one is
/// unnecessary churn and can be visually disruptive on some drivers.
/// </summary>
public static class ApplicationRuleRefreshPolicy
{
    /// <summary>
    /// Whether the 2-second foreground check has any reason to run. With no enabled app rule, the
    /// full-screen guard off and no rule state left to clear, every check would only look up the
    /// foreground process to learn nothing, forever; skipping it removes that constant background work.
    /// </summary>
    public static bool ShouldPollForeground(bool fullscreenGuardEnabled, IEnumerable<ApplicationComfortRule> rules, bool anyRuleStateToClear)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return fullscreenGuardEnabled || anyRuleStateToClear || rules.Any(rule => rule.IsEnabled);
    }

    public static bool ShouldRefresh(
        ApplicationComfortRule? matchingRule,
        bool nativeDisplayRuleWasActive,
        bool comfortPlanRuleWasActive,
        bool fullscreenGuardShouldApply = false,
        bool fullscreenGuardWasActive = false) =>
        matchingRule is not null || nativeDisplayRuleWasActive || comfortPlanRuleWasActive
        || fullscreenGuardShouldApply || fullscreenGuardWasActive;
}
