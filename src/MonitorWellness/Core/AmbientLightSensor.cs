using Windows.Devices.Sensors;

namespace MonitorWellness.Core;

/// <summary>
/// Wraps Windows.Devices.Sensors.LightSensor for the opt-in "match ambient light" brightness
/// feature — see TECHNICAL_UX_REVIEW.md §1.1. Requires the net8.0-windows10.0.19041.0 TFM (see
/// MonitorWellness.csproj) for the WinRT projection. Unlike Windows.Graphics.Display's
/// DisplayInformation (which needs a CoreWindow/UWP view and would have required the Windows
/// App SDK — too heavy a dependency for this app's portable, no-runtime-install story), the
/// device/sensor APIs work directly from a classic desktop app with no such requirement.
///
/// Many desktops have no ambient light sensor at all -- ALS is mostly a laptop/tablet feature --
/// but that's not universal: confirmed live that a machine with an Intel Sensor Hub (HID
/// VID_8087, seen in this project's own dev environment) reports a real, plausible-looking lux
/// reading through this exact API, not just laptops with a dedicated ALS chip. Don't assume
/// GetDefault() returning null based on one machine's hardware -- IsAvailable and the first lux
/// reading are logged once below specifically so this doesn't need to be re-verified by
/// guesswork again later, on this machine or any other.
/// </summary>
public static class AmbientLightSensor
{
    // A deliberately broad catch, unlike this codebase's other P/Invoke wrappers (which catch
    // specific, well-documented Win32 error types): WinRT/COM interop exceptions surfacing from
    // a rarely-exercised hardware sensor path are far less predictable across the range of
    // machines this could run on, and this feature is purely optional — nothing justifies
    // letting an unanticipated exception type here crash the whole app.
    private static readonly Lazy<LightSensor?> Sensor = new(() =>
    {
        try
        {
            var sensor = LightSensor.GetDefault();
            DebugLog.Write(sensor is not null
                ? $"AmbientLightSensor: LightSensor.GetDefault() found a sensor (DeviceId={sensor.DeviceId})."
                : "AmbientLightSensor: LightSensor.GetDefault() returned null — no sensor on this device.");
            return sensor;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"AmbientLightSensor: LightSensor.GetDefault() failed: {ex.Message}");
            return null;
        }
    });

    private static bool _loggedFirstLuxRead;

    /// <summary>True if this device reports having an ambient light sensor at all.</summary>
    public static bool IsAvailable => Sensor.Value is not null;

    /// <summary>Current illuminance in lux, or null if there's no sensor or the read failed. Never throws.</summary>
    public static double? TryGetCurrentLux()
    {
        try
        {
            double? lux = Sensor.Value?.GetCurrentReading()?.IlluminanceInLux;
            if (!_loggedFirstLuxRead)
            {
                _loggedFirstLuxRead = true;
                DebugLog.Write(lux.HasValue
                    ? $"AmbientLightSensor: first reading = {lux.Value:F1} lux."
                    : "AmbientLightSensor: first reading returned no value (sensor present but GetCurrentReading()/IlluminanceInLux was null).");
            }
            return lux;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"AmbientLightSensor.TryGetCurrentLux failed: {ex.Message}");
            return null;
        }
    }
}
