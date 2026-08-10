namespace MonitorWellness.Core;

/// <summary>Supported automatic actions when a configured application becomes foreground.</summary>
public static class ApplicationComfortActions
{
    /// <summary>
    /// Clears overlays, restores gamma and physical brightness, and keeps the native display
    /// state in place while the matched app remains foreground.
    /// </summary>
    public const string RestoreNativeDisplay = "RestoreNativeDisplay";

    /// <summary>
    /// Applies a named built-in comfort plan while the rule is matched, without changing the
    /// user's saved schedule. The normal schedule resumes as soon as the app loses focus.
    /// </summary>
    public const string ApplySensoryComfortPlan = "ApplySensoryComfortPlan";

    public static bool IsSupported(string? action) =>
        string.Equals(action, RestoreNativeDisplay, StringComparison.Ordinal)
        || string.Equals(action, ApplySensoryComfortPlan, StringComparison.Ordinal);
}

/// <summary>A local, user-created foreground-application comfort rule.</summary>
public sealed class ApplicationComfortRule
{
    /// <summary>Executable name only, normalized without a trailing .exe (for example, photoshop).</summary>
    public string ProcessName { get; set; } = "";

    /// <summary>
    /// Optional case-insensitive substring of the foreground window title. This scopes a rule
    /// to a document, meeting, presentation, or workspace without inspecting its contents.
    /// </summary>
    public string? WindowTitleContains { get; set; }

    public string Action { get; set; } = ApplicationComfortActions.RestoreNativeDisplay;

    /// <summary>Required only for <see cref="ApplicationComfortActions.ApplySensoryComfortPlan"/>.</summary>
    public string? ComfortPlanName { get; set; }

    public bool IsEnabled { get; set; } = true;

    public ApplicationComfortRule Clone() => new()
    {
        ProcessName = ProcessName,
        WindowTitleContains = WindowTitleContains,
        Action = Action,
        ComfortPlanName = ComfortPlanName,
        IsEnabled = IsEnabled,
    };
}

/// <summary>Pure matching and validation helpers for application-aware comfort rules.</summary>
public static class ApplicationComfortRules
{
    public static bool TryNormalizeProcessName(string? value, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string candidate = value.Trim();
        if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            candidate = candidate[..^4];
        if (candidate.Length is < 1 or > 240 || candidate.IndexOfAny(['\\', '/', ':']) >= 0)
            return false;
        if (candidate.Any(character => !(char.IsLetterOrDigit(character) || character is '.' or '_' or '-')))
            return false;

        normalized = candidate.ToLowerInvariant();
        return true;
    }

    public static ApplicationComfortRule? FindForegroundRule(
        IEnumerable<ApplicationComfortRule> rules,
        string? foregroundProcessName,
        string? foregroundWindowTitle = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (!TryNormalizeProcessName(foregroundProcessName, out string processName))
            return null;

        // A title-scoped rule is more specific than a process-wide rule. This lets users retain
        // a sensible default while overriding it for a named meeting or document.
        return rules.Where(rule => rule.IsEnabled
            && ApplicationComfortActions.IsSupported(rule.Action)
            && TryNormalizeProcessName(rule.ProcessName, out string configuredProcessName)
            && string.Equals(configuredProcessName, processName, StringComparison.Ordinal)
            && (string.IsNullOrWhiteSpace(rule.WindowTitleContains)
                || (!string.IsNullOrWhiteSpace(foregroundWindowTitle)
                    && foregroundWindowTitle.Contains(rule.WindowTitleContains.Trim(), StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(rule => !string.IsNullOrWhiteSpace(rule.WindowTitleContains))
            .FirstOrDefault();
    }
}
