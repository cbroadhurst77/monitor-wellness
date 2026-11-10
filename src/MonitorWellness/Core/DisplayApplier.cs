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
    }

    /// <summary>
    /// Returns every controlled display to its native state: physical brightness back to what it
    /// was, overlay cleared, colour ramps back to the baseline captured at start. Used by
    /// Emergency Restore, the full-screen guard and "restore native display" app rules.
    /// </summary>
    public void RestoreNative()
    {
        _hardware.RestoreAll();
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
            .Where(monitor => !_settings.ExcludedMonitors.Contains(monitor.DeviceName)
                && (!_settings.PreferOverlayOnlyOnCompatibilityDisplays
                    || !DisplayCompatibilityAdvisor.TryGetOverlayOnlyReason(monitor, out _))
                && HardwareBrightnessSafety.IsApproved(_settings, monitor))
            .Select(monitor => monitor.DeviceName)
            .ToList();
        HardwareBrightnessApplicationResult hardwareResult = _hardware.ApplyApprovedBrightness(approvedHardwareDevices, brightnessByDevice);
        PersistHardwareBrightnessQuarantines(activeMonitors, hardwareResult);

        // A display whose physical brightness was set needs no overlay dimming on top of it.
        var overlayBrightnessByDevice = brightnessByDevice.ToDictionary(
            entry => entry.Key,
            entry => hardwareResult.AppliedDeviceNames.Contains(entry.Key) ? 1.0 : entry.Value);
        _overlay.ApplyDim(overlayBrightnessByDevice, dimColor);
    }

    private void ApplyColor(GammaRampController controller, int kelvin, Dictionary<string, MonitorInfo> activeMonitorsByDeviceName)
    {
        if (_settings.ExcludedMonitors.Contains(controller.DeviceName))
            return;

        if (_settings.PreferOverlayOnlyOnCompatibilityDisplays
            && activeMonitorsByDeviceName.TryGetValue(controller.DeviceName, out MonitorInfo? monitor)
            && DisplayCompatibilityAdvisor.TryGetOverlayOnlyReason(monitor, out string compatibilityReason))
        {
            // A remote/virtual/indirect surface can be backed by a transport or driver
            // which handles gamma state unpredictably. Restore a previously applied ramp,
            // then leave the normal per-monitor overlay in charge of dimming.
            if (_compatibilityFallbackDevices.Add(controller.DeviceName))
            {
                controller.RestoreBaseline();
                DebugLog.Write($"Compatibility fallback active for {controller.DeviceName}: {compatibilityReason}");
                CompatibilityFallbackStarted?.Invoke(controller.DeviceName, compatibilityReason, monitor.IsHdr);
            }
            return;
        }

        _compatibilityFallbackDevices.Remove(controller.DeviceName);

        if (_settings.ColorExcludedMonitors.Contains(controller.DeviceName))
        {
            // Color-accurate reference monitor: stays at native color, but still
            // participates in the brightness schedule.
            controller.RestoreBaseline();
            return;
        }

        int offset = _settings.MonitorKelvinOffset.TryGetValue(controller.DeviceName, out var off) ? off : 0;
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
