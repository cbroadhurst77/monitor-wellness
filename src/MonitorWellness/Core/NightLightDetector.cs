using System.Diagnostics;

namespace MonitorWellness.Core;

/// <summary>
/// Best-effort detection of other software that writes to the same OS-level gamma
/// ramp/color-management state this app uses — see TECHNICAL_UX_REVIEW.md §1.4/§5.1. Two
/// independent writers (this app plus Windows Night Light, or this app plus a still-installed
/// f.lux, this app's own predecessor per README) fight over the same last-write-wins state with
/// no visible explanation of why the screen seems to flicker or "randomly" revert.
///
/// Deliberately does NOT attempt to parse Windows Night Light's on/off state from its
/// undocumented registry blob (HKCU\...\CloudStore\...\bluelightreductionstate). That format is
/// community-reverse-engineered, not published by Microsoft, and known to shift across Windows
/// builds. Checked directly on this project's own dev machine: the key doesn't exist at all
/// here (Night Light has apparently never been toggled on this account), so there is no real
/// sample to verify a parser against — shipping a confident true/false read of an unverified
/// format would be exactly the kind of unverified claim this project's own testing discipline
/// argues against (see IMPLEMENTATION.md's repeated "verify against reality, don't assume"
/// pattern). f.lux detection is reliable instead: it's a plain running-process check, verifiable
/// the same way as everything else in this app.
/// </summary>
public static class NightLightDetector
{
    private static readonly string[] KnownConflictingProcessNames = { "flux" };

    /// <summary>
    /// True if a process name matches a known gamma-ramp-writing tool this app might conflict
    /// with. Case-insensitive, and tolerant of the ".exe" suffix some callers include and
    /// Process.ProcessName never does. Pure and unit-tested separately from the actual process
    /// enumeration below, since the enumeration itself isn't mockable without a process to test
    /// against.
    /// </summary>
    public static bool MatchesKnownConflictingProcessName(string processName)
    {
        string trimmed = processName.Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^4];

        return KnownConflictingProcessNames.Any(known => string.Equals(known, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// True if f.lux (this app's own predecessor, per README) appears to be running right now.
    /// Failures (permission issues enumerating processes, etc.) are treated as "not detected"
    /// rather than surfaced — this is an advisory check, not a critical path, and should never
    /// itself be a reason startup fails.
    /// </summary>
    public static bool IsFluxRunning()
    {
        try
        {
            foreach (var name in KnownConflictingProcessNames)
            {
                var matches = Process.GetProcessesByName(name);
                try
                {
                    if (matches.Length > 0)
                        return true;
                }
                finally
                {
                    foreach (var process in matches)
                        process.Dispose();
                }
            }
            return false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            DebugLog.Write($"NightLightDetector.IsFluxRunning check failed: {ex.Message}");
            return false;
        }
    }
}
