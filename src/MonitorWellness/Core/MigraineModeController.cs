using System.Windows.Threading;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace MonitorWellness.Core;

/// <summary>
/// Migraine mode: instant activation (no fade — someone mid-aura wants relief now, not in
/// 20 seconds), gradual deactivation (fades back to the schedule-driven state, since a
/// sudden brightening is itself a plausible trigger). While active or fading out, the normal
/// day/night schedule is suspended — see SuspendsNormalSchedule — so the two don't fight
/// over the gamma ramp and overlay at the same time.
///
/// Migraine mode intentionally ignores ExcludedMonitors and per-monitor dim multipliers:
/// those are day/night preferences, and this is a distinct, user-triggered emergency
/// override that should affect every screen for full relief.
///
/// Photosensitive epilepsy overlaps with migraine with aura in some patients — the same
/// population this feature serves — so the *rate* of any visual change matters, not just its
/// color (the same reasoning that already led to PlaySoundOnMigraineToggle defaulting off for
/// phonophobia). This is deliberately safe by design, not just accidentally so: activation is
/// a single instantaneous step change (never a strobe/flash), and deactivation is a smooth
/// 20-second fade (FadeDuration) — both well clear of any flicker/strobe frequency that could
/// be a seizure trigger. Documented explicitly here (TECHNICAL_UX_REVIEW.md §4.2) so this
/// reasoning is as visible as the sound-sensitivity reasoning already is, rather than being an
/// unstated assumption a future change could accidentally break (e.g. a "pulse" or "flash to
/// get attention" feature would need to re-examine this).
/// </summary>
public sealed class MigraineModeController
{
    private static readonly TimeSpan FadeDuration = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan FadeTickInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// "Mild" activation scales the configured overlay opacity and contrast reduction by
    /// this factor rather than using a separate, independently-tuned preset — simpler for the
    /// user to reason about ("lighter than my usual setting") than a second full set of
    /// values to configure. Color temperature and hue are unchanged; only intensity differs.
    /// </summary>
    private const double MildIntensityMultiplier = 0.6;

    private readonly IColorTemperatureTarget _colorTemperatureTarget;
    private readonly IOverlayTarget _overlayTarget;
    private readonly AppSettings _settings;
    private readonly Func<(int Kelvin, IReadOnlyDictionary<string, double> BrightnessByDevice, Color DimColor)> _computeScheduleTarget;
    private readonly Func<bool>? _isForegroundFullscreenLikely;

    private DispatcherTimer? _fadeTimer;
    private DateTime _fadeStartUtc;
    private Color _fadeFromColor;
    private double _fadeFromOpacity;
    private int _fadeFromKelvin;
    private double _fadeFromContrast;

    private DispatcherTimer? _autoRevertTimer;
    private bool _activeIsMild;
    private DateTime? _activatedAtUtc;

    /// <summary>UTC time the current activation will auto-revert at, or null if auto-revert is off or migraine mode isn't active.</summary>
    public DateTime? AutoRevertAtUtc { get; private set; }

    public bool IsActive { get; private set; }
    public bool IsFadingOut { get; private set; }

    /// <summary>True if the current (or most recently ended) activation used the mild preset.</summary>
    public bool IsMild => _activeIsMild;

    /// <summary>True while the normal schedule tick should not touch gamma ramp or overlay.</summary>
    public bool SuspendsNormalSchedule => IsActive || IsFadingOut;

    public event Action? StateChanged;

    /// <summary>
    /// Raised right after activation if the foreground window looks like it's probably covering
    /// the whole screen with no border (FullscreenDetector's heuristic) — the overlay's
    /// tint/dim may not actually be visible over a true exclusive-fullscreen surface, and this
    /// is the one signal that can exist to say so instead of failing invisibly. See
    /// TECHNICAL_UX_REVIEW.md §1.3.
    /// </summary>
    public event Action? PossibleFullscreenConflict;

    /// <summary>
    /// Raised right after Deactivate() records its history event, if the user has opted into
    /// both HistoryTrackingEnabled and PromptForMigraineRating — the caller (App) owns showing
    /// any actual UI for this, keeping this class free of a WPF window dependency it doesn't
    /// otherwise have.
    /// </summary>
    public event Action? RatingRequested;

    public MigraineModeController(
        IColorTemperatureTarget colorTemperatureTarget,
        IOverlayTarget overlayTarget,
        AppSettings settings,
        Func<(int Kelvin, IReadOnlyDictionary<string, double> BrightnessByDevice, Color DimColor)> computeScheduleTarget,
        Func<bool>? isForegroundFullscreenLikely = null)
    {
        _colorTemperatureTarget = colorTemperatureTarget;
        _overlayTarget = overlayTarget;
        _settings = settings;
        _computeScheduleTarget = computeScheduleTarget;
        _isForegroundFullscreenLikely = isForegroundFullscreenLikely;
    }

    /// <summary>
    /// Activates migraine mode. mild=true applies a lighter version of the configured
    /// appearance (see MildIntensityMultiplier) — same color, less intense — for when the
    /// full configured intensity feels like more than what's needed right now.
    /// </summary>
    public void Activate(bool mild = false)
    {
        DebugLog.Write($"MigraineMode: Activate (mild={mild})");
        _fadeTimer?.Stop();
        IsFadingOut = false;
        IsActive = true;
        _activeIsMild = mild;
        _activatedAtUtc = DateTime.UtcNow;

        if (_settings.HistoryTrackingEnabled)
            HistoryStore.Append(new HistoryEvent(_activatedAtUtc.Value, "MigraineActivated", mild, null));

        double intensity = mild ? MildIntensityMultiplier : 1.0;

        _colorTemperatureTarget.ApplyToAll(_settings.NightKelvin, _settings.MigraineContrastReduction * intensity);

        Color color = ParseColor(_settings.MigraineOverlayColorHex);
        double opacity = _settings.MigraineOverlayOpacity * intensity;
        var byDevice = _overlayTarget.DeviceNames.ToDictionary(d => d, _ => (color, opacity));
        _overlayTarget.Apply(byDevice);

        _autoRevertTimer?.Stop();
        if (_settings.MigraineAutoRevertMinutes > 0)
        {
            var duration = TimeSpan.FromMinutes(_settings.MigraineAutoRevertMinutes);
            AutoRevertAtUtc = DateTime.UtcNow + duration;
            DebugLog.Write($"MigraineMode: auto-revert armed for {AutoRevertAtUtc:HH:mm} UTC");

            _autoRevertTimer = new DispatcherTimer { Interval = duration };
            _autoRevertTimer.Tick += (_, _) =>
            {
                DebugLog.Write("MigraineMode: auto-revert timer fired");
                Deactivate();
            };
            _autoRevertTimer.Start();
        }
        else
        {
            AutoRevertAtUtc = null;
        }

        StateChanged?.Invoke();

        if (_isForegroundFullscreenLikely?.Invoke() == true)
        {
            DebugLog.Write("MigraineMode: activated while the foreground window looks fullscreen — overlay may not be visible over it");
            PossibleFullscreenConflict?.Invoke();
        }
    }

    public void Deactivate()
    {
        if (!IsActive)
            return;

        DebugLog.Write("MigraineMode: Deactivate (starting fade)");
        _autoRevertTimer?.Stop();
        _autoRevertTimer = null;
        AutoRevertAtUtc = null;

        if (_settings.HistoryTrackingEnabled && _activatedAtUtc.HasValue)
        {
            // Recorded at the moment deactivation is chosen, not when the 20s fade finishes —
            // that's the more meaningful "how long was this actually on" duration for a user
            // reviewing their own history later.
            int durationSeconds = (int)(DateTime.UtcNow - _activatedAtUtc.Value).TotalSeconds;
            HistoryStore.Append(new HistoryEvent(DateTime.UtcNow, "MigraineDeactivated", _activeIsMild, durationSeconds));

            if (_settings.PromptForMigraineRating)
                RatingRequested?.Invoke();
        }
        _activatedAtUtc = null;

        double intensity = _activeIsMild ? MildIntensityMultiplier : 1.0;
        _fadeFromColor = ParseColor(_settings.MigraineOverlayColorHex);
        _fadeFromOpacity = _settings.MigraineOverlayOpacity * intensity;
        _fadeFromKelvin = _settings.NightKelvin;
        _fadeFromContrast = _settings.MigraineContrastReduction * intensity;

        IsActive = false;
        IsFadingOut = true;
        StateChanged?.Invoke();

        _fadeStartUtc = DateTime.UtcNow;
        _fadeTimer?.Stop();
        _fadeTimer = new DispatcherTimer { Interval = FadeTickInterval };
        _fadeTimer.Tick += (_, _) => FadeTick();
        _fadeTimer.Start();
    }

    public void Toggle()
    {
        if (IsActive) Deactivate();
        else Activate();
    }

    /// <summary>
    /// Stops migraine-mode timers without a fade. Reserved for the emergency display restore
    /// path, where returning a usable screen takes precedence over a gradual visual transition.
    /// </summary>
    public void RestoreImmediately()
    {
        if (!IsActive && !IsFadingOut)
            return;

        DebugLog.Write("MigraineMode: emergency immediate restore");
        _fadeTimer?.Stop();
        _fadeTimer = null;
        _autoRevertTimer?.Stop();
        _autoRevertTimer = null;
        AutoRevertAtUtc = null;
        IsActive = false;
        IsFadingOut = false;
        _activatedAtUtc = null;
        StateChanged?.Invoke();
    }

    private void FadeTick()
    {
        double t = Math.Clamp((DateTime.UtcNow - _fadeStartUtc).TotalSeconds / FadeDuration.TotalSeconds, 0.0, 1.0);
        var (targetKelvin, targetBrightnessByDevice, targetDimColor) = _computeScheduleTarget();

        int kelvin = (int)Math.Round(Lerp(_fadeFromKelvin, targetKelvin, t));
        // Normal schedule never uses contrast reduction, so the fade target is always 0 —
        // fading contrast back to identity at the same pace as everything else.
        double contrast = Lerp(_fadeFromContrast, 0.0, t);
        _colorTemperatureTarget.ApplyToAll(kelvin, contrast);

        var byDevice = new Dictionary<string, (Color, double)>();
        foreach (var deviceName in _overlayTarget.DeviceNames)
        {
            double targetBrightness = targetBrightnessByDevice.TryGetValue(deviceName, out var b) ? b : 1.0;
            double targetAlpha = 1.0 - targetBrightness;

            double alpha = Lerp(_fadeFromOpacity, targetAlpha, t);
            // Fades toward whatever the schedule's own dim color currently is (plain black
            // most of the time, but a warm dark brown during deep night — see
            // AppSettings.DeepNightOverlayColorHex), not always pure black.
            Color color = LerpColor(_fadeFromColor, targetDimColor, t);
            byDevice[deviceName] = (color, alpha);
        }
        _overlayTarget.Apply(byDevice);

        if (t >= 1.0)
        {
            _fadeTimer?.Stop();
            _fadeTimer = null;
            IsFadingOut = false;
            DebugLog.Write("MigraineMode: fade complete, normal schedule resumes");
            StateChanged?.Invoke();
        }
    }

    private static Color ParseColor(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;

    private static double Lerp(double from, double to, double t) => from + (to - from) * t;

    private static Color LerpColor(Color from, Color to, double t) => Color.FromRgb(
        (byte)Lerp(from.R, to.R, t),
        (byte)Lerp(from.G, to.G, t),
        (byte)Lerp(from.B, to.B, t));
}
