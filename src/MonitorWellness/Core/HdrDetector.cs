using System.Runtime.InteropServices;

namespace MonitorWellness.Core;

/// <summary>
/// Best-effort detection of whether any active display currently has Windows' HDR ("Advanced
/// Color") mode turned on — see TECHNICAL_UX_REVIEW.md §5.3. This app's gamma-ramp-based color
/// temperature control (GammaRampController) has never been tested against an HDR-enabled
/// display (EVALUATION.md already flags this as unverified), and Windows' HDR tone-mapping
/// pipeline is documented to interact unpredictably with SetDeviceGammaRamp.
///
/// Unlike NightLightDetector's Windows Night Light check, this uses a fully public, documented
/// Win32 API — QueryDisplayConfig / DisplayConfigGetDeviceInfo with
/// DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO — rather than an undocumented registry
/// format, so a confident answer from it is trustworthy in a way the Night Light registry blob
/// wouldn't be, PROVIDED the struct layouts below actually match the native ones exactly.
///
/// That caveat isn't hypothetical: an earlier version of this file had DISPLAYCONFIG_RATIONAL
/// represented as a single `ulong` instead of two UINT32 fields, which silently misaligned
/// every field after it plus DISPLAYCONFIG_PATH_INFO.targetInfo itself. This app's own live
/// "no crash, reports disabled" test at the time could not tell that apart from a correct
/// read, since every non-HDR display should report disabled either way — an independent
/// re-review caught it, and it was confirmed (not just suspected) by directly measuring
/// Marshal.SizeOf/OffsetOf against the hand-derived native offsets before and after the fix.
/// The lesson generalized: for raw struct marshaling specifically, "the app didn't crash and
/// returned a plausible-looking answer" is not sufficient verification on its own — check the
/// actual computed layout, not just the observed behavior.
/// </summary>
public static class HdrDetector
{
    private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
    private const int ERROR_SUCCESS = 0;
    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9;

    // Bit 1 (0-indexed) of DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO's bitfield union —
    // advancedColorSupported is bit 0, advancedColorEnabled is bit 1 (MSVC allocates
    // consecutive same-type bitfields starting from the least significant bit).
    private const uint AdvancedColorEnabledBit = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx; // union with a cloneGroupId/sourceModeInfoIdx bitfield view we never need
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx; // union with a desktopModeInfoIdx/targetModeInfoIdx bitfield view we never need
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        // DISPLAYCONFIG_RATIONAL (numerator/denominator, contents unused here) as two explicit
        // UINT32 fields, not a single `ulong` — a `ulong` forces 8-byte alignment in .NET's
        // default sequential layout, which the native struct (built from two 4-byte-aligned
        // UINT32s) does not have. That mismatch silently shifted every field after this one,
        // AND shifted this whole struct's required alignment to 8, which in turn misaligned
        // DISPLAYCONFIG_PATH_INFO.targetInfo itself (confirmed by measuring both versions with
        // Marshal.SizeOf/OffsetOf: the `ulong` version computed sizeof 56 for this struct and
        // put targetInfo at offset 24 within PATH_INFO; this corrected version computes the
        // correct native 48 and offset 20). Found on re-review — the earlier "verified live: no
        // crash, reports disabled" check couldn't distinguish a correct read from a silently
        // garbage one, since every non-HDR display should report disabled either way.
        public uint refreshRateNumerator;
        public uint refreshRateDenominator;
        public uint scanLineOrdering;
        public int targetAvailable; // Win32 BOOL
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_MODE_INFO
    {
        public uint infoType;
        public uint id;
        public LUID adapterId;
        // Union of DISPLAYCONFIG_TARGET_MODE (48 bytes — the largest of the three variants) /
        // DISPLAYCONFIG_SOURCE_MODE (20 bytes) / DISPLAYCONFIG_DESKTOP_IMAGE_INFO (40 bytes).
        // Contents are never read — only each DISPLAYCONFIG_PATH_INFO.targetInfo matters for
        // this query — so a fixed-size byte buffer just needs to match the native struct's
        // total size to keep QueryDisplayConfig's array element stride correct.
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)]
        public byte[] modeUnion;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public uint type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint colorInfoFlags; // advancedColorSupported:1, advancedColorEnabled:1, wideColorEnforced:1, advancedColorForceDisabled:1, reserved:28
        public uint colorEncoding;
        public uint bitsPerColorChannel;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathArray, ref uint numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO requestPacket);

    /// <summary>Pure bit-flag check, pulled out specifically so it has test coverage independent of the live P/Invoke call around it.</summary>
    public static bool HasAdvancedColorEnabledFlag(uint colorInfoFlags) => (colorInfoFlags & AdvancedColorEnabledBit) != 0;

    /// <summary>
    /// True only if at least one active display reads back a confirmed "advanced color
    /// enabled." False both when no display has it enabled AND when the query itself fails for
    /// any reason (older Windows without this API, a transient failure, an unexpected struct
    /// mismatch) — an inability to check is treated the same as "no," since this is purely
    /// advisory and must never change startup behavior on its own.
    /// </summary>
    public static bool IsAnyDisplayHdrEnabled()
    {
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount) != ERROR_SUCCESS || pathCount == 0)
                return false;

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            for (int i = 0; i < modes.Length; i++)
                modes[i].modeUnion = new byte[48];

            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != ERROR_SUCCESS)
                return false;

            for (int i = 0; i < pathCount; i++)
            {
                var info = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO,
                        size = (uint)Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>(),
                        adapterId = paths[i].targetInfo.adapterId,
                        id = paths[i].targetInfo.id,
                    }
                };

                if (DisplayConfigGetDeviceInfo(ref info) != ERROR_SUCCESS)
                    continue; // this target doesn't support the query — skip it, don't fail the whole check

                if (HasAdvancedColorEnabledFlag(info.colorInfoFlags))
                    return true;
            }

            return false;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException or ArgumentException or SEHException)
        {
            DebugLog.Write($"HdrDetector.IsAnyDisplayHdrEnabled check failed: {ex.Message}");
            return false;
        }
    }
}
