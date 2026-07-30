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
    private readonly Dictionary<string, GammaRampController> _controllers = new();
    private bool _disposed;

    public GammaControllerManager()
    {
        RebuildControllers();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        // Same reasoning as OverlayController's identical guard: SystemEvents isn't
        // guaranteed to raise on the UI thread, and the shared _controllers dictionary isn't
        // thread-safe against a concurrent read from App's schedule tick. See
        // INDEPENDENT_REAUDIT.md for why this was named as a risk but never reproduced.
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            dispatcher.Invoke(RebuildControllers);
        else
            RebuildControllers();
    }

    private void RebuildControllers()
    {
        var current = new HashSet<string>(MonitorEnumerator.GetActiveMonitors().Select(m => m.DeviceName));
        DebugLog.Write($"GammaControllerManager rebuild: {current.Count} active monitor(s): [{string.Join(", ", current)}]");

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
    }

    /// <summary>The current live set of controllers — not a snapshot, so callers always see monitors added/removed after construction.</summary>
    public IReadOnlyCollection<GammaRampController> Controllers => _controllers.Values;

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
            controller.ApplyColorTemperatureWithContrast(kelvin, contrastReduction);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        foreach (var controller in _controllers.Values)
        {
            controller.ResetToIdentity();
            controller.Dispose();
        }
        _controllers.Clear();
    }
}
