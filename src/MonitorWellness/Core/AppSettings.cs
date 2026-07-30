namespace MonitorWellness.Core;

/// <summary>
/// Persisted user settings. Kept as a plain POCO so it round-trips cleanly through
/// System.Text.Json — no computed properties, no behavior.
/// </summary>
public sealed class AppSettings
{
    public double Latitude { get; set; } = 51.5072;
    public double Longitude { get; set; } = -0.1276;

    public int DayKelvin { get; set; } = 6500;
    public int NightKelvin { get; set; } = 3400;

    /// <summary>
    /// Target screen brightness (1.0 = full, lower = dimmer) at full day / full night,
    /// smoothly interpolated across twilight by ScheduleCurve. Applied entirely via the
    /// overlay window, not gamma ramp — see the Week 1 finding in IMPLEMENTATION.md for why.
    /// </summary>
    public double DayBrightness { get; set; } = 1.0;
    public double NightBrightness { get; set; } = 0.85;

    /// <summary>Device names (e.g. "\\.\DISPLAY2") to exclude from all adjustment.</summary>
    public List<string> ExcludedMonitors { get; set; } = new();

    /// <summary>
    /// Per-monitor scaling (device name -> multiplier) applied to how much a monitor dims
    /// relative to the global schedule. 1.0 (or absent) follows the global target exactly;
    /// less than 1.0 dims that monitor less than the global target (useful for panels that
    /// read darker than others at the same overlay opacity); greater than 1.0 dims it more.
    /// Multiplies the *dim amount* (1 - brightness), not brightness directly, so 0 always
    /// means "no dimming on this monitor" regardless of the global target.
    /// </summary>
    public Dictionary<string, double> MonitorDimMultiplier { get; set; } = new();

    /// <summary>
    /// Color temperature applied via gamma ramp during migraine mode. Kept separate from
    /// NightKelvin even though both currently sit at the same safe floor (~3400K, per the
    /// Week 1 finding) — migraine mode's real warmth comes from MigraineOverlayColorHex below,
    /// not from this value, but the two concerns are conceptually distinct and may diverge
    /// later (e.g. if DDC/CI support in v1.1 allows gamma ramp to go further).
    /// </summary>
    public int MigraineKelvin { get; set; } = 3400;

    /// <summary>
    /// Deep warm overlay tint used for migraine mode, carrying the warmth gamma ramp can't
    /// reach (see Week 1 finding). A dark amber rather than pure black so the screen reads
    /// as warm, not just dim.
    /// </summary>
    public string MigraineOverlayColorHex { get; set; } = "#321408";

    /// <summary>Overlay opacity during migraine mode — deliberately much stronger than the night dim.</summary>
    public double MigraineOverlayOpacity { get; set; } = 0.72;

    /// <summary>
    /// GlobalHotkey modifier flags (MOD_CONTROL|MOD_ALT|MOD_SHIFT etc.) for toggling migraine
    /// mode. Default is Ctrl+Alt+Shift+M rather than the old prototype's Ctrl+Alt+M, since
    /// that combination was found to conflict with another app during Week 3 testing.
    /// </summary>
    public uint MigraineHotkeyModifiers { get; set; } = GlobalHotkey.MOD_CONTROL | GlobalHotkey.MOD_ALT | GlobalHotkey.MOD_SHIFT;

    /// <summary>Win32 virtual-key code for the migraine hotkey. Default 0x4D ('M').</summary>
    public uint MigraineHotkeyKey { get; set; } = 0x4D;
}
