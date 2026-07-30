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
/// </summary>
public sealed class MigraineModeController
{
    private static readonly TimeSpan FadeDuration = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan FadeTickInterval = TimeSpan.FromMilliseconds(200);

    private readonly GammaControllerManager _gammaManager;
    private readonly OverlayController _overlay;
    private readonly AppSettings _settings;
    private readonly Func<(int Kelvin, IReadOnlyDictionary<string, double> BrightnessByDevice, Color DimColor)> _computeScheduleTarget;

    private DispatcherTimer? _fadeTimer;
    private DateTime _fadeStartUtc;
    private Color _fadeFromColor;
    private double _fadeFromOpacity;
    private int _fadeFromKelvin;

    public bool IsActive { get; private set; }
    public bool IsFadingOut { get; private set; }

    /// <summary>True while the normal schedule tick should not touch gamma ramp or overlay.</summary>
    public bool SuspendsNormalSchedule => IsActive || IsFadingOut;

    public event Action? StateChanged;

    public MigraineModeController(
        GammaControllerManager gammaManager,
        OverlayController overlay,
        AppSettings settings,
        Func<(int Kelvin, IReadOnlyDictionary<string, double> BrightnessByDevice, Color DimColor)> computeScheduleTarget)
    {
        _gammaManager = gammaManager;
        _overlay = overlay;
        _settings = settings;
        _computeScheduleTarget = computeScheduleTarget;
    }

    public void Activate()
    {
        DebugLog.Write("MigraineMode: Activate");
        _fadeTimer?.Stop();
        IsFadingOut = false;
        IsActive = true;

        foreach (var controller in _gammaManager.Controllers)
            controller.ApplyColorTemperature(_settings.NightKelvin);

        Color color = ParseColor(_settings.MigraineOverlayColorHex);
        double opacity = _settings.MigraineOverlayOpacity;
        var byDevice = _overlay.DeviceNames.ToDictionary(d => d, _ => (color, opacity));
        _overlay.Apply(byDevice);

        StateChanged?.Invoke();
    }

    public void Deactivate()
    {
        if (!IsActive)
            return;

        DebugLog.Write("MigraineMode: Deactivate (starting fade)");
        _fadeFromColor = ParseColor(_settings.MigraineOverlayColorHex);
        _fadeFromOpacity = _settings.MigraineOverlayOpacity;
        _fadeFromKelvin = _settings.NightKelvin;

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

    private void FadeTick()
    {
        double t = Math.Clamp((DateTime.UtcNow - _fadeStartUtc).TotalSeconds / FadeDuration.TotalSeconds, 0.0, 1.0);
        var (targetKelvin, targetBrightnessByDevice, targetDimColor) = _computeScheduleTarget();

        int kelvin = (int)Math.Round(Lerp(_fadeFromKelvin, targetKelvin, t));
        foreach (var controller in _gammaManager.Controllers)
            controller.ApplyColorTemperature(kelvin);

        var byDevice = new Dictionary<string, (Color, double)>();
        foreach (var deviceName in _overlay.DeviceNames)
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
        _overlay.Apply(byDevice);

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
