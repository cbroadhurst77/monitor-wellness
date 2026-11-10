namespace MonitorWellness.Core;

/// <summary>
/// Identifies display paths where driver-mediated colour and physical-brightness commands are
/// less predictable than the normal desktop compositor. This is deliberately conservative:
/// it recognizes only explicit Windows/driver wording and recommends the existing overlay
/// fallback instead of guessing about ordinary monitors.
/// </summary>
public static class DisplayCompatibilityAdvisor
{
    private static readonly string[] RemoteOrVirtualIndicators =
    {
        "remote display",
        "rdp",
        "virtual display",
        "virtual monitor",
        "indirect display",
        "mirage",
        "spacedesk",
        "parsec",
    };

    private static readonly string[] UsbOrIndirectIndicators =
    {
        "displaylink",
        "usb display",
    };

    /// <summary>
    /// Returns an explanation when a display should use the compositor overlay only. The
    /// caller controls whether this recommendation is enforced; it is an accessibility and
    /// reliability preference, not a claim that the display is defective.
    /// </summary>
    public static bool TryGetOverlayOnlyReason(MonitorInfo monitor, out string reason)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        // Colour-ramp (gamma) changes are not reliable while Windows runs a display in HDR, and HDR
        // can be switched on at any time (Win+Alt+B), so this is judged on every look at the display.
        if (monitor.IsHdr)
        {
            reason = "Windows HDR is turned on for this display, where colour-ramp changes are not reliable.";
            return true;
        }

        string description = $"{monitor.DeviceString} {monitor.HardwareDeviceId}";
        if (ContainsAny(description, RemoteOrVirtualIndicators))
        {
            reason = "Windows identifies this as a remote or virtual display path.";
            return true;
        }

        if (ContainsAny(description, UsbOrIndirectIndicators))
        {
            reason = "Windows identifies this as a USB or indirect display path.";
            return true;
        }

        reason = "";
        return false;
    }

    private static bool ContainsAny(string value, IEnumerable<string> indicators) =>
        indicators.Any(indicator => value.Contains(indicator, StringComparison.OrdinalIgnoreCase));
}
