using Microsoft.Win32;

namespace MonitorWellness.Core;

/// <summary>
/// Owns one GammaRampController per active monitor and keeps them in sync with display
/// topology changes — mirrors OverlayController's rebuild pattern, which this class was
/// missing until Week 4: the gamma controller list was previously built once at startup and
/// never rebuilt, so a monitor added/removed after launch (or a device context invalidated
/// by a sleep/resume cycle) would silently go stale.
/// </summary>
public sealed class GammaControllerManager : IDisposable, IColorTemperatureTarget
{
    // Guards _controllers: OnDisplaySettingsChanged can rebuild it off the UI thread when
    // Application.Current is null (e.g. during shutdown), concurrently with a schedule-tick
    // read on the UI thread — named as a risk but never reproduced in INDEPENDENT_REAUDIT.md.
    // The lock removes the theoretical race instead of relying on timing.
    private readonly object _lock = new();
    private readonly Dictionary<string, GammaRampController> _controllers = new();
    private bool _disposed;

    public GammaControllerManager()
    {
        RebuildControllers();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            dispatcher.Invoke(RebuildControllers);
        else
            RebuildControllers();
    }

    private void RebuildControllers()
    {
        var monitors = MonitorEnumerator.GetActiveMonitors(forceRefresh: true); // a display change is exactly when a cached list is wrong
        var current = new HashSet<string>(monitors.Select(m => m.DeviceName));
        DebugLog.Write($"GammaControllerManager rebuild: {current.Count} active monitor(s): [{string.Join(", ", current)}]");

        lock (_lock)
        {
            foreach (var stale in _controllers.Keys.Except(current).ToList())
            {
                _controllers[stale].Dispose();
                _controllers.Remove(stale);
            }

            foreach (var deviceName in current)
            {
                if (_controllers.ContainsKey(deviceName))
                    continue;

                try
                {
                    _controllers[deviceName] = new GammaRampController(deviceName);
                }
                catch (InvalidOperationException)
                {
                    // Monitor doesn't support a gamma-ramp-capable device context; skip it
                    // rather than crashing the whole app.
                }
            }

            PersistBaselines(monitors);
        }
    }

    /// <summary>
    /// Write-ahead record of each display's pre-modification ramp, written before anything in
    /// this app has changed it (rebuilds run before the first apply) and deleted again on a
    /// clean restore. If the process dies in between, DisplayStateRecovery puts these back at
    /// the next launch. Caller holds _lock.
    /// </summary>
    private void PersistBaselines(IReadOnlyList<MonitorInfo> monitors)
    {
        var entries = _controllers.Values
            .Select(controller => new GammaBaselineEntry(
                controller.DeviceName,
                monitors.FirstOrDefault(m => string.Equals(m.DeviceName, controller.DeviceName, StringComparison.OrdinalIgnoreCase))?.HardwareDeviceId ?? "",
                controller.Baseline.Red,
                controller.Baseline.Green,
                controller.Baseline.Blue))
            .ToList();
        RecoveryJournals.GammaBaselines.Save(entries);
    }

    /// <summary>A snapshot of the current controllers, safe to enumerate even if a display-topology change rebuilds the live set concurrently.</summary>
    public IReadOnlyCollection<GammaRampController> Controllers
    {
        get { lock (_lock) return _controllers.Values.ToList(); }
    }

    /// <summary>
    /// Rebuilds device contexts after a sleep/resume cycle. Some driver configurations
    /// invalidate or reset gamma ramp state across sleep or monitor power-cycling; rebuilding
    /// is the safe recovery regardless of the exact cause, since the caller reapplies its
    /// current target (schedule or migraine) right after calling this.
    /// </summary>
    public void ReapplyAfterWake() => RebuildControllers();

    /// <summary>IColorTemperatureTarget implementation — applies the same Kelvin/contrast to every currently-tracked monitor, the primitive MigraineModeController's activate/fade logic pushes through.</summary>
    public void ApplyToAll(int kelvin, double contrastReduction)
    {
        foreach (var controller in Controllers)
        {
            if (!controller.ApplyColorTemperatureWithContrast(kelvin, contrastReduction))
                DebugLog.Write($"GammaControllerManager.ApplyToAll: rejected for {controller.DeviceName} (kelvin={kelvin}, contrast={contrastReduction}, Win32 error {controller.LastWin32Error}).");
        }
    }

    /// <summary>Immediately restores all controllable gamma ramps to the baseline captured when this app took control (the display's calibration curve, or linear).</summary>
    public void RestoreAllToBaseline()
    {
        foreach (var controller in Controllers)
        {
            if (!controller.RestoreBaseline())
                DebugLog.Write($"GammaControllerManager.RestoreAllToBaseline: restore rejected for {controller.DeviceName}.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        lock (_lock)
        {
            bool allRestored = true;
            foreach (var controller in _controllers.Values)
            {
                allRestored &= controller.RestoreBaseline();
                controller.Dispose();
            }
            _controllers.Clear();

            // Only forget the baselines once every display is really back to normal; a failed
            // restore leaves them for the next launch to retry.
            if (allRestored)
                RecoveryJournals.GammaBaselines.Delete();
        }
    }
}
