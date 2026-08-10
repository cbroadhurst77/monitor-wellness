using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MonitorWellness.Core;

/// <summary>
/// Read-only DDC/CI brightness capability probe. This deliberately does not change a monitor:
/// capability detection is the prerequisite for a later explicit test-and-restore workflow.
/// </summary>
public static class DdcCiBrightnessProbe
{
    private const uint MonitorDefaultToNearest = 2;
    private const uint MonitorCapabilitiesBrightness = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct PointNative
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PhysicalMonitorNative
    {
        public IntPtr Handle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(PointNative point, uint flags);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint monitorCount);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint monitorCount, [Out] PhysicalMonitorNative[] physicalMonitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetMonitorCapabilities(IntPtr monitor, out uint capabilities, out uint supportedColorTemperatures);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool DestroyPhysicalMonitors(uint monitorCount, [In] PhysicalMonitorNative[] physicalMonitors);

    public static IReadOnlyList<DdcCiBrightnessCapability> GetCapabilities()
    {
        if (!OperatingSystem.IsWindows())
            return Array.Empty<DdcCiBrightnessCapability>();

        var capabilities = new List<DdcCiBrightnessCapability>();
        foreach (Screen screen in Screen.AllScreens)
        {
            try
            {
                capabilities.Add(ProbeScreen(screen));
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or SEHException)
            {
                DebugLog.Write($"DDC/CI capability probe unavailable for {screen.DeviceName}: {ex.Message}");
                capabilities.Add(new DdcCiBrightnessCapability(screen.DeviceName, screen.Primary, false, "DDC/CI is unavailable on this Windows installation."));
            }
        }

        return capabilities;
    }

    private static DdcCiBrightnessCapability ProbeScreen(Screen screen)
    {
        var point = new PointNative
        {
            X = screen.Bounds.Left + screen.Bounds.Width / 2,
            Y = screen.Bounds.Top + screen.Bounds.Height / 2,
        };
        IntPtr displayMonitor = MonitorFromPoint(point, MonitorDefaultToNearest);
        if (displayMonitor == IntPtr.Zero)
            return new DdcCiBrightnessCapability(screen.DeviceName, screen.Primary, false, "Windows did not return a monitor handle.");

        if (!GetNumberOfPhysicalMonitorsFromHMONITOR(displayMonitor, out uint monitorCount) || monitorCount == 0)
            return new DdcCiBrightnessCapability(screen.DeviceName, screen.Primary, false, "This display does not expose physical-monitor controls.");

        var physicalMonitors = new PhysicalMonitorNative[monitorCount];
        if (!GetPhysicalMonitorsFromHMONITOR(displayMonitor, monitorCount, physicalMonitors))
            return new DdcCiBrightnessCapability(screen.DeviceName, screen.Primary, false, "Windows could not open this display's physical-monitor controls.");

        try
        {
            bool supportsBrightness = physicalMonitors.Any(physicalMonitor =>
                GetMonitorCapabilities(physicalMonitor.Handle, out uint monitorCapabilities, out _)
                && (monitorCapabilities & MonitorCapabilitiesBrightness) != 0);

            return supportsBrightness
                ? new DdcCiBrightnessCapability(screen.DeviceName, screen.Primary, true, "DDC/CI brightness control is available for testing.")
                : new DdcCiBrightnessCapability(screen.DeviceName, screen.Primary, false, "This display does not report DDC/CI brightness support.");
        }
        finally
        {
            _ = DestroyPhysicalMonitors(monitorCount, physicalMonitors);
        }
    }
}

/// <summary>Read-only per-display result returned by <see cref="DdcCiBrightnessProbe"/>.</summary>
public sealed record DdcCiBrightnessCapability(string DeviceName, bool IsPrimary, bool IsSupported, string Detail);
