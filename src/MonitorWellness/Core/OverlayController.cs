using System.Diagnostics;
using System.Windows.Forms;
using System.Windows.Threading;
using Microsoft.Win32;
using Color = System.Windows.Media.Color;

namespace MonitorWellness.Core;

/// <summary>
/// Owns the per-monitor dim/tint overlay windows and keeps them in sync with the current display
/// topology. Screen.DeviceName (e.g. "\\.\DISPLAY1") uses the same format as MonitorEnumerator's
/// device names, so the two can be correlated directly.
///
/// An overlay window is only on screen while it has something to show (see
/// OverlayVisibilityPolicy). Windows are created lazily, and prepared in advance — hidden, in idle
/// time after startup — so that showing one (migraine mode most of all, where someone mid-aura
/// wants relief now) is instant and the app's startup isn't spent building three transparent
/// full-screen windows that daytime never uses.
/// </summary>
public sealed class OverlayController : IDisposable, IOverlayTarget, IDimOverlay
{
    private static readonly TimeSpan TopologyRefreshDebounce = TimeSpan.FromMilliseconds(250);
    private readonly Dictionary<string, OverlayWindow> _windows = new();
    private readonly Dictionary<string, bool> _shown = new();
    private IReadOnlyList<(string DeviceName, System.Drawing.Rectangle Bounds)> _screens = Array.Empty<(string, System.Drawing.Rectangle)>();
    private DispatcherTimer? _topologyRefreshTimer;
    private DispatcherTimer? _identifyTimer;
    private bool _disposed;
    private bool _excludeFromCapture = true;

    /// <summary>Whether the overlay windows are hidden from screenshots and screen sharing (AppSettings.ExcludeOverlayFromCapture). Applies to existing and future windows.</summary>
    public bool ExcludeFromCapture
    {
        get => _excludeFromCapture;
        set
        {
            _excludeFromCapture = value;
            foreach (var window in _windows.Values)
                window.SetCaptureExclusion(value);
        }
    }

    public OverlayController()
    {
        RefreshScreens();
        DebugLog.Write($"OverlayController: {_screens.Count} screen(s); overlay windows are created when needed.");
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        VisualStabilityDiagnostics.RecordDisplayTopologySignal();
        // SystemEvents can raise this off the thread that subscribed (documented Windows
        // Forms/SystemEvents behavior, not guaranteed to always fire on the UI thread) —
        // touching WPF Window objects from a non-owning thread throws. Confirmed as a real,
        // plausible risk by INDEPENDENT_REAUDIT.md (never reproduced, since a real sleep/resume
        // or hot-plug cycle can't be triggered from a dev session, but named there as a concrete
        // hypothesis worth closing proactively rather than waiting to reproduce a crash first).
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            dispatcher.BeginInvoke(ScheduleTopologyRefresh);
        else
            ScheduleTopologyRefresh();
    }

    /// <summary>
    /// Display settings commonly arrive in a short burst while a dock, driver, or DPI setting
    /// settles. Rebuilding a topmost overlay for every notification can be perceived as a
    /// flash, so one quiet, delayed refresh replaces the whole burst.
    /// </summary>
    private void ScheduleTopologyRefresh()
    {
        _topologyRefreshTimer ??= new DispatcherTimer { Interval = TopologyRefreshDebounce };
        _topologyRefreshTimer.Tick -= OnTopologyRefreshTimerTick;
        _topologyRefreshTimer.Tick += OnTopologyRefreshTimerTick;
        _topologyRefreshTimer.Stop();
        _topologyRefreshTimer.Start();
    }

    private void OnTopologyRefreshTimerTick(object? sender, EventArgs e)
    {
        _topologyRefreshTimer?.Stop();
        VisualStabilityDiagnostics.RecordCoalescedTopologyRefresh();
        RebuildWindows();
    }

    private void RefreshScreens()
        => _screens = Screen.AllScreens.Select(screen => (screen.DeviceName, screen.Bounds)).ToList();

    /// <summary>
    /// After a display change: learn the new screens, close the windows of monitors that went away,
    /// and move the rest. New monitors get no window yet; the next apply creates one if it needs one.
    /// </summary>
    private void RebuildWindows()
    {
        RefreshScreens();
        var currentDeviceNames = new HashSet<string>(_screens.Select(screen => screen.DeviceName));
        DebugLog.Write($"RebuildWindows: {_screens.Count} screen(s) reported by Screen.AllScreens: [{string.Join(", ", currentDeviceNames)}]; {_windows.Count} overlay window(s) exist");

        foreach (var staleName in _windows.Keys.Except(currentDeviceNames).ToList())
        {
            _windows[staleName].Close();
            _windows.Remove(staleName);
            _shown.Remove(staleName);
        }

        foreach (var (deviceName, bounds) in _screens)
        {
            if (_windows.TryGetValue(deviceName, out var window))
                window.PositionOver(bounds);
        }
    }

    /// <summary>Every monitor's device name, whether or not it currently has an overlay window (callers use this to mean "every monitor", e.g. migraine mode).</summary>
    public IReadOnlyCollection<string> DeviceNames => _screens.Select(screen => screen.DeviceName).ToList();

    /// <summary>Returns the overlay window for a monitor, creating it if it doesn't exist yet. Creating it does not show it.</summary>
    private OverlayWindow? EnsureWindow(string deviceName)
    {
        if (_windows.TryGetValue(deviceName, out var existing))
            return existing;

        foreach (var (name, bounds) in _screens)
        {
            if (!string.Equals(name, deviceName, StringComparison.OrdinalIgnoreCase))
                continue;

            var stopwatch = Stopwatch.StartNew();
            var window = new OverlayWindow();
            window.PrepareHandle();
            window.SetCaptureExclusion(_excludeFromCapture);
            window.PositionOver(bounds);
            _windows[name] = window;
            DebugLog.Write($"Created overlay window for {name}, bounds={bounds}, in {stopwatch.ElapsedMilliseconds} ms (not shown yet)");
            return window;
        }

        return null;
    }

    /// <summary>
    /// Prepares the (hidden) overlay windows in idle time after startup, one per monitor, so the first
    /// time one is needed it only has to be shown. Harmless if a window is needed sooner: it is
    /// then created on the spot.
    /// </summary>
    public void PrewarmWhenIdle()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;

        foreach (var name in _screens.Select(screen => screen.DeviceName).ToList())
            dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => WarmUp(name, dispatcher));
    }

    /// <summary>
    /// Creates a monitor's overlay window and takes it through its first show while it is still fully
    /// transparent (so nothing is visible), then hides it again. WPF does most of its work the first
    /// time a window is shown; paying that now, in idle time, instead of when migraine mode needs the
    /// window, is what keeps showing it instant.
    /// </summary>
    private void WarmUp(string deviceName, Dispatcher dispatcher)
    {
        if (_disposed)
            return;

        OverlayWindow? window = EnsureWindow(deviceName);
        if (window is null || window.IsOnScreen)
            return; // unavailable, or already in use

        var stopwatch = Stopwatch.StartNew();
        window.ShowIfHidden();
        DebugLog.Write($"Overlay window for {deviceName}: first show took {stopwatch.ElapsedMilliseconds} ms (transparent, as a warm-up)");

        dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            // Hide it again unless something started using it in the meantime.
            if (!_disposed && !_shown.GetValueOrDefault(deviceName) && !window.IsShowingLabel)
                window.HideIfShown();
        });
    }

    /// <summary>
    /// The single primitive both the normal day/night schedule and migraine mode's
    /// activate/fade logic apply through, so there's exactly one code path that touches
    /// window tint state. A monitor with no entry in <paramref name="byDevice"/> is left
    /// as-is (not reset) — callers always pass a complete map for the devices they care about.
    /// A monitor whose amount is effectively zero has its overlay hidden, not just made transparent.
    /// </summary>
    public void Apply(IReadOnlyDictionary<string, (Color Color, double Opacity)> byDevice)
    {
        foreach (var (deviceName, _) in _screens)
        {
            if (!byDevice.TryGetValue(deviceName, out var setting))
                continue;

            double opacity = Math.Clamp(setting.Opacity, 0.0, 1.0);
            bool wasShown = _shown.GetValueOrDefault(deviceName);
            bool needed = OverlayVisibilityPolicy.NeedsOverlay(opacity, wasShown);
            _shown[deviceName] = needed;

            if (needed)
            {
                OverlayWindow? window = EnsureWindow(deviceName);
                if (window is null)
                {
                    _shown[deviceName] = false; // no window exists, so nothing is shown
                    continue;
                }

                window.SetTint(setting.Color, opacity);   // before showing, so nothing flashes at the wrong tint
                window.ShowIfHidden();
            }
            else if (_windows.TryGetValue(deviceName, out var existing))
            {
                existing.SetTint(System.Windows.Media.Colors.Transparent, 0.0);
                existing.HideIfShown();
            }
        }
    }

    /// <summary>
    /// Convenience wrapper over Apply for the common case: a dim overlay per monitor, given
    /// effective brightness (1.0 = no dimming, lower = darker) per device name. dimColor is
    /// usually black, but the normal schedule shifts it toward a warm dark brown during deep
    /// night (see AppSettings.DeepNightOverlayColorHex) to approximate warmth gamma ramp can't
    /// reach on its own.
    /// </summary>
    public void ApplyDim(IReadOnlyDictionary<string, double> brightnessByDevice, Color dimColor)
    {
        var byDevice = brightnessByDevice.ToDictionary(
            kv => kv.Key,
            kv => (dimColor, 1.0 - Math.Clamp(kv.Value, 0.0, 1.0)));
        Apply(byDevice);
    }

    /// <summary>Raises every overlay that is on screen back above other topmost windows.</summary>
    public void ReassertTopmost()
    {
        foreach (var window in _windows.Values)
            window.ReassertTopmost();
    }

    /// <summary>Immediately removes every dim/tint overlay from the screen.</summary>
    public void Clear()
    {
        foreach (var window in _windows.Values)
        {
            window.SetTint(System.Windows.Media.Colors.Transparent, 0.0);
            window.HideIfShown();
        }
        _shown.Clear();
    }

    /// <summary>
    /// Briefly shows each monitor's device name on that monitor, so a per-monitor setting
    /// (or a bug report) can be tied to a physical screen with certainty rather than a guess.
    /// </summary>
    public void IdentifyMonitors(TimeSpan duration)
    {
        DebugLog.Write($"IdentifyMonitors invoked, {_screens.Count} screen(s): [{string.Join(", ", _screens.Select(screen => screen.DeviceName))}]");
        try
        {
            foreach (var (deviceName, _) in _screens)
            {
                OverlayWindow? window = EnsureWindow(deviceName);
                if (window is null)
                    continue;

                // Path.GetFileName misparses "\\.\DISPLAY1" as a UNC path (treats "." as a
                // server name and "DISPLAY1" as the share), returning "" — confirmed via
                // tools/GammaCheck. Win32 device path strings aren't real paths, so Path
                // methods shouldn't be used on them at all; a plain last-backslash split does.
                string label = deviceName[(deviceName.LastIndexOf('\\') + 1)..];
                DebugLog.Write($"ShowLabel('{label}') on {deviceName}");
                window.ShowLabel(label);
                window.ShowIfHidden();
            }

            // A second call replaces the first one's timer instead of letting the earlier timer
            // take the labels down early.
            _identifyTimer?.Stop();
            var timer = new DispatcherTimer { Interval = duration };
            _identifyTimer = timer;
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                foreach (var (deviceName, window) in _windows)
                {
                    window.HideLabel();
                    if (!_shown.GetValueOrDefault(deviceName))
                        window.HideIfShown(); // it was only up to identify the monitor
                }
            };
            timer.Start();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"IdentifyMonitors EXCEPTION: {ex}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _topologyRefreshTimer?.Stop();
        _identifyTimer?.Stop();
        foreach (var window in _windows.Values)
            window.Close();
        _windows.Clear();
        _shown.Clear();
    }
}
