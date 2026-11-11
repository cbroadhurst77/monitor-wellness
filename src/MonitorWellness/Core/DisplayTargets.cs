using Color = System.Windows.Media.Color;

namespace MonitorWellness.Core;

// The pieces DisplayApplier writes to, as small interfaces. The real implementations own live Win32
// device contexts, WPF windows and DDC/CI sessions, none of which a test can create; with these a test
// can use fakes and check what reaches each display (which was how three real bugs in this layer got
// past review: nothing could exercise it).

/// <summary>One monitor's colour ramp.</summary>
internal interface IGammaController
{
    string DeviceName { get; }
    int LastWin32Error { get; }
    bool ApplyColorTemperature(int kelvin, double brightnessAssist = 1.0);
    bool ApplyColorTemperatureWithContrast(int kelvin, double contrastReduction);
    bool RestoreBaseline();

    /// <summary>Restores the native ramp only if something of ours is on it; free when there is nothing to undo.</summary>
    bool RestoreBaselineIfModified();
}

/// <summary>The set of colour ramps, one per active monitor.</summary>
internal interface IGammaSource
{
    IReadOnlyCollection<IGammaController> Controllers { get; }
    void RestoreAllToBaseline();
}

/// <summary>The dim/tint overlay.</summary>
internal interface IDimOverlay
{
    void ApplyDim(IReadOnlyDictionary<string, double> brightnessByDevice, Color dimColor);
    void Clear();
}

/// <summary>
/// What the Settings window needs from the overlay: the dim/tint itself, the list of screens, and the
/// "which monitor is which" labels. With this (and <see cref="IGammaSource"/>) the window can be
/// opened in a test with fakes, without touching the real screens.
/// </summary>
internal interface IPreviewOverlay : IDimOverlay, IOverlayTarget
{
    void IdentifyMonitors(TimeSpan duration);
}

/// <summary>Physical (DDC/CI) brightness, set off the UI thread.</summary>
internal interface IHardwareBrightness
{
    event Action<HardwareBrightnessApplicationResult>? ResultChanged;
    IReadOnlySet<string> AppliedDevices { get; }
    void RequestApply(IReadOnlyCollection<string> approvedDevices, IReadOnlyDictionary<string, double> brightnessByDevice);
    bool RestoreAll(TimeSpan timeout);
}

/// <summary>Which monitors are active right now.</summary>
internal interface IMonitorSource
{
    IReadOnlyList<MonitorInfo> GetActiveMonitors();
}

internal sealed class SystemMonitorSource : IMonitorSource
{
    public IReadOnlyList<MonitorInfo> GetActiveMonitors() => MonitorEnumerator.GetActiveMonitors();
}
