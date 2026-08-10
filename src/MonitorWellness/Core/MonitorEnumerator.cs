using System.Runtime.InteropServices;

namespace MonitorWellness.Core;

/// <summary>
/// Describes a display attached to the Windows desktop. <see cref="HardwareDeviceId"/> is a
/// physical monitor device-interface path, which is more stable across dock/replug cycles than
/// the transient <c>\\.\DISPLAYn</c> path used to address the desktop surface.
/// </summary>
public sealed record MonitorInfo(string DeviceName, string DeviceString, bool IsPrimary, string HardwareDeviceId);

/// <summary>
/// Enumerates active desktop displays and resolves each one through Windows' CCD display
/// topology. The CCD source name maps directly to <c>\\.\DISPLAYn</c>; its target name supplies
/// the corresponding physical monitor path. This avoids incorrectly assigning child index zero
/// from EnumDisplayDevices to every surface on hybrid-GPU or docked systems.
/// </summary>
public static class MonitorEnumerator
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public uint RefreshRateNumerator;
        public uint RefreshRateDenominator;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathInfo
    {
        public DisplayConfigPathSourceInfo SourceInfo;
        public DisplayConfigPathTargetInfo TargetInfo;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigModeInfo
    {
        public uint InfoType;
        public uint Id;
        public Luid AdapterId;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)]
        public byte[] ModeUnion;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigDeviceInfoHeader
    {
        public uint Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigSourceDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigTargetDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint Flags;
        public uint OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string MonitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string MonitorDevicePath;
    }

    private const int DisplayDeviceAttachedToDesktop = 0x1;
    private const int DisplayDevicePrimaryDevice = 0x4;
    private const uint QueryDisplayConfigOnlyActivePaths = 0x00000002;
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;
    private const uint DisplayConfigDeviceInfoGetSourceName = 1;
    private const uint DisplayConfigDeviceInfoGetTargetName = 2;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? deviceName, uint deviceIndex, ref DisplayDevice displayDevice, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint pathCount,
        [Out] DisplayConfigPathInfo[] pathArray,
        ref uint modeCount,
        [Out] DisplayConfigModeInfo[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int DisplayConfigGetSourceDeviceInfo(ref DisplayConfigSourceDeviceName requestPacket);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int DisplayConfigGetTargetDeviceInfo(ref DisplayConfigTargetDeviceName requestPacket);

    /// <summary>Returns active desktop surfaces. Unsafe or ambiguous identities are empty.</summary>
    public static List<MonitorInfo> GetActiveMonitors()
    {
        IReadOnlyDictionary<string, string> hardwareIdsByDesktopDevice = GetHardwareIdsByDesktopDevice();
        var monitors = new List<MonitorInfo>();
        uint deviceIndex = 0;

        while (true)
        {
            var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null, deviceIndex, ref device, 0))
                break;

            deviceIndex++;
            if ((device.StateFlags & DisplayDeviceAttachedToDesktop) == 0)
                continue;

            hardwareIdsByDesktopDevice.TryGetValue(device.DeviceName, out string? hardwareDeviceId);
            monitors.Add(new MonitorInfo(
                device.DeviceName,
                device.DeviceString,
                (device.StateFlags & DisplayDevicePrimaryDevice) != 0,
                hardwareDeviceId ?? string.Empty));
        }

        return RemoveAmbiguousHardwareIdentities(monitors);
    }

    /// <summary>
    /// Windows can report the same target identity for more than one desktop surface. That is
    /// not a usable physical identity: an approval granted after testing one display could
    /// otherwise enable DDC/CI writes to a different one. Deliberately fail closed instead.
    /// </summary>
    internal static List<MonitorInfo> RemoveAmbiguousHardwareIdentities(IEnumerable<MonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        List<MonitorInfo> materialized = monitors.ToList();
        var ambiguousIds = materialized
            .Where(monitor => !string.IsNullOrWhiteSpace(monitor.HardwareDeviceId))
            .GroupBy(monitor => monitor.HardwareDeviceId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return materialized
            .Select(monitor => !string.IsNullOrWhiteSpace(monitor.HardwareDeviceId)
                && ambiguousIds.Contains(monitor.HardwareDeviceId.Trim())
                ? monitor with { HardwareDeviceId = string.Empty }
                : monitor)
            .ToList();
    }

    internal static IReadOnlyDictionary<string, string> BuildHardwareIdsByDesktopDevice(IEnumerable<DisplayTopologyPath> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var targetsByDesktopDevice = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (DisplayTopologyPath path in paths)
        {
            if (string.IsNullOrWhiteSpace(path.DesktopDeviceName) || string.IsNullOrWhiteSpace(path.MonitorDevicePath))
                continue;

            if (!targetsByDesktopDevice.TryGetValue(path.DesktopDeviceName, out HashSet<string>? targets))
            {
                targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                targetsByDesktopDevice[path.DesktopDeviceName] = targets;
            }
            targets.Add(path.MonitorDevicePath.Trim());
        }

        // Cloned desktop sources have more than one target and therefore cannot safely be
        // given one physical approval key.
        return targetsByDesktopDevice
            .Where(pair => pair.Value.Count == 1)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Single(), StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string> GetHardwareIdsByDesktopDevice()
    {
        try
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (GetDisplayConfigBufferSizes(QueryDisplayConfigOnlyActivePaths, out uint pathCount, out uint modeCount) != ErrorSuccess || pathCount == 0)
                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                var paths = new DisplayConfigPathInfo[pathCount];
                var modes = new DisplayConfigModeInfo[modeCount];
                for (int index = 0; index < modes.Length; index++)
                    modes[index].ModeUnion = new byte[48];

                int result = QueryDisplayConfig(QueryDisplayConfigOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
                if (result == ErrorInsufficientBuffer)
                    continue;
                if (result != ErrorSuccess)
                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                return BuildHardwareIdsByDesktopDevice(paths.Take((int)pathCount));
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or ArgumentException or SEHException)
        {
            DebugLog.Write($"MonitorEnumerator display topology lookup failed: {ex.Message}");
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string> BuildHardwareIdsByDesktopDevice(IEnumerable<DisplayConfigPathInfo> paths)
    {
        var topologyPaths = new List<DisplayTopologyPath>();
        foreach (DisplayConfigPathInfo path in paths)
        {
            var sourceName = new DisplayConfigSourceDeviceName
            {
                Header = new DisplayConfigDeviceInfoHeader
                {
                    Type = DisplayConfigDeviceInfoGetSourceName,
                    Size = (uint)Marshal.SizeOf<DisplayConfigSourceDeviceName>(),
                    AdapterId = path.SourceInfo.AdapterId,
                    Id = path.SourceInfo.Id,
                },
            };
            var targetName = new DisplayConfigTargetDeviceName
            {
                Header = new DisplayConfigDeviceInfoHeader
                {
                    Type = DisplayConfigDeviceInfoGetTargetName,
                    Size = (uint)Marshal.SizeOf<DisplayConfigTargetDeviceName>(),
                    AdapterId = path.TargetInfo.AdapterId,
                    Id = path.TargetInfo.Id,
                },
            };

            if (DisplayConfigGetSourceDeviceInfo(ref sourceName) != ErrorSuccess
                || DisplayConfigGetTargetDeviceInfo(ref targetName) != ErrorSuccess)
            {
                continue;
            }

            topologyPaths.Add(new DisplayTopologyPath(sourceName.ViewGdiDeviceName, targetName.MonitorDevicePath));
        }

        return BuildHardwareIdsByDesktopDevice(topologyPaths);
    }
}

/// <summary>One active Windows desktop-source to physical-monitor target mapping.</summary>
internal sealed record DisplayTopologyPath(string DesktopDeviceName, string MonitorDevicePath);
