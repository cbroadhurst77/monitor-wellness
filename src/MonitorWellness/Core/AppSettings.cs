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
    /// Device names to exclude from color temperature shifting specifically, while still
    /// letting them dim with the rest of the schedule — for a photo/video reference monitor
    /// that needs to stay color-accurate but doesn't need to stay at full brightness all
    /// night. Distinct from ExcludedMonitors (which skips both color and brightness).
    /// </summary>
    public List<string> ColorExcludedMonitors { get; set; } = new();

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
    /// Per-monitor color temperature offset in Kelvin (device name -> offset, absent or 0 =
    /// no adjustment), added to the global schedule-computed Kelvin before applying to that
    /// monitor's gamma ramp specifically. For a panel that reads visibly warmer or cooler than
    /// the others at the same nominal setting -- a real, monitor-to-monitor variation, not
    /// something the global Day/Night Kelvin sliders alone can correct since they apply
    /// uniformly. Rides on the same driver-rejection safety net as any other Kelvin value
    /// (GammaRampController.ApplyColorTemperature already returns false and gets logged rather
    /// than applied if the offset pushes a monitor's ramp outside the safe range).
    /// </summary>
    public Dictionary<string, int> MonitorKelvinOffset { get; set; } = new();

    /// <summary>Monitors explicitly approved to use physical DDC/CI brightness after a successful test.</summary>
    public List<string> HardwareBrightnessEnabledMonitors { get; set; } = new();

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
    public int MigraineAutoRevertMinutes { get; set; }

    /// <summary>
    /// Play a system sound when the hotkey toggles migraine mode, in addition to the always-
    /// on visual balloon tip. Defaults to false: migraine sufferers commonly also experience
    /// phonophobia (sound sensitivity) during an attack — the same population this feature is
    /// for — so an audible confirmation is opt-in rather than assumed helpful.
    /// </summary>
    public bool PlaySoundOnMigraineToggle { get; set; }

    /// <summary>
    /// AutoStartManager.Register() was called and succeeded — tracked separately from the
    /// live Task Scheduler state so a startup check can notice if the two have drifted apart
    /// (e.g. a Windows update or IT policy silently removed the task) and tell the user,
    /// rather than auto-start just quietly stopping working with no explanation.
    /// </summary>
    public bool AutoStartEnabled { get; set; }

    /// <summary>
    /// Optional "HH:mm" local bedtime. When set, ScheduleCurve.GetBedtimeFactor blends in
    /// deep-night warmth/dim on a clock-time schedule as well as the usual solar one, via
    /// Math.Max in App.ComputeScheduleTarget -- whichever reaches deep night first wins. Null
    /// (the default) disables this; the sun alone still drives deep night as before.
    /// </summary>
    public string? BedtimeLocal { get; set; }

    /// <summary>
    /// Set once the first-run onboarding window has been shown and dismissed (either path —
    /// "Open Settings Now" or "Got it"). False only ever occurs on a brand-new settings.json,
    /// so this is really just "is this the very first launch."
    /// </summary>
    public bool HasCompletedOnboarding { get; set; }

    /// <summary>
    /// Opt-in, defaults false: whether migraine-mode activations and schedule pauses get
    /// logged locally (HistoryStore) so a user can see their own patterns over time. Off by
    /// default so this app's "no telemetry" story stays true out of the box even though this
    /// particular log never leaves the PC either way — see TECHNICAL_UX_REVIEW.md §1.5/§7.1.
    /// </summary>
    public bool HistoryTrackingEnabled { get; set; }

    /// <summary>
    /// Opt-in, defaults false, only meaningful alongside HistoryTrackingEnabled: after each
    /// Migraine Mode deactivation, briefly ask how helpful it was (1-5, or skip) and log the
    /// answer. Frequency/duration alone (what HistoryTrackingEnabled already logs) can answer
    /// "how often do I use this," but not "is it actually helping" — the more useful half of
    /// "insights to help users understand what helps them specifically." Separate from
    /// HistoryTrackingEnabled since counting usage and being asked to rate it are different
    /// levels of opt-in.
    /// </summary>
    public bool PromptForMigraineRating { get; set; }

    /// <summary>
    /// Opt-in, defaults false: nudge daytime brightness up/down (within
    /// AmbientLightAdapter.MaxAdjustment) based on a real ambient-light sensor reading, on top
    /// of the existing solar-based schedule. Off by default, and a no-op with no user-visible
    /// difference on the (common) majority of machines with no ambient light sensor at all —
    /// see TECHNICAL_UX_REVIEW.md §1.1.
    /// </summary>
    public bool MatchAmbientLight { get; set; }

    /// <summary>
    /// Opt-in, defaults false: a periodic reminder to look away from the screen (the 20-20-20
    /// rule — every 20 minutes, look at something ~20 feet away for ~20 seconds). Unlike this
    /// app's color/brightness features, break-taking is the one eye-strain intervention this
    /// app's own EVALUATION.md notes actually has ergonomics backing (the AAO's position is
    /// that blue-light filtering specifically does not demonstrably reduce eye strain) — it was
    /// previously entirely absent despite that. Off by default, consistent with every other
    /// comfort feature in this app being opt-in rather than assumed wanted.
    /// </summary>
    public bool BreakReminderEnabled { get; set; }

    /// <summary>Minutes between break reminders when BreakReminderEnabled is true. 20 matches the 20-20-20 rule this feature is based on.</summary>
    public int BreakReminderIntervalMinutes { get; set; } = 20;

    /// <summary>
    /// Opt-in, defaults false: once a day at most, check GitHub's public Releases API for a
    /// newer version and show a balloon linking to it if one exists (see Core/UpdateChecker.cs)
    /// — never a silent download/install. This is the only other network call in the app
    /// besides the user-triggered location search, so it follows the same off-by-default
    /// pattern as every other optional feature rather than silently phoning home.
    /// </summary>
    public bool CheckForUpdatesEnabled { get; set; }

    /// <summary>Last time an update check actually ran, so it's throttled to roughly once/day regardless of how often the app is launched/restarted.</summary>
    public DateTime? LastUpdateCheckUtc { get; set; }

    /// <summary>Returns a deep copy suitable for editing as an uncommitted settings draft.</summary>
    public AppSettings Clone() => new()
    {
        Latitude = Latitude,
        Longitude = Longitude,
        DayKelvin = DayKelvin,
        NightKelvin = NightKelvin,
        DayBrightness = DayBrightness,
        NightBrightness = NightBrightness,
        DeepNightBrightness = DeepNightBrightness,
        DeepNightOverlayColorHex = DeepNightOverlayColorHex,
        ExcludedMonitors = new List<string>(ExcludedMonitors),
        ColorExcludedMonitors = new List<string>(ColorExcludedMonitors),
        MonitorDimMultiplier = new Dictionary<string, double>(MonitorDimMultiplier),
        MonitorKelvinOffset = new Dictionary<string, int>(MonitorKelvinOffset),
        HardwareBrightnessEnabledMonitors = new List<string>(HardwareBrightnessEnabledMonitors),
        MigraineOverlayColorHex = MigraineOverlayColorHex,
        MigraineOverlayOpacity = MigraineOverlayOpacity,
        MigraineContrastReduction = MigraineContrastReduction,
        MigraineHotkeyModifiers = MigraineHotkeyModifiers,
        MigraineHotkeyKey = MigraineHotkeyKey,
        MigraineAutoRevertMinutes = MigraineAutoRevertMinutes,
        PlaySoundOnMigraineToggle = PlaySoundOnMigraineToggle,
        AutoStartEnabled = AutoStartEnabled,
        BedtimeLocal = BedtimeLocal,
        HasCompletedOnboarding = HasCompletedOnboarding,
        HistoryTrackingEnabled = HistoryTrackingEnabled,
        PromptForMigraineRating = PromptForMigraineRating,
        MatchAmbientLight = MatchAmbientLight,
        BreakReminderEnabled = BreakReminderEnabled,
        BreakReminderIntervalMinutes = BreakReminderIntervalMinutes,
        CheckForUpdatesEnabled = CheckForUpdatesEnabled,
        LastUpdateCheckUtc = LastUpdateCheckUtc,
    };

    /// <summary>Replaces this instance's values with a deep copy of a validated settings snapshot.</summary>
    public void CopyFrom(AppSettings source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var copy = source.Clone();

        Latitude = copy.Latitude;
        Longitude = copy.Longitude;
        DayKelvin = copy.DayKelvin;
        NightKelvin = copy.NightKelvin;
        DayBrightness = copy.DayBrightness;
        NightBrightness = copy.NightBrightness;
        DeepNightBrightness = copy.DeepNightBrightness;
        DeepNightOverlayColorHex = copy.DeepNightOverlayColorHex;
        ExcludedMonitors = copy.ExcludedMonitors;
        ColorExcludedMonitors = copy.ColorExcludedMonitors;
        MonitorDimMultiplier = copy.MonitorDimMultiplier;
        MonitorKelvinOffset = copy.MonitorKelvinOffset;
        HardwareBrightnessEnabledMonitors = copy.HardwareBrightnessEnabledMonitors;
        MigraineOverlayColorHex = copy.MigraineOverlayColorHex;
        MigraineOverlayOpacity = copy.MigraineOverlayOpacity;
        MigraineContrastReduction = copy.MigraineContrastReduction;
        MigraineHotkeyModifiers = copy.MigraineHotkeyModifiers;
        MigraineHotkeyKey = copy.MigraineHotkeyKey;
        MigraineAutoRevertMinutes = copy.MigraineAutoRevertMinutes;
        PlaySoundOnMigraineToggle = copy.PlaySoundOnMigraineToggle;
        AutoStartEnabled = copy.AutoStartEnabled;
        BedtimeLocal = copy.BedtimeLocal;
        HasCompletedOnboarding = copy.HasCompletedOnboarding;
        HistoryTrackingEnabled = copy.HistoryTrackingEnabled;
        PromptForMigraineRating = copy.PromptForMigraineRating;
        MatchAmbientLight = copy.MatchAmbientLight;
        BreakReminderEnabled = copy.BreakReminderEnabled;
        BreakReminderIntervalMinutes = copy.BreakReminderIntervalMinutes;
        CheckForUpdatesEnabled = copy.CheckForUpdatesEnabled;
        LastUpdateCheckUtc = copy.LastUpdateCheckUtc;
    }
}
