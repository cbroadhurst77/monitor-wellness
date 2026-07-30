using System.Reflection;
using System.Runtime.InteropServices;
using MonitorWellness.Core;

namespace MonitorWellness.Tests;

/// <summary>
/// Regression tests for the exact defect an independent review caught: a `ulong` standing in
/// for DISPLAYCONFIG_RATIONAL (really two UINT32 fields) silently misaligned every field after
/// it in DISPLAYCONFIG_PATH_TARGET_INFO, and — because .NET's default sequential layout raises
/// a struct's own required alignment to match its widest field — misaligned
/// DISPLAYCONFIG_PATH_INFO.targetInfo itself. The earlier "verified live: no crash, reports
/// disabled" check could not tell a correct read apart from a silently garbage one, since every
/// non-HDR display should report disabled either way. These expected sizes/offsets are the
/// hand-derived native values from Microsoft's published struct documentation (see
/// HdrDetector.cs's doc comments for citations) — if any of these ever regress,
/// QueryDisplayConfig will read garbage instead of real adapter/target IDs, and HdrDetector
/// will most likely fail silently forever rather than crash, exactly like the original bug did.
/// </summary>
public class HdrDetectorStructLayoutTests
{
    private static Type GetNestedType(string name)
        => typeof(HdrDetector).GetNestedType(name, BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"HdrDetector no longer has a nested type named '{name}' — update this test to match.");

    [Fact]
    public void PathSourceInfo_MatchesNativeSize()
        => Assert.Equal(20, Marshal.SizeOf(GetNestedType("DISPLAYCONFIG_PATH_SOURCE_INFO")));

    [Fact]
    public void PathTargetInfo_MatchesNativeSize()
        => Assert.Equal(48, Marshal.SizeOf(GetNestedType("DISPLAYCONFIG_PATH_TARGET_INFO")));

    [Fact]
    public void PathTargetInfo_StatusFlagsIsAtTheCorrectNativeOffset()
        => Assert.Equal(44, Marshal.OffsetOf(GetNestedType("DISPLAYCONFIG_PATH_TARGET_INFO"), "statusFlags").ToInt32());

    [Fact]
    public void PathInfo_MatchesNativeSize()
        => Assert.Equal(72, Marshal.SizeOf(GetNestedType("DISPLAYCONFIG_PATH_INFO")));

    [Fact]
    public void PathInfo_TargetInfoIsAtTheCorrectNativeOffset()
        => Assert.Equal(20, Marshal.OffsetOf(GetNestedType("DISPLAYCONFIG_PATH_INFO"), "targetInfo").ToInt32());

    [Fact]
    public void ModeInfo_MatchesNativeSize()
        => Assert.Equal(64, Marshal.SizeOf(GetNestedType("DISPLAYCONFIG_MODE_INFO")));

    [Fact]
    public void DeviceInfoHeader_MatchesNativeSize()
        => Assert.Equal(20, Marshal.SizeOf(GetNestedType("DISPLAYCONFIG_DEVICE_INFO_HEADER")));

    [Fact]
    public void GetAdvancedColorInfo_MatchesNativeSize()
        => Assert.Equal(32, Marshal.SizeOf(GetNestedType("DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO")));
}
