namespace MonitorWellness.Core;

/// <summary>Supported automatic actions when a configured application becomes foreground.</summary>
public static class ApplicationComfortActions
{
    /// <summary>
    /// Clears overlays, restores gamma and physical brightness, and keeps the native display
    /// state in place while the matched app remains foreground.
    /// </summary>
    public const string RestoreNativeDisplay = "RestoreNativeDisplay";

    public static bool IsSupported(string? action) =>
        string.Equals(action, RestoreNativeDisplay, StringComparison.Ordinal);
}

/// <summary>A local, user-created foreground-application comfort rule.</summary>
public sealed class ApplicationComfortRule
{
    /// <summary>Executable name only, normalized without a trailing .exe (for example, photoshop).</summary>
    public string ProcessName { get; set; } = "";

    public string Action { get; set; } = ApplicationComfortActions.RestoreNativeDisplay;
    public bool IsEnabled { get; set; } = true;

    public ApplicationComfortRule Clone() => new()
    {
        ProcessName = ProcessName,
        Action = Action,
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
        string? foregroundProcessName)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (!TryNormalizeProcessName(foregroundProcessName, out string processName))
            return null;

        return rules.FirstOrDefault(rule => rule.IsEnabled
            && ApplicationComfortActions.IsSupported(rule.Action)
            && TryNormalizeProcessName(rule.ProcessName, out string configuredProcessName)
            && string.Equals(configuredProcessName, processName, StringComparison.Ordinal));
    }
}
