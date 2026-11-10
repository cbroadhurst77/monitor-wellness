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

    /// <summary>True if the user had approved this monitor before it was quarantined; only such monitors are ever retried automatically.</summary>
    public bool ApprovedBeforeQuarantine { get; set; }

    /// <summary>Consecutive failed runtime commands (each already retried once after re-opening the monitor handles).</summary>
    public int FailureCount { get; set; }
}

/// <summary>
/// Resolves hardware-brightness approvals against a physical monitor identity rather than a
/// transient Windows display number. This safety boundary deliberately fails closed: a monitor
/// without a hardware identifier cannot receive automatic DDC/CI writes.
/// </summary>
public static class HardwareBrightnessSafety
{
    private const string HardwareIdPrefix = "monitor:";

    /// <summary>
    /// A quarantine is no longer forever. A monitor the user had approved is retried after a
    /// backoff (30 minutes, then 2 hours, then 12 hours); after this many failures it stays
    /// quarantined until the user re-tests it. A one-off DDC/CI hiccup (common right after
    /// wake or a dock change) therefore doesn't permanently switch hardware dimming off.
    /// </summary>
    public const int MaximumAutomaticRetries = 3;

    private static readonly TimeSpan[] RetryBackoff =
    {
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(12),
    };

    public static string? GetKey(MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        return string.IsNullOrWhiteSpace(monitor.HardwareDeviceId)
            ? null
            : HardwareIdPrefix + monitor.HardwareDeviceId.Trim().ToUpperInvariant();
    }

    /// <summary>True if automatic hardware brightness may be attempted on this monitor right now (approved, or quarantined but due for a bounded retry).</summary>
    public static bool IsApproved(AppSettings settings, MonitorInfo monitor, DateTime? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? key = GetKey(monitor);
        if (key is not null && settings.HardwareBrightnessSafetyByMonitor.TryGetValue(key, out HardwareBrightnessSafetyState? state))
            return state.IsQuarantined ? IsRetryDue(state, nowUtc ?? DateTime.UtcNow) : state.IsApproved;

        // Legacy display-name opt-ins are deliberately not honoured. A dock/replug can reuse
        // a DISPLAYn name for a different physical monitor, so the safe migration path is a
        // fresh reversible test rather than silently carrying approval forward.
        return false;
    }

    /// <summary>Whether a quarantined, previously approved monitor has waited out its backoff and has retries left.</summary>
    public static bool IsRetryDue(HardwareBrightnessSafetyState state, DateTime nowUtc)
    {
        if (!state.IsQuarantined || !state.ApprovedBeforeQuarantine || !state.QuarantinedUtc.HasValue)
            return false;
        if (state.FailureCount < 1 || state.FailureCount > MaximumAutomaticRetries)
            return false;

        return nowUtc >= state.QuarantinedUtc.Value + RetryBackoff[state.FailureCount - 1];
    }

    /// <summary>Clears a quarantine after a retry succeeded. Returns true if anything changed (the caller persists).</summary>
    public static bool ClearQuarantineAfterRecovery(AppSettings settings, MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? key = GetKey(monitor);
        if (key is null
            || !settings.HardwareBrightnessSafetyByMonitor.TryGetValue(key, out HardwareBrightnessSafetyState? state)
            || !state.IsQuarantined)
        {
            return false;
        }

        settings.HardwareBrightnessSafetyByMonitor[key] = new HardwareBrightnessSafetyState
        {
            IsApproved = true,
            LastVerifiedUtc = state.LastVerifiedUtc,
        };
        return true;
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

        settings.HardwareBrightnessSafetyByMonitor.TryGetValue(key, out HardwareBrightnessSafetyState? existing);

        // A repeat failure of an already-quarantined monitor (it was only attempted because a
        // retry was due) counts against its retry budget and restarts the backoff.
        bool wasApproved = existing is null ? false : existing.IsQuarantined ? existing.ApprovedBeforeQuarantine : existing.IsApproved;
        int failureCount = existing is { IsQuarantined: true } ? existing.FailureCount + 1 : 1;

        settings.HardwareBrightnessSafetyByMonitor[key] = new HardwareBrightnessSafetyState
        {
            IsApproved = false,
            IsQuarantined = true,
            LastVerifiedUtc = existing?.LastVerifiedUtc,
            QuarantinedUtc = DateTime.UtcNow,
            QuarantineReason = reason,
            ApprovedBeforeQuarantine = wasApproved,
            FailureCount = failureCount,
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
                ApprovedBeforeQuarantine = pair.Value.ApprovedBeforeQuarantine,
                FailureCount = pair.Value.FailureCount,
            },
            StringComparer.OrdinalIgnoreCase);
}
