using System.Windows.Forms;
using System.Windows.Threading;
using Microsoft.Win32;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace MonitorWellness.Core;

/// <summary>
/// Owns one OverlayWindow per active monitor and keeps them in sync with the current
/// display topology. Screen.DeviceName (e.g. "\\.\DISPLAY1") uses the same format as
/// MonitorEnumerator's device names, so the two can be correlated directly.
/// </summary>
public sealed class OverlayController : IDisposable
{
    private readonly Dictionary<string, OverlayWindow> _windows = new();
    private bool _disposed;

    public OverlayController()
    {
        RebuildWindows();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => RebuildWindows();

    private void RebuildWindows()
    {
        var currentDeviceNames = new HashSet<string>(Screen.AllScreens.Select(s => s.DeviceName));
        DebugLog.Write($"RebuildWindows: {Screen.AllScreens.Length} screen(s) reported by Screen.AllScreens: [{string.Join(", ", currentDeviceNames)}]");

        // Drop windows for monitors that disappeared.
        foreach (var staleName in _windows.Keys.Except(currentDeviceNames).ToList())
        {
            _windows[staleName].Close();
            _windows.Remove(staleName);
        }

        // Add windows for monitors that appeared.
        foreach (var screen in Screen.AllScreens)
        {
            if (_windows.ContainsKey(screen.DeviceName))
                continue;

            var window = new OverlayWindow();
            window.Show();
            window.PositionOver(screen.Bounds);
            _windows[screen.DeviceName] = window;
            DebugLog.Write($"Created overlay window for {screen.DeviceName}, bounds={screen.Bounds}");
        }

        // Existing windows may need repositioning (resolution/arrangement change).
        foreach (var screen in Screen.AllScreens)
        {
            if (_windows.TryGetValue(screen.DeviceName, out var window))
                window.PositionOver(screen.Bounds);
        }
    }

    /// <summary>Device names currently tracked by an overlay window, for callers that need to target "every monitor" (e.g. migraine mode).</summary>
    public IReadOnlyCollection<string> DeviceNames => _windows.Keys;

    /// <summary>
    /// The single primitive both the normal day/night schedule and migraine mode's
    /// activate/fade logic apply through, so there's exactly one code path that touches
    /// window tint state. A monitor with no entry in <paramref name="byDevice"/> is left
    /// as-is (not reset) — callers always pass a complete map for the devices they care about.
    /// </summary>
    public void Apply(IReadOnlyDictionary<string, (Color Color, double Opacity)> byDevice)
    {
        foreach (var (deviceName, window) in _windows)
        {
            if (byDevice.TryGetValue(deviceName, out var setting))
                window.SetTint(setting.Color, Math.Clamp(setting.Opacity, 0.0, 1.0));
        }
    }

    /// <summary>
    /// Convenience wrapper over Apply for the common case: a black dim overlay per monitor,
    /// given effective brightness (1.0 = no dimming, lower = darker) per device name.
    /// </summary>
    public void ApplyDim(IReadOnlyDictionary<string, double> brightnessByDevice)
    {
        var byDevice = brightnessByDevice.ToDictionary(
            kv => kv.Key,
            kv => (Colors.Black, 1.0 - Math.Clamp(kv.Value, 0.0, 1.0)));
        Apply(byDevice);
    }

    /// <summary>
    /// Briefly shows each monitor's device name on that monitor, so a per-monitor setting
    /// (or a bug report) can be tied to a physical screen with certainty rather than a guess.
    /// </summary>
    public void IdentifyMonitors(TimeSpan duration)
    {
        DebugLog.Write($"IdentifyMonitors invoked, {_windows.Count} window(s) tracked: [{string.Join(", ", _windows.Keys)}]");
        try
        {
            foreach (var (deviceName, window) in _windows)
            {
                // Path.GetFileName misparses "\\.\DISPLAY1" as a UNC path (treats "." as a
                // server name and "DISPLAY1" as the share), returning "" — confirmed via
                // tools/GammaCheck. Win32 device path strings aren't real paths, so Path
                // methods shouldn't be used on them at all; a plain last-backslash split does.
                string label = deviceName[(deviceName.LastIndexOf('\\') + 1)..];
                DebugLog.Write($"ShowLabel('{label}') on {deviceName}, window.IsVisible={window.IsVisible}");
                window.ShowLabel(label);
            }

            var timer = new DispatcherTimer { Interval = duration };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                foreach (var window in _windows.Values)
                    window.HideLabel();
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
        foreach (var window in _windows.Values)
            window.Close();
        _windows.Clear();
    }
}
