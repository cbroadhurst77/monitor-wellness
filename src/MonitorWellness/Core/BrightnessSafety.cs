namespace MonitorWellness.Core;

/// <summary>
/// Calculates a monitor's final scheduled brightness without allowing a per-monitor
/// multiplier to bypass the persisted brightness safety floor.
/// </summary>
public static class BrightnessSafety
{
    /// <summary>
    /// A normal schedule must leave the primary display visibly recoverable. Migraine mode is
    /// intentionally a separate, user-triggered emergency mode and is not calculated here.
    /// </summary>
    public const double MinimumPrimaryMonitorBrightness = 0.20;

    public static double CalculateEffectiveBrightness(double globalBrightness, double multiplier, bool isPrimaryMonitor = false)
    {
        double clampedGlobalBrightness = Math.Clamp(globalBrightness, AppSettingsValidator.MinimumSafeBrightness, 1.0);
        double clampedMultiplier = Math.Clamp(multiplier, 0.0, 5.0);
        double proposedBrightness = 1.0 - (1.0 - clampedGlobalBrightness) * clampedMultiplier;
        double minimumBrightness = isPrimaryMonitor
            ? MinimumPrimaryMonitorBrightness
            : AppSettingsValidator.MinimumSafeBrightness;
        return Math.Clamp(proposedBrightness, minimumBrightness, 1.0);
    }
}
