using System.IO;
using Color = System.Windows.Media.Color;

namespace MonitorWellness.Core;

/// <summary>
/// Writes one computed schedule target to the displays: colour temperature through the gamma
/// ramps, brightness through approved hardware (DDC/CI) with the overlay as fallback, and the
/// dim overlay itself. Extracted from App.RunScheduleTick so that method only decides *what*
/// should be shown and this class owns *how* it reaches the screens, including the
/// compatibility-display fallback and the hardware-brightness quarantine bookkeeping.
/// </summary>
public sealed class DisplayApplier
{
    private readonly AppSettings _settings;
    private readonly GammaControllerManager _gamma;
    private readonly OverlayController _overlay;
    private readonly HardwareBrightnessControllerManager _hardware;
    private readonly HashSet<string> _compatibilityFallbackDevices = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised when a display first switches to overlay-only: (device name, reason, whether the reason is HDR).</summary>
    public event Action<string, string, bool>? CompatibilityFallbackStarted;

    public DisplayApplier(
        AppSettings settings,
        GammaControllerManager gamma,
        OverlayController overlay,
        HardwareBrightnessControllerManager hardware)
    {
        _settings = settings;
        _gamma = gamma;
        _overlay = overlay;
        _hardware = hardware;
        _hardware.ResultChanged += OnHardwareResultChanged;
    }

    /// <summary>Raised (on the UI thread) when hardware brightness took over a display, gave one back, or failed: the schedule should be re-applied so the overlay matches.</summary>
    public event Action? HardwareBrightnessChanged;

    private void OnHardwareResultChanged(HardwareBrightnessApplicationResult result)
    {
        try
        {
            PersistHardwareBrightnessQuarantines(MonitorEnumerator.GetActiveMonitors(), result);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"Handling the hardware brightness result failed: {ex}");
        }
        HardwareBrightnessChanged?.Invoke();
    }

    /// <summary>
    /// Returns every controlled display to its native state: physical brightness back to what it
    /// was, overlay cleared, colour ramps back to the baseline captured at start. Used by
    /// Emergency Restore, the full-screen guard and "restore native display" app rules.
    /// </summary>
    public void RestoreNative()
    {
        _hardware.RestoreAll(TimeSpan.FromSeconds(2));
        _overlay.Clear();
        _gamma.RestoreAllToBaseline();
    }

    /// <summary>Applies the colour temperature and brightness schedule target to every active display.</summary>
    public void Apply(int kelvin, IReadOnlyDictionary<string, double> brightnessByDevice, Color dimColor)
    {
        IReadOnlyList<MonitorInfo> activeMonitors = MonitorEnumerator.GetActiveMonitors();
        var activeMonitorsByDeviceName = activeMonitors.ToDictionary(monitor => monitor.DeviceName, StringComparer.OrdinalIgnoreCase);
        foreach (var controller in _gamma.Controllers)
            ApplyColor(controller, kelvin, activeMonitorsByDeviceName);

        var approvedHardwareDevices = activeMonitors
            .Where(monitor => !MonitorKeys.Contains(_settings.ExcludedMonitors, monitor)
                && (!_settings.PreferOverlayOnlyOnCompatibilityDisplays
                    || !DisplayCompatibilityAdvisor.TryGetOverlayOnlyReason(monitor, out _))
                && HardwareBrightnessSafety.IsApproved(_settings, monitor))
            .Select(monitor => monitor.DeviceName)
            .ToList();
        // Hardware (DDC/CI) brightness is set on a worker thread and never waited for here. A
        // display whose physical brightness is currently set by us needs no overlay dimming on top
        // of it; until the first result is in (or while the worker is stuck) the overlay dims it,
        // and HardwareBrightnessChanged re-applies the schedule once hardware takes over.
        _hardware.RequestApply(approvedHardwareDevices, brightnessByDevice);
        IReadOnlySet<string> hardwareApplied = _hardware.AppliedDevices;
        var overlayBrightnessByDevice = brightnessByDevice.ToDictionary(
            entry => entry.Key,
            entry => hardwareApplied.Contains(entry.Key) ? 1.0 : entry.Value);
        _overlay.ApplyDim(overlayBrightnessByDevice, dimColor);
    }

    private void ApplyColor(GammaRampController controller, int kelvin, Dictionary<string, MonitorInfo> activeMonitorsByDeviceName)
    {
        activeMonitorsByDeviceName.TryGetValue(controller.DeviceName, out MonitorInfo? knownMonitor);
        MonitorInfo identity = knownMonitor ?? MonitorKeys.Unidentified(controller.DeviceName);

        if (MonitorKeys.Contains(_settings.ExcludedMonitors, identity))
        {
            // Excluded after it was already tinted (or tinted by migraine mode): hand it back.
            controller.RestoreBaselineIfModified();
            return;
        }

        if (_settings.PreferOverlayOnlyOnCompatibilityDisplays
            && knownMonitor is MonitorInfo monitor
            && DisplayCompatibilityAdvisor.TryGetOverlayOnlyReason(monitor, out string compatibilityReason))
        {
            // A remote/virtual/indirect surface can be backed by a transport or driver
            // which handles gamma state unpredictably. Restore a previously applied ramp,
            // then leave the normal per-monitor overlay in charge of dimming.
            bool firstTime = _compatibilityFallbackDevices.Add(controller.DeviceName);
            // Every tick, not just the first: migraine mode writes ramps to every display, and
            // nothing else would take that colour off this one afterwards.
            controller.RestoreBaselineIfModified();
            if (firstTime)
            {
                DebugLog.Write($"Compatibility fallback active for {controller.DeviceName}: {compatibilityReason}");
                CompatibilityFallbackStarted?.Invoke(controller.DeviceName, compatibilityReason, monitor.IsHdr);
            }
            return;
        }

        _compatibilityFallbackDevices.Remove(controller.DeviceName);

        if (MonitorKeys.Contains(_settings.ColorExcludedMonitors, identity))
        {
            // Color-accurate reference monitor: stays at native color, but still
            // participates in the brightness schedule.
            controller.RestoreBaselineIfModified();
            return;
        }

        int offset = MonitorKeys.TryGetValue(_settings.MonitorKelvinOffset, identity, out int off) ? off : 0;
        int targetKelvin = kelvin + offset;
        if (!controller.ApplyColorTemperature(targetKelvin))
        {
            // Two different causes look identical to the caller: a Kelvin value below this
            // hardware's safe ramp floor (see the Week 1 finding), and a transient failure of an
            // otherwise safe value. The log says which, with the Win32 error code.
            DebugLog.Write($"ApplyColorTemperature rejected for {controller.DeviceName}: {GammaDiagnostics.DescribeRejection(targetKelvin, controller.LastWin32Error)}.");
        }
    }

    private void PersistHardwareBrightnessQuarantines(
        IReadOnlyCollection<MonitorInfo> activeMonitors,
        HardwareBrightnessApplicationResult hardwareResult)
    {
        bool changed = false;

        // A monitor that was quarantined but is now working again (its bounded retry
        // succeeded) is cleared, so one transient failure doesn't disable hardware dimming for good.
        foreach (MonitorInfo recovered in activeMonitors.Where(monitor =>
            hardwareResult.AppliedDeviceNames.Contains(monitor.DeviceName)))
        {
            if (HardwareBrightnessSafety.ClearQuarantineAfterRecovery(_settings, recovered))
            {
                DebugLog.Write($"Hardware brightness quarantine cleared for {recovered.DeviceName}: the automatic retry succeeded.");
                changed = true;
            }
        }

        foreach (var (deviceName, reason) in hardwareResult.FailuresByDeviceName)
        {
            MonitorInfo? monitor = activeMonitors.FirstOrDefault(candidate =>
                string.Equals(candidate.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
            if (monitor is not null)
            {
                changed |= HardwareBrightnessSafety.Quarantine(
                    _settings,
                    monitor,
                    $"Hardware brightness was disabled after a failed command: {reason}");
            }
        }

        if (!changed)
            return;

        try
        {
            SettingsStore.Save(_settings);
            DebugLog.Write("Hardware brightness safety state persisted; overlay fallback remains active where needed.");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            DebugLog.Write($"Couldn't persist hardware brightness safety state: {ex}");
        }
    }
}
