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
    private DispatcherTimer? _breakReminderTimer;
    private GammaControllerManager? _gammaManager;
    private OverlayController? _overlay;
    private MigraineModeController? _migraine;
    private GlobalHotkey? _hotkey;
    private ToolStripMenuItem? _resumeScheduleMenuItem;
    private DispatcherTimer? _pauseTimer;
    private DateTime? _pauseUntilUtc;
    private SingleInstanceGuard? _singleInstanceGuard;
    private readonly CrashLoopDetector _crashLoopDetector = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DebugLog.Write("App starting up");

        // Checked first, before anything else touches the gamma ramp/overlay/hotkey/tray icon:
        // a second instance racing the first would otherwise register a competing schedule
        // timer and a hotkey that silently fails (see TECHNICAL_UX_REVIEW.md §3.1) — this app's
        // own portable + auto-start design makes an accidental double-launch a real scenario,
        // not just a hypothetical one.
        _singleInstanceGuard = new SingleInstanceGuard();
        if (!_singleInstanceGuard.IsPrimaryInstance)
        {
            DebugLog.Write("Another instance is already running — exiting");
            System.Windows.MessageBox.Show(
                "Monitor Wellness is already running — check your system tray (near the clock) for its icon.",
                "Monitor Wellness",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // Decision (TECHNICAL_UX_REVIEW.md §3.2, closing out a gap this code previously left
        // open in its own comment): keep the app alive on an unhandled exception — a full
        // crash could leave migraine mode's tint/dim frozen on screen with no controller left
        // running to fade it back, which is worse than a logged, recovered hiccup. But don't
        // swallow forever with no signal: a single/rare exception attempts the same recovery
        // already used for sleep/resume (rebuild gamma controllers, reapply the current
        // target), since an unhandled exception mid-tick is exactly the kind of moment that
        // could leave gamma ramp state stale. If exceptions keep recurring in a short window
        // (CrashLoopDetector), stop pretending everything's fine and tell the user visibly.
        DispatcherUnhandledException += (_, args) =>
        {
            DebugLog.Write($"UNHANDLED DISPATCHER EXCEPTION: {args.Exception}");
            args.Handled = true;

            if (_crashLoopDetector.RecordAndCheckIsLooping(DateTime.UtcNow))
            {
                _trayIcon?.ShowBalloonTip(
                    15_000,
                    "Monitor Wellness",
                    "Monitor Wellness has hit repeated internal errors and may not be working correctly. Check the log in %AppData%\\MonitorWellness\\debug.log, or restart the app.",
                    ToolTipIcon.Error);
                return;
            }

            try
            {
                _gammaManager?.ReapplyAfterWake();
                if (_migraine?.IsActive == true)
                    _migraine.Activate(); // idempotent — reapplies in case gamma ramp/overlay state was left stale
                else if (!_settingsPreviewActive && !_pauseUntilUtc.HasValue)
                    RunScheduleTick();
            }
            catch (Exception recoveryEx)
            {
                DebugLog.Write($"Recovery attempt after unhandled exception also failed: {recoveryEx}");
            }
        };

        _settings = SettingsStore.Load();

        _gammaManager = new GammaControllerManager();
        _overlay = new OverlayController();
        _migraine = new MigraineModeController(_gammaManager, _overlay, _settings, ComputeScheduleTarget, FullscreenDetector.IsForegroundWindowLikelyFullscreen);
        _migraine.StateChanged += OnMigraineStateChanged;
        _migraine.PossibleFullscreenConflict += () => _trayIcon?.ShowBalloonTip(
            10_000,
            "Monitor Wellness",
            "Migraine Mode applied color/contrast, but a fullscreen app may be blocking the dim overlay — Alt+Tab out or switch to windowed mode to see the full effect.",
            ToolTipIcon.Warning);
        _migraine.RatingRequested += ShowMigraineRatingPrompt;

        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        _iconOff = LoadEmbeddedIcon("MonitorWellness.Assets.migraine_off.ico");
        _iconOn = LoadEmbeddedIcon("MonitorWellness.Assets.migraine_on.ico");

        _trayIcon = new NotifyIcon
        {
            Icon = _iconOff,
            Visible = true,
            Text = "Monitor Wellness"
        };

        if (HdrDetector.IsAnyDisplayHdrEnabled())
        {
            // EVALUATION.md already flags HDR as untested; this app's gamma-ramp approach is
            // documented to interact unpredictably with Windows' HDR tone-mapping pipeline.
            // See TECHNICAL_UX_REVIEW.md §5.3.
            DebugLog.Write("An active display has Windows HDR (advanced color) enabled — gamma ramp behavior here is unverified");
            _trayIcon.ShowBalloonTip(
                10_000,
                "Monitor Wellness",
                "One of your displays has Windows HDR turned on. Color/brightness adjustments haven't been tested against HDR displays and may not look right — if something seems off, try turning HDR off for this monitor.",
                ToolTipIcon.Warning);
        }

        if (NightLightDetector.IsFluxRunning())
        {
            // f.lux is this app's own predecessor (README) and writes to the exact same
            // last-write-wins gamma ramp state — a real, likely-to-occur conflict for anyone
            // who migrated from it without uninstalling. See TECHNICAL_UX_REVIEW.md §1.4/§5.1.
            DebugLog.Write("f.lux appears to be running — likely gamma ramp conflict");
            _trayIcon.ShowBalloonTip(
                10_000,
                "Monitor Wellness",
                "f.lux also appears to be running. Two tools adjusting your screen color at once can cause flickering or color that keeps reverting — consider closing one of them.",
                ToolTipIcon.Warning);
        }

        RebuildHotkey();

        // A single left-click on the tray icon toggles migraine mode directly — the hotkey and
        // the right-click menu both still work, but this is the fastest possible path for the
        // moment this feature exists for: someone mid-aura who doesn't want to hunt through a
        // ~14-item context menu (see TECHNICAL_UX_REVIEW.md §2.1). MouseClick (not Click) is
        // used specifically so this only fires for the left button — Click alone would also
        // fire on the right-click that opens ContextMenuStrip, double-triggering a toggle.
        _trayIcon.MouseClick += (_, args) =>
        {
            if (args.Button == System.Windows.Forms.MouseButtons.Left)
            {
                DebugLog.Write("Tray icon left-clicked: toggling migraine mode");
                ToggleMigraineModeWithFeedback();
            }
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("&Toggle Migraine Mode", null, (_, _) => _migraine.Toggle());
        menu.Items.Add("Activate Migraine Mode (&Full)", null, (_, _) => _migraine.Activate(mild: false));
        menu.Items.Add("Activate Migraine Mode (Mi&ld)", null, (_, _) => _migraine.Activate(mild: true));
        menu.Items.Add("&Deactivate Migraine Mode", null, (_, _) => _migraine.Deactivate());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("&Identify Monitors", null, (_, _) =>
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
        var pauseMenu = new ToolStripMenuItem("&Pause Schedule");
        pauseMenu.DropDownItems.Add("30 minutes", null, (_, _) => PauseScheduleFor(TimeSpan.FromMinutes(30)));
        pauseMenu.DropDownItems.Add("1 hour", null, (_, _) => PauseScheduleFor(TimeSpan.FromHours(1)));
        pauseMenu.DropDownItems.Add("2 hours", null, (_, _) => PauseScheduleFor(TimeSpan.FromHours(2)));
        pauseMenu.DropDownItems.Add("Until tomorrow", null, (_, _) =>
            PauseScheduleFor(SchedulePause.ComputeUntilTomorrowLocal(DateTime.Now) - DateTime.Now));
        menu.Items.Add(pauseMenu);
        _resumeScheduleMenuItem = new ToolStripMenuItem("&Resume Schedule", null, (_, _) => ResumeSchedule()) { Enabled = false };
        menu.Items.Add(_resumeScheduleMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        bool autoStartCurrentlyRegistered = AutoStartManager.IsRegistered();
        if (_settings.AutoStartEnabled && !autoStartCurrentlyRegistered)
        {
            // The user turned this on at some point and it's since disappeared — e.g. a
            // Windows update or IT policy silently removed the Task Scheduler entry. Worth
            // saying so explicitly rather than letting auto-start just quietly stop working.
            DebugLog.Write("Auto-start drift detected: AppSettings.AutoStartEnabled is true but the Task Scheduler entry is missing");
            _trayIcon.ShowBalloonTip(
                10_000,
                "Monitor Wellness",
                "Auto-start seems to have been turned off (possibly by a Windows update or IT policy) — re-enable it from this tray menu if you still want it.",
                ToolTipIcon.Warning);
        }

        var autoStartItem = new ToolStripMenuItem("Start with &Windows") { Checked = autoStartCurrentlyRegistered };
        autoStartItem.Click += (_, _) =>
        {
            bool wasOn = autoStartItem.Checked;
            bool success = wasOn ? AutoStartManager.Unregister() : AutoStartManager.Register();
            if (success)
            {
                autoStartItem.Checked = !wasOn;
                _settings.AutoStartEnabled = !wasOn;
                SettingsStore.Save(_settings);
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
        menu.Items.Add("&Auto-start Diagnostics...", null, (_, _) => ShowAutoStartDiagnostics());
        menu.Items.Add("&Open Logs Folder", null, (_, _) => OpenLogsFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("&Settings...", null, (_, _) => OpenSettingsWindow());
        menu.Items.Add("&Help / About...", null, (_, _) => ShowOnboarding(markCompletedOnClose: false));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("E&xit", null, (_, _) => Shutdown());
        _trayIcon.ContextMenuStrip = menu;

        _timer = new DispatcherTimer { Interval = ScheduleTickInterval };
        _timer.Tick += (_, _) => RunScheduleTick();
        _timer.Start();

        RunScheduleTick();
        RebuildBreakReminderTimer();

        if (!_settings.HasCompletedOnboarding)
            ShowOnboarding(markCompletedOnClose: true);
    }

    /// <summary>
    /// Shows the onboarding window — either the real first-run flow (markCompletedOnClose:
    /// true, the only path that persists HasCompletedOnboarding) or a manual reopen from the
    /// tray menu's "Help / About..." item, which previously had no way back to this content
    /// at all once dismissed once (see TECHNICAL_UX_REVIEW.md §2.4).
    /// </summary>
    private void ShowOnboarding(bool markCompletedOnClose)
    {
        var onboarding = new OnboardingWindow(OpenSettingsWindow);
        if (markCompletedOnClose)
        {
            onboarding.Closed += (_, _) =>
            {
                _settings.HasCompletedOnboarding = true;
                SettingsStore.Save(_settings);
            };
        }
        onboarding.Show();
        onboarding.Activate();
    }

    /// <summary>Opens %AppData%\MonitorWellness\ in Explorer — closes the gap where a user filing a bug report had to be told the exact path and find it manually (TECHNICAL_UX_REVIEW.md §7.2).</summary>
    private static void OpenLogsFolder()
    {
        string folder = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MonitorWellness");
        System.IO.Directory.CreateDirectory(folder); // in case nothing has been logged/saved yet
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DebugLog.Write($"OpenLogsFolder failed: {ex.Message}");
        }
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
        RebuildBreakReminderTimer();
        // Deliberately not calling RunScheduleTick() here: the settings window's own preview
        // already has the correct saved values on screen right now (that's the whole point
        // of previewing before Save), and the window is about to close anyway, which reapplies
        // the schedule fresh from the just-saved settings.
    }

    /// <summary>
    /// Opt-in 20-20-20 reminder (AppSettings.BreakReminderEnabled) — the one eye-strain
    /// intervention this app's own EVALUATION.md notes actually has ergonomics backing
    /// (unlike blue-light filtering itself, per the AAO position cited there), previously
    /// entirely absent from the app. Called at startup and again after Settings saves, since
    /// the interval or on/off state may have just changed.
    /// </summary>
    private void RebuildBreakReminderTimer()
    {
        _breakReminderTimer?.Stop();
        _breakReminderTimer = null;

        if (!_settings.BreakReminderEnabled)
            return;

        _breakReminderTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(Math.Max(1, _settings.BreakReminderIntervalMinutes)) };
        _breakReminderTimer.Tick += (_, _) =>
        {
            // Skip while migraine mode is active -- someone already dealing with that doesn't
            // need an unrelated interruption layered on top. The timer keeps running on its
            // normal interval rather than resetting, so it resumes nudging once migraine mode
            // ends rather than needing a full interval to elapse again.
            if (_migraine?.IsActive != true)
            {
                _trayIcon?.ShowBalloonTip(
                    8_000,
                    "Monitor Wellness",
                    "Time for a break — look at something about 20 feet away for 20 seconds (the 20-20-20 rule).",
                    ToolTipIcon.Info);
            }
        };
        _breakReminderTimer.Start();
    }

    /// <summary>
    /// Toggles migraine mode and gives feedback — shared by both the global hotkey and the
    /// tray icon's single-click shortcut, since both are meant to be equally fast emergency
    /// activation paths. A visual confirmation matters specifically here: this is most likely
    /// to be used mid-aura, when vision may already be compromised, so relying on noticing the
    /// screen change alone is less reliable than it should be. Sound is opt-in, not default —
    /// see AppSettings.PlaySoundOnMigraineToggle for why.
    /// </summary>
    private void ToggleMigraineModeWithFeedback()
    {
        bool wasActive = _migraine?.IsActive == true;
        _migraine?.Toggle();

        bool nowActive = !wasActive;
        _trayIcon?.ShowBalloonTip(
            4_000,
            "Monitor Wellness",
            nowActive ? "Migraine Mode ON" : "Migraine Mode OFF — fading back to normal",
            ToolTipIcon.None);

        if (_settings.PlaySoundOnMigraineToggle)
        {
            try { (nowActive ? System.Media.SystemSounds.Exclamation : System.Media.SystemSounds.Asterisk).Play(); }
            catch (Exception ex) { DebugLog.Write($"PlaySoundOnMigraineToggle failed: {ex.Message}"); }
        }
    }

    /// <summary>
    /// Shows the "how helpful was that?" prompt (AppSettings.PromptForMigraineRating) — kept
    /// here rather than in MigraineModeController so that class stays free of a WPF window
    /// dependency it doesn't otherwise have; only appends a history event if the user actually
    /// answers (a skipped/auto-dismissed prompt leaves no trace).
    /// </summary>
    private void ShowMigraineRatingPrompt()
    {
        var window = new MigraineRatingWindow(rating =>
        {
            if (rating.HasValue)
                HistoryStore.Append(new HistoryEvent(DateTime.UtcNow, "MigraineRating", null, null, rating));
        });
        window.Show();
    }

    /// <summary>Disposes any existing hotkey and registers one from the current settings. Called at startup and again after the settings window saves a rebind.</summary>
    private void RebuildHotkey()
    {
        _hotkey?.Dispose();

        _hotkey = new GlobalHotkey(_settings.MigraineHotkeyModifiers, _settings.MigraineHotkeyKey);
        _hotkey.Pressed += () =>
        {
            DebugLog.Write("Global hotkey pressed: toggling migraine mode");
            ToggleMigraineModeWithFeedback();
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

    /// <summary>
    /// Answers the one question the drift-check-on-startup balloon (above) can't: not just
    /// "is the task still registered," but "has it actually fired" -- the only way, short of
    /// physically rebooting and watching, to check whether auto-start really survives a real
    /// logon rather than just a manual "Start with Windows" click.
    /// </summary>
    private static void ShowAutoStartDiagnostics()
    {
        var diagnostics = AutoStartManager.GetDiagnostics();
        string message = !diagnostics.IsRegistered
            ? "Auto-start is not currently registered. Turn on \"Start with Windows\" from this menu first."
            : $"Status: {diagnostics.Status ?? "unknown"}\n" +
              $"Last Run Time: {diagnostics.LastRunTime ?? "unknown"}\n" +
              $"Last Result: {diagnostics.LastResult ?? "unknown"} (0 = last run succeeded)\n" +
              $"Next Run Time: {diagnostics.NextRunTime ?? "unknown"}\n\n" +
              "\"Last Run Time\" only updates after an actual Windows logon triggers the task " +
              "-- it won't change just from toggling \"Start with Windows\" or running the app " +
              "manually. If you want to confirm auto-start survives a real reboot, restart " +
              "Windows normally and check this again afterward.";

        System.Windows.MessageBox.Show(message, "Monitor Wellness — Auto-start Diagnostics", MessageBoxButton.OK,
            diagnostics.IsRegistered ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume)
            return;

        // SystemEvents isn't guaranteed to raise on the UI thread (see the identical guard in
        // OverlayController/GammaControllerManager) — everything this handler touches
        // (gamma controllers, migraine state, the overlay via RunScheduleTick) is owned by
        // this app's single Dispatcher thread.
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnPowerModeChanged(sender, e));
            return;
        }

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
        string intensitySuffix = _migraine.IsMild ? " (Mild)" : "";
        _trayIcon.Text = _migraine.IsActive
            ? _migraine.AutoRevertAtUtc is DateTime revertAt
                ? $"Monitor Wellness — Migraine Mode ON{intensitySuffix} (auto-off {revertAt.ToLocalTime():HH:mm})"
                : $"Monitor Wellness — Migraine Mode ON{intensitySuffix}"
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
        if (!string.IsNullOrWhiteSpace(_settings.BedtimeLocal) && TimeSpan.TryParse(_settings.BedtimeLocal, out var bedtime))
        {
            double bedtimeFactor = ScheduleCurve.GetBedtimeFactor(DateTime.Now, bedtime);
            deepNightFactor = Math.Max(deepNightFactor, bedtimeFactor);
        }
        double globalBrightness = Lerp(nightBrightness, _settings.DeepNightBrightness, deepNightFactor);

        if (_settings.MatchAmbientLight)
        {
            // Scaled by dayFactor so this has zero effect at night regardless of room
            // lighting — a lamp-lit bedroom shouldn't fight the night schedule. Any failure to
            // read the sensor (including simply not having one, the common case) leaves
            // globalBrightness untouched.
            double? lux = AmbientLightSensor.TryGetCurrentLux();
            if (lux.HasValue)
            {
                double dayFactor = ScheduleCurve.GetDayFactor(elevation);
                double adjustment = AmbientLightAdapter.ComputeBrightnessAdjustment(lux.Value) * dayFactor;
                globalBrightness = Math.Clamp(globalBrightness + adjustment, 0.0, 1.0);
            }
        }

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

        if (_settings.HistoryTrackingEnabled)
            HistoryStore.Append(new HistoryEvent(DateTime.UtcNow, "SchedulePaused", null, (int)duration.TotalSeconds));

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

            if (_settings.ColorExcludedMonitors.Contains(controller.DeviceName))
            {
                // Color-accurate reference monitor: stays at native color, but still
                // participates in the brightness schedule below.
                controller.ResetToIdentity();
                continue;
            }

            int offset = _settings.MonitorKelvinOffset.TryGetValue(controller.DeviceName, out var off) ? off : 0;
            int targetKelvin = kelvin + offset;
            if (!controller.ApplyColorTemperature(targetKelvin))
            {
                // Confirmed this actually happens in practice: a user-entered Kelvin value
                // below this hardware's safe gamma ramp floor (~3300K, see the Week 1
                // finding) gets silently rejected by the driver, leaving the display stuck
                // at whatever color it last reached rather than transitioning. Previously
                // unlogged here — RunScheduleTick just ignored the return value.
                DebugLog.Write($"ApplyColorTemperature({targetKelvin}) REJECTED by driver for {controller.DeviceName} — likely below this hardware's safe floor.");
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
        _breakReminderTimer?.Stop();
        _hotkey?.Dispose();
        _gammaManager?.Dispose();
        _overlay?.Dispose();
        _trayIcon?.Dispose();
        _iconOn?.Dispose();
        _iconOff?.Dispose();
        _singleInstanceGuard?.Dispose();
        base.OnExit(e);
    }
}
