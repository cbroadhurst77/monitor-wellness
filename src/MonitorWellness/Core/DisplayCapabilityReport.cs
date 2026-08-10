using System.Globalization;
using System.Text;

namespace MonitorWellness.Core;

/// <summary>
/// A read-only explanation of how safely Monitor Wellness can control each active desktop
/// display. It deliberately reports unknown capability as unknown rather than inferring that a
/// monitor is flicker-free, colour accurate, or safe for a particular medical condition.
/// </summary>
public sealed record DisplayCapability(
    string DeviceName,
    string DisplayName,
    bool IsPrimary,
    string HardwareIdentity,
    bool DdcCiBrightnessAvailable,
    string RecommendedBrightnessBackend,
    string SafetyStatus,
    string Detail);

public sealed record DisplayCapabilityReport(
    bool IsHdrEnabled,
    bool IsAmbientLightSensorAvailable,
    IReadOnlyList<DisplayCapability> Displays,
    VisualStabilitySnapshot VisualStability)
{
    public string ToPlainText()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Monitor Wellness — Display Capability Passport");
        builder.AppendLine(CultureInfo.InvariantCulture, $"HDR enabled: {IsHdrEnabled}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Ambient-light sensor available: {IsAmbientLightSensorAvailable}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Flicker Guard this session: {VisualStability.ForegroundDisplayWritesAvoided} unrelated foreground display write(s) avoided; {VisualStability.CoalescedTopologyRefreshes} coalesced refresh(es) from {VisualStability.DisplayTopologySignals} display-topology signal(s).");
        builder.AppendLine();

        foreach (DisplayCapability display in Displays)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"{display.DeviceName} — {display.DisplayName}{(display.IsPrimary ? " (primary)" : "")}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"  Physical identity: {display.HardwareIdentity}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"  DDC/CI brightness: {(display.DdcCiBrightnessAvailable ? "available for explicit testing" : "not available")}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"  Recommended brightness backend: {display.RecommendedBrightnessBackend}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"  Safety status: {display.SafetyStatus}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"  {display.Detail}");
            builder.AppendLine();
        }

        builder.AppendLine("This report does not measure panel PWM, temporal dithering, spectral output, or medical suitability. It records only what Windows and the monitor expose to this app.");
        return builder.ToString();
    }
}

public static class DisplayCapabilityReporter
{
    public static DisplayCapabilityReport Create(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        IReadOnlyList<MonitorInfo> monitors = MonitorEnumerator.GetActiveMonitors();
        IReadOnlyDictionary<string, DdcCiBrightnessCapability> ddcCapabilities = DdcCiBrightnessProbe.GetCapabilities()
            .ToDictionary(capability => capability.DeviceName, StringComparer.OrdinalIgnoreCase);

        var displays = monitors.Select(monitor => CreateDisplayCapability(settings, monitor, ddcCapabilities)).ToList();
        return new DisplayCapabilityReport(HdrDetector.IsAnyDisplayHdrEnabled(), AmbientLightSensor.IsAvailable, displays, VisualStabilityDiagnostics.GetSnapshot());
    }

    internal static DisplayCapability CreateDisplayCapability(
        AppSettings settings,
        MonitorInfo monitor,
        IReadOnlyDictionary<string, DdcCiBrightnessCapability> ddcCapabilities)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(ddcCapabilities);

        bool hasIdentity = HardwareBrightnessSafety.GetKey(monitor) is not null;
        bool quarantined = HardwareBrightnessSafety.IsQuarantined(settings, monitor, out string quarantineReason);
        bool approved = HardwareBrightnessSafety.IsApproved(settings, monitor);
        bool ddcAvailable = ddcCapabilities.TryGetValue(monitor.DeviceName, out DdcCiBrightnessCapability? ddcCapability)
            && ddcCapability.IsSupported;
        string compatibilityReason = "";
        bool compatibilityFallback = settings.PreferOverlayOnlyOnCompatibilityDisplays
            && DisplayCompatibilityAdvisor.TryGetOverlayOnlyReason(monitor, out compatibilityReason);

        string identity = hasIdentity ? "Stable physical monitor path available" : "Unavailable or ambiguous — automatic hardware brightness is disabled";
        string backend;
        string status;
        string detail;
        if (compatibilityFallback)
        {
            backend = "Stable overlay fallback";
            status = "Compatibility fallback active";
            detail = $"{compatibilityReason} Gamma-ramp and physical-brightness commands are held back while compatibility mode is enabled.";
        }
        else if (quarantined)
        {
            backend = "Stable overlay fallback";
            status = "Hardware brightness quarantined";
            detail = quarantineReason;
        }
        else if (approved && ddcAvailable)
        {
            backend = "Verified hardware brightness";
            status = "Hardware brightness approved";
            detail = "The monitor passed a reversible test. Its original physical brightness is restored when Monitor Wellness stops controlling it.";
        }
        else if (ddcAvailable && hasIdentity)
        {
            backend = "Stable overlay fallback";
            status = "Hardware brightness available but not approved";
            detail = "Run the explicit reversible hardware-brightness test before enabling scheduled DDC/CI control.";
        }
        else
        {
            backend = "Stable overlay fallback";
            status = "Overlay only";
            detail = ddcCapability?.Detail ?? "Windows did not expose DDC/CI brightness control for this display.";
        }

        return new DisplayCapability(
            monitor.DeviceName,
            monitor.DeviceString,
            monitor.IsPrimary,
            identity,
            ddcAvailable,
            backend,
            status,
            detail);
    }
}
