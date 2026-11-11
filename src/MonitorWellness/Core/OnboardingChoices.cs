namespace MonitorWellness.Core;

/// <summary>
/// What the optional "Breaks and comfort" step of the first-run welcome can change. Everything is opt-in:
/// leaving the step alone changes nothing, and only a ticked box or a chosen plan is written.
/// </summary>
public static class OnboardingChoices
{
    /// <summary>Applies the choices to the settings; returns true if anything changed.</summary>
    public static bool Apply(AppSettings settings, bool wantsBreakReminders, string? defaultComfortPlan)
    {
        ArgumentNullException.ThrowIfNull(settings);

        bool changed = false;
        if (wantsBreakReminders && !settings.BreakReminderEnabled)
        {
            settings.BreakReminderEnabled = true;
            changed = true;
        }

        if (SensoryComfortPlans.IsSupported(defaultComfortPlan) && !string.Equals(settings.DefaultComfortPlan, defaultComfortPlan, StringComparison.Ordinal))
        {
            settings.DefaultComfortPlan = defaultComfortPlan!;
            changed = true;
        }

        return changed;
    }
}
