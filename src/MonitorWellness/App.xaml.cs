using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using Microsoft.Win32;
using MonitorWellness.Core;
using Application = System.Windows.Application;

namespace MonitorWellness;

/// <summary>
/// Week 4: robustness and packaging groundwork. Color temperature comes from the gamma ramp
/// (clamped to a safe ~3400K-6500K range — see the Week 1 finding in IMPLEMENTATION.md),
/// brightness dimming comes from the per-monitor overlay window (Week 2), and migraine mode
/// (Week 3) layers a hotkey-triggered emergency override on top of both. This pass fixes a
/// gap from Week 3: the gamma controller list was built once at startup and never rebuilt,
/// unlike the overlay layer — GammaControllerManager now mirrors OverlayController's
/// rebuild-on-topology-change behavior, and both are explicitly reapplied on resume from
/// sleep, since some driver configurations reset gamma ramp state across that transition.
/// Still no settings UI — settings.json is hand-edited or defaulted for now; that's next.
/// </summary>
public partial class App : Application
{
    private static readonly TimeSpan ScheduleTickInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan IdentifyDuration = TimeSpan.FromSeconds(6);

    private AppSettings _settings = new();
    private NotifyIcon? _trayIcon;
    private System.Drawing.Icon? _iconOff;
    private System.Drawing.Icon? _iconOn;
    private DispatcherTimer? _timer;
    private GammaControllerManager? _gammaManager;
    private OverlayController? _overlay;
    private MigraineModeController? _migraine;
    private GlobalHotkey? _hotkey;
    private ToolStripMenuItem? _resumeScheduleMenuItem;
    private DispatcherTimer? _pauseTimer;
    private DateTime? _pauseUntilUtc;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DebugLog.Write("App starting up");
        DispatcherUnhandledException += (_, args) =>
        {
            DebugLog.Write($"UNHANDLED DISPATCHER EXCEPTION: {args.Exception}");
            args.Handled = true; // diagnostic build only — keep the app alive so the log is useful; revisit before v1 ships
        };

        _settings = SettingsStore.Load();

        _gammaManager = new GammaControllerManager();
        _overlay = new OverlayController();
        _migraine = new MigraineModeController(_gammaManager, _overlay, _settings, ComputeScheduleTarget);
        _migraine.StateChanged += OnMigraineStateChanged;

        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        _iconOff = LoadEmbeddedIcon("MonitorWellness.Assets.migraine_off.ico");
        _iconOn = LoadEmbeddedIcon("MonitorWellness.Assets.migraine_on.ico");

        _trayIcon = new NotifyIcon
        {
            Icon = _iconOff,
            Visible = true,
            Text = "Monitor Wellness"
        };

        RebuildHotkey();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Toggle Migraine Mode", null, (_, _) => _migraine.Toggle());
        menu.Items.Add("Activate Migraine Mode", null, (_, _) => _migraine.Activate());
        menu.Items.Add("Deactivate Migraine Mode", null, (_, _) => _migraine.Deactivate());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Identify Monitors", null, (_, _) =>
        {
            DebugLog.Write("Tray menu: Identify Monitors clicked");
            try
            {
                _overlay?.IdentifyMonitors(IdentifyDuration);
            }
            catch (Exception ex)
            {
                DebugLog.Write($"Tray menu click EXCEPTION: {ex}");
            }
        });
        menu.Items.Add(new ToolStripSeparator());
        var pauseMenu = new ToolStripMenuItem("Pause Schedule");
        pauseMenu.DropDownItems.Add("30 minutes", null, (_, _) => PauseScheduleFor(TimeSpan.FromMinutes(30)));
        pauseMenu.DropDownItems.Add("1 hour", null, (_, _) => PauseScheduleFor(TimeSpan.FromHours(1)));
        pauseMenu.DropDownItems.Add("2 hours", null, (_, _) => PauseScheduleFor(TimeSpan.FromHours(2)));
        pauseMenu.DropDownItems.Add("Until tomorrow", null, (_, _) =>
            PauseScheduleFor(SchedulePause.ComputeUntilTomorrowLocal(DateTime.Now) - DateTime.Now));
        menu.Items.Add(pauseMenu);
        _resumeScheduleMenuItem = new ToolStripMenuItem("Resume Schedule", null, (_, _) => ResumeSchedule()) { Enabled = false };
        menu.Items.Add(_resumeScheduleMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        var autoStartItem = new ToolStripMenuItem("Start with Windows") { Checked = AutoStartManager.IsRegistered() };
        autoStartItem.Click += (_, _) =>
        {
            bool wasOn = autoStartItem.Checked;
            bool success = wasOn ? AutoStartManager.Unregister() : AutoStartManager.Register();
            if (success)
            {
                autoStartItem.Checked = !wasOn;
            }
            else if (_trayIcon is not null)
            {
                _trayIcon.ShowBalloonTip(
                    10_000,
                    "Monitor Wellness",
                    wasOn
                        ? "Couldn't remove the auto-start entry — the UAC prompt may have been cancelled."
                        : "Couldn't set up auto-start — this needs administrator approval (UAC prompt), which may have been cancelled or blocked by IT policy.",
                    ToolTipIcon.Warning);
            }
        };
        menu.Items.Add(autoStartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings...", null, (_, _) => OpenSettingsWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Shutdown());
        _trayIcon.ContextMenuStrip = menu;

        _timer = new DispatcherTimer { Interval = ScheduleTickInterval };
        _timer.Tick += (_, _) => RunScheduleTick();
        _timer.Start();

        RunScheduleTick();
    }

    private SettingsWindow? _settingsWindow;

    /// <summary>
    /// True while the settings window is open. The normal schedule tick is suspended for the
    /// same reason migraine mode suspends it — the settings window drives live previews
    /// directly to the gamma ramp/overlay while sliders are being dragged, and a 30s tick
    /// firing mid-drag would fight with that. Cleared (and the schedule immediately
    /// reapplied) the moment the window closes, whether via Save or Cancel — Cancel doesn't
    /// need to explicitly "revert" anything, it just stops overriding and lets the real
    /// schedule take back over.
    /// </summary>
    private bool _settingsPreviewActive;

    private void OpenSettingsWindow()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsPreviewActive = true;
        _settingsWindow = new SettingsWindow(_settings, _gammaManager!, _overlay!, OnSettingsSaved);
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            _settingsPreviewActive = false;
            RunScheduleTick(); // clears any live preview left on screen the instant the window closes
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void OnSettingsSaved()
    {
        DebugLog.Write("Settings saved from settings window — rebuilding hotkey");
        RebuildHotkey();
        // Deliberately not calling RunScheduleTick() here: the settings window's own preview
        // already has the correct saved values on screen right now (that's the whole point
        // of previewing before Save), and the window is about to close anyway, which reapplies
        // the schedule fresh from the just-saved settings.
    }

    /// <summary>Disposes any existing hotkey and registers one from the current settings. Called at startup and again after the settings window saves a rebind.</summary>
    private void RebuildHotkey()
    {
        _hotkey?.Dispose();

        _hotkey = new GlobalHotkey(_settings.MigraineHotkeyModifiers, _settings.MigraineHotkeyKey);
        _hotkey.Pressed += () =>
        {
            DebugLog.Write("Global hotkey pressed: toggling migraine mode");
            _migraine?.Toggle();
        };

        if (!_hotkey.IsRegistered && _trayIcon is not null)
        {
            // A silently-failed hotkey looks like the app just doesn't work — surface it
            // visibly rather than only logging it (see Week 3 finding in IMPLEMENTATION.md).
            _trayIcon.ShowBalloonTip(
                10_000,
                "Monitor Wellness",
                "That migraine mode shortcut is already used by another app. Use the tray menu to trigger Migraine Mode, or pick a different shortcut in Settings.",
                ToolTipIcon.Warning);
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume)
            return;

        DebugLog.Write("System resumed from sleep — reapplying current state");
        _gammaManager?.ReapplyAfterWake();

        if (_migraine?.IsActive == true)
            _migraine.Activate(); // idempotent — reapplies in case gamma ramp state was reset
        else
            RunScheduleTick();
    }

    private void OnMigraineStateChanged()
    {
        if (_trayIcon is null || _migraine is null) return;

        _trayIcon.Icon = _migraine.IsActive ? _iconOn : _iconOff;
        _trayIcon.Text = _migraine.IsActive
            ? _migraine.AutoRevertAtUtc is DateTime revertAt
                ? $"Monitor Wellness — Migraine Mode ON (auto-off {revertAt.ToLocalTime():HH:mm})"
                : "Monitor Wellness — Migraine Mode ON"
            : _migraine.IsFadingOut
                ? "Monitor Wellness — fading back to normal"
                : "Monitor Wellness";
    }

    /// <summary>
    /// Pure computation of the current schedule target — no side effects, so migraine mode's
    /// fade-out can call it repeatedly to know what to fade toward. Includes the deep-night
    /// bedtime-like phase (see ScheduleCurve/AppSettings doc comments): once past civil
    /// twilight, brightness keeps dropping toward DeepNightBrightness and the dim overlay's
    /// own color shifts from black toward DeepNightOverlayColorHex, approximating the extra
    /// warmth research supports close to bedtime that gamma ramp can't reach on its own.
    /// </summary>
    private (int Kelvin, IReadOnlyDictionary<string, double> BrightnessByDevice, System.Windows.Media.Color DimColor) ComputeScheduleTarget()
    {
        double elevation = SolarCalculator.GetSolarElevationDegrees(DateTime.UtcNow, _settings.Latitude, _settings.Longitude);
        int kelvin = ScheduleCurve.GetTargetKelvin(elevation, _settings.DayKelvin, _settings.NightKelvin);
        double nightBrightness = ScheduleCurve.GetTargetBrightness(elevation, _settings.DayBrightness, _settings.NightBrightness);

        double deepNightFactor = ScheduleCurve.GetDeepNightFactor(elevation);
        double globalBrightness = Lerp(nightBrightness, _settings.DeepNightBrightness, deepNightFactor);

        var dimColor = LerpColor(
            System.Windows.Media.Colors.Black,
            ParseColor(_settings.DeepNightOverlayColorHex),
            deepNightFactor);

        var brightnessByDevice = new Dictionary<string, double>();
        foreach (var controller in _gammaManager?.Controllers ?? Array.Empty<GammaRampController>())
        {
            if (_settings.ExcludedMonitors.Contains(controller.DeviceName))
            {
                brightnessByDevice[controller.DeviceName] = 1.0;
                continue;
            }

            double multiplier = _settings.MonitorDimMultiplier.TryGetValue(controller.DeviceName, out var m) ? m : 1.0;
            double dimAmount = (1.0 - globalBrightness) * multiplier;
            brightnessByDevice[controller.DeviceName] = Math.Clamp(1.0 - dimAmount, 0.0, 1.0);
        }

        return (kelvin, brightnessByDevice, dimColor);
    }

    /// <summary>
    /// Suspends the normal schedule for the given duration — e.g. for color-sensitive work
    /// (photo/video editing) that needs a neutral screen temporarily. Deliberately does not
    /// touch the gamma ramp/overlay itself; it just stops the tick from updating them, so
    /// whatever was already on screen stays there (a genuinely neutral pause, not a forced
    /// reset to some other state).
    /// </summary>
    private void PauseScheduleFor(TimeSpan duration)
    {
        _pauseUntilUtc = DateTime.UtcNow + duration;
        DebugLog.Write($"Schedule paused until {_pauseUntilUtc:yyyy-MM-dd HH:mm} UTC");

        _pauseTimer?.Stop();
        _pauseTimer = new DispatcherTimer { Interval = duration };
        _pauseTimer.Tick += (_, _) => ResumeSchedule();
        _pauseTimer.Start();

        if (_resumeScheduleMenuItem is not null)
            _resumeScheduleMenuItem.Enabled = true;
        if (_trayIcon is not null)
            _trayIcon.Text = $"Monitor Wellness — paused until {_pauseUntilUtc.Value.ToLocalTime():HH:mm}";
    }

    private void ResumeSchedule()
    {
        _pauseTimer?.Stop();
        _pauseTimer = null;
        _pauseUntilUtc = null;
        DebugLog.Write("Schedule resumed");

        if (_resumeScheduleMenuItem is not null)
            _resumeScheduleMenuItem.Enabled = false;

        RunScheduleTick();
    }

    private void RunScheduleTick()
    {
        if (_migraine?.SuspendsNormalSchedule == true || _settingsPreviewActive || _pauseUntilUtc.HasValue)
            return; // migraine mode, a live settings preview, or an explicit pause owns the gamma ramp + overlay right now

        var (kelvin, brightnessByDevice, dimColor) = ComputeScheduleTarget();

        foreach (var controller in _gammaManager?.Controllers ?? Array.Empty<GammaRampController>())
        {
            if (_settings.ExcludedMonitors.Contains(controller.DeviceName))
                continue;

            if (!controller.ApplyColorTemperature(kelvin))
            {
                // Confirmed this actually happens in practice: a user-entered Kelvin value
                // below this hardware's safe gamma ramp floor (~3300K, see the Week 1
                // finding) gets silently rejected by the driver, leaving the display stuck
                // at whatever color it last reached rather than transitioning. Previously
                // unlogged here — RunScheduleTick just ignored the return value.
                DebugLog.Write($"ApplyColorTemperature({kelvin}) REJECTED by driver for {controller.DeviceName} — likely below this hardware's safe floor.");
            }
        }

        _overlay?.ApplyDim(brightnessByDevice, dimColor);

        if (_trayIcon is not null && _migraine?.IsActive != true)
        {
            double elevation = SolarCalculator.GetSolarElevationDegrees(DateTime.UtcNow, _settings.Latitude, _settings.Longitude);
            _trayIcon.Text = $"Monitor Wellness — {kelvin}K, sun {elevation:F1}°";
        }
    }

    private static System.Windows.Media.Color ParseColor(string hex)
        => (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!;

    private static double Lerp(double from, double to, double t) => from + (to - from) * t;

    private static System.Windows.Media.Color LerpColor(System.Windows.Media.Color from, System.Windows.Media.Color to, double t)
        => System.Windows.Media.Color.FromRgb(
            (byte)Lerp(from.R, to.R, t),
            (byte)Lerp(from.G, to.G, t),
            (byte)Lerp(from.B, to.B, t));

    private static System.Drawing.Icon LoadEmbeddedIcon(string resourceName)
    {
        using var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        return new System.Drawing.Icon(stream);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _hotkey?.Dispose();
        _gammaManager?.Dispose();
        _overlay?.Dispose();
        _trayIcon?.Dispose();
        _iconOn?.Dispose();
        _iconOff?.Dispose();
        base.OnExit(e);
    }
}
