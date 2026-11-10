using System.Windows.Threading;
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

    /// <summary>Display-change notifications arrive in bursts while a dock, driver or DPI setting settles; one rebuild after they stop replaces the burst.</summary>
    internal static readonly TimeSpan TopologyDebounce = TimeSpan.FromMilliseconds(400);
    private DispatcherTimer? _topologyTimer;

    public GammaControllerManager()
    {
        RebuildControllers();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    /// <summary>
    /// Raised (on the UI thread) after the controllers were rebuilt and their device contexts
    /// refreshed because the display layout changed. Windows often resets the colour ramp when the
    /// layout changes, so the app listens to this and reapplies the schedule instead of waiting up
    /// to 30 seconds for the next timer tick.
    /// </summary>
    public event Action? TopologyChanged;

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
            HandleTopologyChange();   // no UI thread (shutdown): nothing to debounce on
        else if (dispatcher.CheckAccess())
            ScheduleTopologyChange();
        else
            dispatcher.BeginInvoke(ScheduleTopologyChange);
    }

    private void ScheduleTopologyChange()
    {
        if (_disposed)
            return;

        if (_topologyTimer is null)
        {
            _topologyTimer = new DispatcherTimer { Interval = TopologyDebounce };
            _topologyTimer.Tick += (_, _) =>
            {
                _topologyTimer.Stop();
                if (!_disposed)
                    HandleTopologyChange();
            };
        }
        _topologyTimer.Stop();
        _topologyTimer.Start();
    }

    private void HandleTopologyChange()
    {
        RebuildControllers();
        foreach (var controller in Controllers)
            controller.RefreshDeviceContext();
        TopologyChanged?.Invoke();
    }

    private void RebuildControllers()
    {
        var monitors = MonitorEnumerator.GetActiveMonitors(forceRefresh: true); // a display change is exactly when a cached list is wrong
        var current = new HashSet<string>(monitors.Select(m => m.DeviceName));
        DebugLog.Write($"GammaControllerManager rebuild: {current.Count} active monitor(s): [{string.Join(", ", current)}]");

        lock (_lock)
        {
            bool setChanged = false;
            foreach (var stale in _controllers.Keys.Except(current).ToList())
            {
                _controllers[stale].Dispose();
                _controllers.Remove(stale);
                setChanged = true;
            }

            foreach (var deviceName in current)
            {
                if (_controllers.ContainsKey(deviceName))
                    continue;

                try
                {
                    _controllers[deviceName] = new GammaRampController(deviceName);
                    setChanged = true;
                }
                catch (InvalidOperationException)
                {
                    // Monitor doesn't support a gamma-ramp-capable device context; skip it
                    // rather than crashing the whole app.
                }
            }

            // The journal holds each display's baseline, which only changes when a display comes or
            // goes. Display-change notifications arrive in bursts (seen every ~6 s for minutes), and
            // re-saving the same ~30 KB file each time is pointless disk work. It is deleted only on
            // a clean exit, so skipping an unchanged save never leaves a display unprotected.
            if (setChanged)
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

    /// <summary>Re-applies our colour to any display another program has reset since the last apply. Returns how many were repaired.</summary>
    public int RepairResetDisplays()
    {
        int repaired = 0;
        foreach (var controller in Controllers)
        {
            if (controller.RepairIfReset())
            {
                repaired++;
                DebugLog.Write($"GammaControllerManager: {controller.DeviceName}'s colour had been reset by something else; reapplied.");
            }
        }
        return repaired;
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
        _topologyTimer?.Stop();
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
