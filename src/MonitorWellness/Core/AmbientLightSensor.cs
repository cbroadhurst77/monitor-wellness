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
/// Most desktops — including this project's own 3-monitor dev machine — have no ambient light
/// sensor at all; ALS is mostly a laptop/tablet feature. GetDefault() returning null is the
/// expected, common case, not a failure, and this gracefully falls back to the existing fixed
/// schedule with zero user-visible difference — verified live on this machine.
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
            return LightSensor.GetDefault();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"AmbientLightSensor: LightSensor.GetDefault() failed: {ex.Message}");
            return null;
        }
    });

    /// <summary>True if this device reports having an ambient light sensor at all.</summary>
    public static bool IsAvailable => Sensor.Value is not null;

    /// <summary>Current illuminance in lux, or null if there's no sensor or the read failed. Never throws.</summary>
    public static double? TryGetCurrentLux()
    {
        try
        {
            return Sensor.Value?.GetCurrentReading()?.IlluminanceInLux;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"AmbientLightSensor.TryGetCurrentLux failed: {ex.Message}");
            return null;
        }
    }
}
