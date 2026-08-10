namespace MonitorWellness.Core;

/// <summary>Persisted safety decision for one physically identified monitor.</summary>
public sealed class HardwareBrightnessSafetyState
{
    /// <summary>True only after the user confirmed the reversible hardware test.</summary>
    public bool IsApproved { get; set; }

    /// <summary>Blocks automatic DDC/CI retries after a runtime failure until the user retests.</summary>
    public bool IsQuarantined { get; set; }

    public DateTime? LastVerifiedUtc { get; set; }
    public DateTime? QuarantinedUtc { get; set; }
    public string? QuarantineReason { get; set; }
}

/// <summary>
/// Resolves hardware-brightness approvals against a physical monitor identity rather than a
/// transient Windows display number. This safety boundary deliberately fails closed: a monitor
/// without a hardware identifier cannot receive automatic DDC/CI writes.
/// </summary>
public static class HardwareBrightnessSafety
{
    private const string HardwareIdPrefix = "monitor:";

    public static string? GetKey(MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        return string.IsNullOrWhiteSpace(monitor.HardwareDeviceId)
            ? null
            : HardwareIdPrefix + monitor.HardwareDeviceId.Trim().ToUpperInvariant();
    }

    public static bool IsApproved(AppSettings settings, MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? key = GetKey(monitor);
        if (key is not null && settings.HardwareBrightnessSafetyByMonitor.TryGetValue(key, out HardwareBrightnessSafetyState? state))
            return state.IsApproved && !state.IsQuarantined;

        // Legacy display-name opt-ins are deliberately not honoured. A dock/replug can reuse
        // a DISPLAYn name for a different physical monitor, so the safe migration path is a
        // fresh reversible test rather than silently carrying approval forward.
        return false;
    }

    public static bool IsQuarantined(AppSettings settings, MonitorInfo monitor, out string reason)
    {
        ArgumentNullException.ThrowIfNull(settings);
        reason = "";
        string? key = GetKey(monitor);
        if (key is null || !settings.HardwareBrightnessSafetyByMonitor.TryGetValue(key, out HardwareBrightnessSafetyState? state) || !state.IsQuarantined)
            return false;

        reason = string.IsNullOrWhiteSpace(state.QuarantineReason)
            ? "A previous hardware-brightness command failed. Test this monitor again before enabling it."
            : state.QuarantineReason;
        return true;
    }

    public static bool MarkVerified(AppSettings settings, MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? key = GetKey(monitor);
        if (key is null)
            return false;

        settings.HardwareBrightnessSafetyByMonitor[key] = new HardwareBrightnessSafetyState
        {
            IsApproved = true,
            LastVerifiedUtc = DateTime.UtcNow,
        };
        settings.HardwareBrightnessEnabledMonitors.RemoveAll(deviceName =>
            string.Equals(deviceName, monitor.DeviceName, StringComparison.OrdinalIgnoreCase));
        return true;
    }

    public static bool Quarantine(AppSettings settings, MonitorInfo monitor, string reason)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? key = GetKey(monitor);
        if (key is null)
            return false;

        if (settings.HardwareBrightnessSafetyByMonitor.TryGetValue(key, out HardwareBrightnessSafetyState? existing)
            && existing.IsQuarantined)
        {
            return false;
        }

        settings.HardwareBrightnessSafetyByMonitor[key] = new HardwareBrightnessSafetyState
        {
            IsApproved = false,
            IsQuarantined = true,
            QuarantinedUtc = DateTime.UtcNow,
            QuarantineReason = reason,
        };
        settings.HardwareBrightnessEnabledMonitors.RemoveAll(deviceName =>
            string.Equals(deviceName, monitor.DeviceName, StringComparison.OrdinalIgnoreCase));
        return true;
    }

    public static Dictionary<string, HardwareBrightnessSafetyState> CloneStates(
        IReadOnlyDictionary<string, HardwareBrightnessSafetyState> states) =>
        states.ToDictionary(
            pair => pair.Key,
            pair => new HardwareBrightnessSafetyState
            {
                IsApproved = pair.Value.IsApproved,
                IsQuarantined = pair.Value.IsQuarantined,
                LastVerifiedUtc = pair.Value.LastVerifiedUtc,
                QuarantinedUtc = pair.Value.QuarantinedUtc,
                QuarantineReason = pair.Value.QuarantineReason,
            },
            StringComparer.OrdinalIgnoreCase);
}
