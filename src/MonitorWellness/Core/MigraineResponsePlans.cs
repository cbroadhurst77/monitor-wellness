namespace MonitorWellness.Core;

/// <summary>
/// Named, evidence-honest intensity plans for the user-triggered migraine comfort feature.
/// They are personal comfort presets, not medical treatment or diagnostic categories.
/// </summary>
public static class MigraineResponsePlans
{
    public const string Gentle = "Gentle";
    public const string Strong = "Strong";

    public static bool IsSupported(string? plan) =>
        string.Equals(plan, Gentle, StringComparison.Ordinal)
        || string.Equals(plan, Strong, StringComparison.Ordinal);

    public static bool IsMild(string? plan) => string.Equals(plan, Gentle, StringComparison.Ordinal);
}
