namespace MonitorWellness.Core;

/// <summary>
/// Uses physical brightness only for monitors a user explicitly approved after testing.
/// Any failure restores the original brightness and leaves the normal overlay as fallback.
/// </summary>
public sealed class HardwareBrightnessControllerManager : IDisposable
{
    private readonly Dictionary<string, DdcCiBrightnessTestSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <summary>
    /// Applies scheduled targets and reports failures to the caller, which persists the
    /// associated monitor's safety quarantine. Failed hardware is never retried in-process.
    /// </summary>
    public HardwareBrightnessApplicationResult ApplyApprovedBrightness(IReadOnlyCollection<string> approvedDevices, IReadOnlyDictionary<string, double> brightnessByDevice)
    {
        var approved = approvedDevices.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string staleDevice in _sessions.Keys.Where(deviceName => !approved.Contains(deviceName)).ToList())
            DisposeSession(staleDevice);

        var applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string deviceName in approved)
        {
            if (!brightnessByDevice.TryGetValue(deviceName, out double brightness))
            {
                DisposeSession(deviceName);
                continue;
            }

            if (!_sessions.TryGetValue(deviceName, out DdcCiBrightnessTestSession? session))
            {
                if (!DdcCiBrightnessProbe.TryOpenTestSession(deviceName, out session, out string openError) || session is null)
                {
                    DebugLog.Write($"Hardware brightness unavailable for {deviceName}; using overlay fallback: {openError}");
                    failures[deviceName] = openError;
                    continue;
                }
                _sessions[deviceName] = session;
            }

            if (!session.TryApplyNormalizedBrightness(brightness, out string applyError))
            {
                DebugLog.Write($"Hardware brightness apply failed for {deviceName}; using overlay fallback: {applyError}");
                DisposeSession(deviceName);
                failures[deviceName] = applyError;
                continue;
            }

            applied.Add(deviceName);
        }

        return new HardwareBrightnessApplicationResult(applied, failures);
    }

    public void RestoreAll()
    {
        foreach (string deviceName in _sessions.Keys.ToList())
            DisposeSession(deviceName);
    }

    private void DisposeSession(string deviceName)
    {
        if (_sessions.Remove(deviceName, out DdcCiBrightnessTestSession? session))
            session.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        RestoreAll();
    }
}

/// <summary>Outcome of one scheduled DDC/CI application attempt.</summary>
public sealed record HardwareBrightnessApplicationResult(
    IReadOnlySet<string> AppliedDeviceNames,
    IReadOnlyDictionary<string, string> FailuresByDeviceName);
