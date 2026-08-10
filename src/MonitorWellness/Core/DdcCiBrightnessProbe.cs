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
    internal struct PhysicalMonitorNative
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
    private static extern bool GetMonitorBrightness(IntPtr monitor, out uint minimumBrightness, out uint currentBrightness, out uint maximumBrightness);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool SetMonitorBrightness(IntPtr monitor, uint brightness);

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

    /// <summary>
    /// Opens a compatible monitor for the short, user-requested test only. The returned session
    /// restores its exact original brightness when disposed and is not used by normal scheduling.
    /// </summary>
    public static bool TryOpenTestSession(string deviceName, out DdcCiBrightnessTestSession? session, out string error)
    {
        session = null;
        error = "";
        Screen? screen = Screen.AllScreens.FirstOrDefault(candidate =>
            string.Equals(candidate.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
        if (screen is null)
        {
            error = "That display is no longer connected.";
            return false;
        }

        try
        {
            if (!TryOpenPhysicalMonitors(screen, out PhysicalMonitorNative[] physicalMonitors, out error))
                return false;

            var brightnessHandles = physicalMonitors
                .Where(physicalMonitor => GetMonitorCapabilities(physicalMonitor.Handle, out uint capabilities, out _)
                    && (capabilities & MonitorCapabilitiesBrightness) != 0)
                .Select(physicalMonitor => physicalMonitor.Handle)
                .ToArray();
            if (brightnessHandles.Length == 0)
            {
                _ = DestroyPhysicalMonitors((uint)physicalMonitors.Length, physicalMonitors);
                error = "This display does not report DDC/CI brightness support.";
                return false;
            }

            session = new DdcCiBrightnessTestSession(physicalMonitors, brightnessHandles);
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or SEHException)
        {
            error = "DDC/CI is unavailable on this Windows installation.";
            DebugLog.Write($"DDC/CI test session unavailable for {deviceName}: {ex.Message}");
            return false;
        }
    }

    private static DdcCiBrightnessCapability ProbeScreen(Screen screen)
    {
        if (!TryOpenPhysicalMonitors(screen, out PhysicalMonitorNative[] physicalMonitors, out string error))
            return new DdcCiBrightnessCapability(screen.DeviceName, screen.Primary, false, error);

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
            _ = DestroyPhysicalMonitors((uint)physicalMonitors.Length, physicalMonitors);
        }
    }

    private static bool TryOpenPhysicalMonitors(Screen screen, out PhysicalMonitorNative[] physicalMonitors, out string error)
    {
        physicalMonitors = Array.Empty<PhysicalMonitorNative>();
        error = "";
        var point = new PointNative
        {
            X = screen.Bounds.Left + screen.Bounds.Width / 2,
            Y = screen.Bounds.Top + screen.Bounds.Height / 2,
        };
        IntPtr displayMonitor = MonitorFromPoint(point, MonitorDefaultToNearest);
        if (displayMonitor == IntPtr.Zero)
        {
            error = "Windows did not return a monitor handle.";
            return false;
        }

        if (!GetNumberOfPhysicalMonitorsFromHMONITOR(displayMonitor, out uint monitorCount) || monitorCount == 0)
        {
            error = "This display does not expose physical-monitor controls.";
            return false;
        }

        physicalMonitors = new PhysicalMonitorNative[monitorCount];
        if (GetPhysicalMonitorsFromHMONITOR(displayMonitor, monitorCount, physicalMonitors))
            return true;

        error = "Windows could not open this display's physical-monitor controls.";
        physicalMonitors = Array.Empty<PhysicalMonitorNative>();
        return false;
    }

    internal static bool TryGetMonitorBrightness(IntPtr monitor, out uint minimum, out uint current, out uint maximum)
        => GetMonitorBrightness(monitor, out minimum, out current, out maximum);

    internal static bool TrySetMonitorBrightness(IntPtr monitor, uint brightness) => SetMonitorBrightness(monitor, brightness);

    internal static void DestroyPhysicalMonitorHandles(PhysicalMonitorNative[] physicalMonitors)
        => _ = DestroyPhysicalMonitors((uint)physicalMonitors.Length, physicalMonitors);
}

/// <summary>Read-only per-display result returned by <see cref="DdcCiBrightnessProbe"/>.</summary>
public sealed record DdcCiBrightnessCapability(string DeviceName, bool IsPrimary, bool IsSupported, string Detail);

/// <summary>Owns DDC/CI monitor handles for one short, reversible user test.</summary>
public sealed class DdcCiBrightnessTestSession : IDisposable
{
    private sealed record OriginalBrightness(IntPtr Handle, uint Value, uint Minimum, uint Maximum);

    private readonly DdcCiBrightnessProbe.PhysicalMonitorNative[] _physicalMonitors;
    private readonly IntPtr[] _brightnessHandles;
    private IReadOnlyList<OriginalBrightness>? _originalBrightness;
    private double? _lastAppliedNormalizedBrightness;
    private bool _disposed;

    internal DdcCiBrightnessTestSession(DdcCiBrightnessProbe.PhysicalMonitorNative[] physicalMonitors, IntPtr[] brightnessHandles)
    {
        _physicalMonitors = physicalMonitors;
        _brightnessHandles = brightnessHandles;
    }

    /// <summary>Applies a small dim-only change and records exact values for automatic restore.</summary>
    public bool TryDimForTest(out string error)
    {
        error = "";
        var originalBrightness = new List<OriginalBrightness>();
        foreach (IntPtr handle in _brightnessHandles)
        {
            if (!DdcCiBrightnessProbe.TryGetMonitorBrightness(handle, out uint minimum, out uint current, out uint maximum) || minimum > maximum)
            {
                error = "Windows could not read this monitor's current brightness.";
                return false;
            }

            double normalizedCurrent = maximum == minimum ? 1.0 : (double)(current - minimum) / (maximum - minimum);
            if (!HardwareBrightnessMath.TryGetSafeTestBrightness(normalizedCurrent, out _))
            {
                error = "This monitor is already too dim for a safe hardware-brightness test. Increase its physical brightness first.";
                return false;
            }

            originalBrightness.Add(new OriginalBrightness(handle, current, minimum, maximum));
        }

        _originalBrightness = originalBrightness;
        foreach (OriginalBrightness original in originalBrightness)
        {
            double normalizedCurrent = original.Maximum == original.Minimum ? 1.0 : (double)(original.Value - original.Minimum) / (original.Maximum - original.Minimum);
            HardwareBrightnessMath.TryGetSafeTestBrightness(normalizedCurrent, out double testBrightness);
            if (!DdcCiBrightnessProbe.TrySetMonitorBrightness(original.Handle, HardwareBrightnessMath.ToNativeBrightness(testBrightness, original.Minimum, original.Maximum)))
            {
                Restore();
                error = "Windows could not apply the temporary hardware brightness test.";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Applies a normalized value while preserving the exact original physical brightness for
    /// restoration when this session is disposed.
    /// </summary>
    public bool TryApplyNormalizedBrightness(double normalizedBrightness, out string error)
    {
        error = "";
        normalizedBrightness = Math.Clamp(normalizedBrightness, 0, 1);
        if (_lastAppliedNormalizedBrightness.HasValue
            && Math.Abs(_lastAppliedNormalizedBrightness.Value - normalizedBrightness) < 0.002)
        {
            return true;
        }

        if (_originalBrightness is null)
        {
            var captured = new List<OriginalBrightness>();
            foreach (IntPtr handle in _brightnessHandles)
            {
                if (!DdcCiBrightnessProbe.TryGetMonitorBrightness(handle, out uint minimum, out uint current, out uint maximum) || minimum > maximum)
                {
                    error = "Windows could not read this monitor's current brightness.";
                    return false;
                }
                captured.Add(new OriginalBrightness(handle, current, minimum, maximum));
            }
            _originalBrightness = captured;
        }

        foreach (OriginalBrightness original in _originalBrightness)
        {
            uint target = HardwareBrightnessMath.ToNativeBrightness(normalizedBrightness, original.Minimum, original.Maximum);
            if (!DdcCiBrightnessProbe.TrySetMonitorBrightness(original.Handle, target))
            {
                Restore();
                error = "Windows could not apply the hardware brightness target.";
                return false;
            }
        }

        _lastAppliedNormalizedBrightness = normalizedBrightness;

        return true;
    }

    /// <summary>Restores the exact physical brightness values captured before the test.</summary>
    public void Restore()
    {
        if (_originalBrightness is null)
            return;

        foreach (OriginalBrightness original in _originalBrightness)
            _ = DdcCiBrightnessProbe.TrySetMonitorBrightness(original.Handle, original.Value);
        _originalBrightness = null;
        _lastAppliedNormalizedBrightness = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Restore();
        DdcCiBrightnessProbe.DestroyPhysicalMonitorHandles(_physicalMonitors);
    }
}
