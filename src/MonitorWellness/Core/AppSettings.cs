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

    /// <summary>
    /// Brightness once past nautical twilight (ScheduleCurve.DeepNightThresholdDeg), blended
    /// in on top of NightBrightness as elevation drops further — see ScheduleCurve's doc
    /// comment for why this exists as a separate, deeper stage (approximating f.lux's
    /// "bedtime" 2700K/dimmer stage, which gamma ramp can't reach on its own).
    /// </summary>
    public double DeepNightBrightness { get; set; } = 0.7;

    /// <summary>
    /// The overlay's dim color blends from black toward this warm, very dark brown as deep
    /// night deepens — approximates the extra warmth of a bedtime-level color temperature
    /// (research commonly cites ~1800-2400K shortly before sleep) that gamma ramp alone
    /// cannot reach once already at its safe floor (NightKelvin).
    /// </summary>
    public string DeepNightOverlayColorHex { get; set; } = "#190C04";

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
    /// Migraine mode overlay tint. A muted, desaturated green rather than the amber/red used
    /// in the original prototype — this is a deliberate research-backed choice, not a
    /// stylistic one. Noseda &amp; Burstein (Brain, 2016) found that white, blue, amber, and
    /// red light all *increase* migraine headache pain, while a narrow band of green light
    /// reduces it. Amber "feels" warm and soothing, but the actual clinical finding is that it
    /// aggravates migraine photophobia about as much as blue does — only green measurably
    /// helps. See IMPLEMENTATION.md for the full citation and reasoning.
    ///
    /// There's no separate MigraineKelvin setting for the gamma ramp layer underneath this —
    /// it reuses NightKelvin. At this tint's opacity (~0.7, see MigraineOverlayOpacity below),
    /// the overlay dominates the perceived color, so the gamma layer only needs its usual safe
    /// warmth, not a distinct value of its own.
    /// </summary>
    public string MigraineOverlayColorHex { get; set; } = "#173620";

    /// <summary>Overlay opacity during migraine mode — deliberately much stronger than the night dim.</summary>
    public double MigraineOverlayOpacity { get; set; } = 0.72;

    /// <summary>
    /// Contrast reduction (0.0-0.3) applied via gamma ramp during migraine mode — a distinct
    /// photophobia comfort lever from color/brightness. Confirmed directly on real hardware
    /// (tools/GammaCheck) that raising the ramp floor this way is safe up to 0.30 even at this
    /// app's warmest color temp, as long as it's not combined with gamma-level brightness
    /// scaling (which this app doesn't do anyway — dimming is always the overlay's job).
    /// </summary>
    public double MigraineContrastReduction { get; set; } = 0.15;

    /// <summary>
    /// GlobalHotkey modifier flags (MOD_CONTROL|MOD_ALT|MOD_SHIFT etc.) for toggling migraine
    /// mode. Default is Ctrl+Alt+Shift+M rather than the old prototype's Ctrl+Alt+M, since
    /// that combination was found to conflict with another app during Week 3 testing.
    /// </summary>
    public uint MigraineHotkeyModifiers { get; set; } = GlobalHotkey.MOD_CONTROL | GlobalHotkey.MOD_ALT | GlobalHotkey.MOD_SHIFT;

    /// <summary>Win32 virtual-key code for the migraine hotkey. Default 0x4D ('M').</summary>
    public uint MigraineHotkeyKey { get; set; } = 0x4D;

    /// <summary>
    /// If greater than 0, migraine mode automatically fades back to normal after this many
    /// minutes — a safety net for forgetting to turn it off. Defaults to disabled (0): a real
    /// migraine can last many hours, and auto-reverting on a fixed timer could be actively
    /// unwelcome if someone's genuinely still mid-migraine when it fires. Opt-in, not default-on.
    /// </summary>
    public int MigraineAutoRevertMinutes { get; set; } = 0;
}
