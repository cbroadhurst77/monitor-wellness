namespace MonitorWellness.Core;

/// <summary>What a recovery pass found and did.</summary>
public sealed record DisplayRecoveryResult(int GammaRestored, int BrightnessRestored, int BrightnessStillPending)
{
    public bool RestoredAnything => GammaRestored > 0 || BrightnessRestored > 0;
}

/// <summary>
/// Puts the display back after a previous run ended without cleaning up (killed, crashed hard,
/// power loss): the gamma ramp and the monitor's own DDC/CI brightness both persist after the
/// process that set them is gone. The recovery journals (RecoveryJournal.cs) hold what each
/// display looked like before this app first touched it. Runs at every launch, before anything
/// reads the current display state, and on demand through <c>--restore</c>.
/// </summary>
public static class DisplayStateRecovery
{
    /// <summary>A journalled brightness older than this is discarded rather than applied: the person has probably changed it themselves since.</summary>
    private static readonly TimeSpan BrightnessJournalMaxAge = TimeSpan.FromDays(7);

    public static DisplayRecoveryResult RestoreLeftoverState()
    {
        IReadOnlyList<MonitorInfo> monitors = MonitorEnumerator.GetActiveMonitors();
        int gamma = RestoreGamma(monitors);
        (int restored, int pending) = RestoreBrightness(monitors);
        return new DisplayRecoveryResult(gamma, restored, pending);
    }

    /// <summary>
    /// For an explicit user request (<c>--restore</c>): leftover state first, then, if no gamma
    /// journal existed to restore from, write a linear ramp to every display so the screen is
    /// guaranteed to be back to a neutral, visible state.
    /// </summary>
    public static DisplayRecoveryResult RestoreForUser()
    {
        DisplayRecoveryResult result = RestoreLeftoverState();
        if (result.GammaRestored > 0)
            return result;

        int forced = 0;
        foreach (MonitorInfo monitor in MonitorEnumerator.GetActiveMonitors())
        {
            if (GammaRampController.TryWriteRamp(monitor.DeviceName, GammaRamp.Identity()))
                forced++;
        }
        return result with { GammaRestored = forced };
    }

    private static int RestoreGamma(IReadOnlyList<MonitorInfo> monitors)
    {
        IReadOnlyList<GammaBaselineEntry> entries = RecoveryJournals.GammaBaselines.Load();
        if (entries.Count == 0)
            return 0;

        int restored = 0;
        foreach (GammaBaselineEntry entry in entries)
        {
            string? deviceName = ResolveDevice(monitors, entry.HardwareDeviceId, entry.DeviceName);
            var ramp = new GammaRamp(entry.Red ?? Array.Empty<ushort>(), entry.Green ?? Array.Empty<ushort>(), entry.Blue ?? Array.Empty<ushort>());
            if (deviceName is null || !ramp.HasValidShape || !GammaRampMath.IsPlausibleBaseline(ramp))
                continue;

            if (GammaRampController.TryWriteRamp(deviceName, ramp))
            {
                restored++;
                DebugLog.Write($"DisplayStateRecovery: restored {deviceName}'s gamma ramp left over from a previous run.");
            }
        }

        // Entries for displays that are no longer connected can't be applied; the driver resets
        // a reconnected display's ramp itself, so the journal is cleared rather than kept forever.
        RecoveryJournals.GammaBaselines.Delete();
        return restored;
    }

    private static (int Restored, int Pending) RestoreBrightness(IReadOnlyList<MonitorInfo> monitors)
    {
        IReadOnlyList<BrightnessOriginalEntry> entries = RecoveryJournals.BrightnessOriginals.Load();
        if (entries.Count == 0)
            return (0, 0);

        var remaining = new List<BrightnessOriginalEntry>();
        int restored = 0;
        foreach (BrightnessOriginalEntry entry in entries)
        {
            if (IsStale(entry, DateTime.UtcNow))
            {
                DebugLog.Write($"DisplayStateRecovery: discarded a stale hardware-brightness record for {entry.DeviceName}.");
                continue;
            }

            string? deviceName = ResolveDevice(monitors, entry.HardwareDeviceId, entry.DeviceName);
            if (deviceName is not null
                && DdcCiBrightnessProbe.TryRestoreOriginal(deviceName, entry.PhysicalIndex, entry.Value, entry.Minimum, entry.Maximum))
            {
                restored++;
                DebugLog.Write($"DisplayStateRecovery: restored {deviceName}'s hardware brightness left over from a previous run.");
            }
            else
            {
                remaining.Add(entry); // monitor absent or the write failed: try again next launch
            }
        }

        RecoveryJournals.BrightnessOriginals.Save(remaining);
        return (restored, remaining.Count);
    }

    /// <summary>True for a brightness record too old to apply (the person has probably changed the brightness themselves since). Pure and testable.</summary>
    internal static bool IsStale(BrightnessOriginalEntry entry, DateTime nowUtc) => nowUtc - entry.SavedUtc > BrightnessJournalMaxAge;

    /// <summary>Prefers the stable hardware identity; falls back to the Windows device name when no identity was recorded. Pure and testable.</summary>
    internal static string? ResolveDevice(IReadOnlyList<MonitorInfo> monitors, string hardwareDeviceId, string deviceName)
    {
        if (!string.IsNullOrWhiteSpace(hardwareDeviceId))
        {
            MonitorInfo? byIdentity = monitors.FirstOrDefault(monitor =>
                string.Equals(monitor.HardwareDeviceId, hardwareDeviceId, StringComparison.OrdinalIgnoreCase));
            if (byIdentity is not null)
                return byIdentity.DeviceName;
        }

        return monitors
            .FirstOrDefault(monitor => string.Equals(monitor.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
            ?.DeviceName; // the display's own spelling, not the caller's
    }
}
