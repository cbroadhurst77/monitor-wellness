using System.Runtime.InteropServices;

namespace MonitorWellness.Core;

public sealed record MonitorInfo(string DeviceName, string DeviceString, bool IsPrimary);

/// <summary>
/// Enumerates active display devices via the Win32 EnumDisplayDevices API. This is the
/// device-name source that GammaRampController uses to open a per-monitor device context.
/// </summary>
public static class MonitorEnumerator
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    private const int DisplayDeviceAttachedToDesktop = 0x1;
    private const int DisplayDevicePrimaryDevice = 0x4;

    /// <summary>
    /// Returns all display devices currently attached to the desktop. Devices that exist
    /// but are disabled/disconnected are excluded, since gamma ramp calls against them fail.
    /// </summary>
    public static List<MonitorInfo> GetActiveMonitors()
    {
        var monitors = new List<MonitorInfo>();
        uint deviceIndex = 0;

        while (true)
        {
            var device = new DISPLAY_DEVICE();
            device.cb = Marshal.SizeOf(device);

            if (!EnumDisplayDevices(null, deviceIndex, ref device, 0))
                break;

            deviceIndex++;

            bool attached = (device.StateFlags & DisplayDeviceAttachedToDesktop) != 0;
            if (!attached)
                continue;

            bool isPrimary = (device.StateFlags & DisplayDevicePrimaryDevice) != 0;
            monitors.Add(new MonitorInfo(device.DeviceName, device.DeviceString, isPrimary));
        }

        return monitors;
    }
}
