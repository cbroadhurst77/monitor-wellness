using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using System.IO;
using System.Diagnostics.CodeAnalysis;
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
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "WPF Application owns the process lifetime; OnExit disposes the owned resources after stopping event sources.")]
public partial class App : Application
{
    private static readonly TimeSpan ScheduleTickInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ApplicationRulePollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan IdentifyDuration = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan EmergencyRestorePauseDuration = TimeSpan.FromHours(1);
    private const uint EmergencyRestoreModifiers = GlobalHotkey.MOD_CONTROL | GlobalHotkey.MOD_ALT | GlobalHotkey.MOD_SHIFT;
    private const uint EmergencyRestoreKey = 0x52; // R
    private const int EmergencyRestoreHotkeyId = 0xA1F4;

    private AppSettings _settings = new();
    private NotifyIcon? _trayIcon;
    private System.Drawing.Icon? _iconOff;
    private System.Drawing.Icon? _iconOn;
    private DispatcherTimer? _timer;
    private DispatcherTimer? _applicationRuleTimer;
    private BreakFocusWindow? _breakFocusWindow;
    private BreakReminderService? _breakReminder;
    private DisplayApplier? _displayApplier;
    private GammaControllerManager? _gammaManager;
    private OverlayController? _overlay;
    private readonly HardwareBrightnessControllerManager _hardwareBrightness = new();
    private readonly AmbientLightSmoother _ambientLightSmoother = new();
    private MigraineModeController? _migraine;
    private GlobalHotkey? _hotkey;
    private uint _hotkeyModifiers;
    private uint _hotkeyKey;
    private GlobalHotkey? _emergencyRestoreHotkey;
    private ToolStripMenuItem? _resumeScheduleMenuItem;
    private ToolStripMenuItem? _focusBreakMenuItem;
    private DispatcherTimer? _pauseTimer;
    private readonly ScheduleOverrideState _overrides = new();
    private string? _lastForegroundApplicationContext;
    private bool _migraineWasActive;
    private SingleInstanceGuard? _singleInstanceGuard;
    private readonly CrashLoopDetector _crashLoopDetector = new();

    // Multiple detection balloons (HDR, f.lux conflict, hotkey conflict, auto-start drift) can
    // all fire on the very first launch, potentially stacked with the onboarding window itself
    // — a noisy first impression for a "calm" product. These are queued instead of shown
    // immediately, then drained one at a time (after onboarding closes, if this is a first run)
    // with a short gap between each. See MonitorWellness_UX_Accessibility_Audit.html §1/§6 (P0).
    private static readonly TimeSpan StartupBalloonSpacing = TimeSpan.FromSeconds(6);
    private readonly Queue<(string Title, string Message, ToolTipIcon Icon)> _pendingStartupBalloons = new();
    private DispatcherTimer? _startupBalloonTimer;

    // NotifyIcon.BalloonTipClicked doesn't say which balloon was clicked -- there's only ever
    // one visible at a time, so this just remembers what the currently-showing balloon should
    // do if clicked, and is cleared the moment that balloon closes (clicked or not) so a stale
    // URL can't get triggered by an unrelated balloon shown much later. See ShowUpdateBalloon.
    private string? _pendingBalloonClickUrl;
    private Action? _pendingBalloonClickAction;
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromDays(1);
    private static readonly TimeSpan UpdateCheckPollInterval = TimeSpan.FromHours(6);
    private DispatcherTimer? _updateCheckTimer;
    private string? _lastNotifiedUpdateTag;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DebugLog.Write("App starting up");
        var startup = new StartupTimeline();

        // Checked first, before anything else touches the gamma ramp/overlay/hotkey/tray icon:
        // a second instance racing the first would otherwise register a competing schedule
        // timer and a hotkey that silently fails (see TECHNICAL_UX_REVIEW.md §3.1) — this app's
        // own portable + auto-start design makes an accidental double-launch a real scenario,
        // not just a hypothetical one.
        bool restoreOnly = StartupArguments.HasSwitch(e.Args, StartupArguments.Restore);
        if (StartupArguments.TryGetRestartWaitProcessId(e.Args, out int previousProcessId))
            WaitForPreviousInstance(previousProcessId);
        _singleInstanceGuard = new SingleInstanceGuard();
        if (!_singleInstanceGuard.IsPrimaryInstance)
        {
            DebugLog.Write(restoreOnly
                ? "--restore requested while another instance is running — exiting"
                : "Another instance is already running — exiting");
            System.Windows.MessageBox.Show(
                restoreOnly
                    ? "Monitor Wellness is running, so it controls your display right now. Use its tray icon's \"Emergency Restore Screen\" (Ctrl+Alt+Shift+R), or exit it first and run this again."
                    : "Monitor Wellness is already running — check your system tray (near the clock) for its icon.",
                "Monitor Wellness",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        if (restoreOnly)
        {
            RunRestoreOnly();
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
        // Exceptions on the UI thread are handled just below. These two cover everything else: a
        // background-thread crash used to leave no trace in the log at all, and left the screen
        // tinted (or a monitor dimmed) until the app was started again.
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            DebugLog.Write($"UNOBSERVED TASK EXCEPTION: {args.Exception}");
            args.SetObserved();
        };

        DispatcherUnhandledException += (_, args) =>
        {
            DebugLog.Write($"UNHANDLED DISPATCHER EXCEPTION: {args.Exception}");
            args.Handled = true;

            // Everything below — including the crash-loop check and its own balloon call —
            // is inside this try. It previously wasn't: if _crashLoopDetector or
            // ShowBalloonTip itself threw (e.g. _trayIcon in a bad state), the exception
            // would escape this handler entirely, and since this already runs inside
            // dispatcher exception dispatch, args.Handled = true above wouldn't save it —
            // the whole recovery/crash-loop mechanism defeated by the code meant to report it.
            try
            {
                if (_crashLoopDetector.RecordAndCheckIsLooping(DateTime.UtcNow))
                {
                    ShowTrayBalloon(
                        15_000,
                        "Monitor Wellness",
                        "Something's gone wrong repeatedly. Please restart Monitor Wellness — if it keeps happening, our log file can help us find out why.",
                        ToolTipIcon.Error);
                    return;
                }

                _gammaManager?.ReapplyAfterWake();
                if (_migraine?.IsActive == true)
                    _migraine.Reapply(); // reapplies in case gamma ramp/overlay state was left stale, without restarting the session
                else if (!_overrides.SettingsPreviewActive && !_overrides.PauseUntilUtc.HasValue)
                    RunScheduleTick();
            }
            catch (Exception recoveryEx)
            {
                DebugLog.Write($"Recovery attempt after unhandled exception also failed: {recoveryEx}");
            }
        };

        _settings = SettingsStore.Load();
        if (SettingsStore.LastLoadProblem is string settingsProblem)
            QueueStartupBalloon("Monitor Wellness", settingsProblem, ToolTipIcon.Warning);
        MigrateMonitorSettings();
        if (_settings.MatchAmbientLight)
            AmbientLightSensor.WarmUp(); // slow right after a boot, so start it now, off the UI thread
        startup.Mark("settings");

        // A previous run that was killed, crashed hard, or lost power leaves its gamma ramp and
        // any DDC/CI hardware dimming in place. Put those back first, before anything below
        // reads the display's current state and mistakes the leftover for "normal".
        try
        {
            DisplayRecoveryResult recovery = DisplayStateRecovery.RestoreLeftoverState();
            if (recovery.RestoredAnything)
            {
                QueueStartupBalloon(
                    "Monitor Wellness",
                    "Monitor Wellness didn't close cleanly last time, so it has put your display back to normal before starting.",
                    ToolTipIcon.Info);
            }
            if (recovery.BrightnessStillPending > 0)
            {
                QueueStartupBalloon(
                    "Monitor Wellness",
                    "A monitor's own brightness may still be lower than before and couldn't be restored automatically. If it looks dim, raise it from the monitor's own menu.",
                    ToolTipIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            // Recovery is best-effort and must never be the reason the app fails to start.
            DebugLog.Write($"DisplayStateRecovery failed at startup: {ex}");
        }

        startup.Mark("display recovery");
        _gammaManager = new GammaControllerManager();
        _overlay = new OverlayController { ExcludeFromCapture = _settings.ExcludeOverlayFromCapture };
        _migraine = new MigraineModeController(_gammaManager, _overlay, _settings, () => ComputeScheduleTarget(), FullscreenDetector.IsForegroundWindowLikelyFullscreen);
        _migraine.StateChanged += OnMigraineStateChanged;
        _migraine.PossibleFullscreenConflict += () => ShowTrayBalloon(
            10_000,
            "Monitor Wellness",
            "Migraine relief turned on, but a full-screen app may be hiding it. Try switching to windowed mode to see the full effect.",
            ToolTipIcon.Warning);
        _migraine.RatingRequested += ShowMigraineRatingPrompt;
        _displayApplier = new DisplayApplier(_settings, _gammaManager, _overlay, _hardwareBrightness);
        _gammaManager.TopologyChanged += () =>
        {
            MigrateMonitorSettings(); // a monitor seen for the first time may still be stored under its old display name
            RequestDisplayReapply("the display layout changed");
        };
        _displayApplier.HardwareBrightnessChanged += RunScheduleTick;
        _displayApplier.CompatibilityFallbackStarted += OnCompatibilityFallbackStarted;
        _breakReminder = new BreakReminderService(
            _settings,
            () => _migraine?.IsActive == true,
            (milliseconds, message, icon, onClick) => ShowTrayBalloon(milliseconds, "Monitor Wellness", message, icon, clickAction: onClick),
            startBreak: ShowBreakFocusWindow,
            showPrompt: ShowBreakPrompt);

        startup.Mark("display controllers and services");
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        _iconOff = LoadEmbeddedIcon("MonitorWellness.Assets.migraine_off.ico");
        _iconOn = LoadEmbeddedIcon("MonitorWellness.Assets.migraine_on.ico");

        _trayIcon = new NotifyIcon
        {
            Icon = _iconOff,
            Visible = true,
            Text = "Monitor Wellness"
        };
        _trayIcon.BalloonTipClicked += (_, _) =>
        {
            if (_pendingBalloonClickAction is Action action)
            {
                _pendingBalloonClickAction = null;
                try { action(); }
                catch (Exception ex) { DebugLog.Write($"Balloon click action failed: {ex.Message}"); }
                return;
            }
            if (_pendingBalloonClickUrl is not string url)
                return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { DebugLog.Write($"Opening update URL failed: {ex.Message}"); }
        };
        _trayIcon.BalloonTipClosed += (_, _) =>
        {
            _pendingBalloonClickUrl = null;
            _pendingBalloonClickAction = null;
        };

        startup.Mark("tray icon");
        if (HdrDetector.IsAnyDisplayHdrEnabled())
        {
            // This app's gamma-ramp approach is documented to interact unpredictably with
            // Windows' HDR tone-mapping pipeline — untested against HDR displays.
            DebugLog.Write("An active display has Windows HDR (advanced color) enabled — gamma ramp behavior here is unverified");
            QueueStartupBalloon(
                "Monitor Wellness",
                _settings.PreferOverlayOnlyOnCompatibilityDisplays
                    ? "One of your displays has Windows HDR turned on. Colour changes aren't reliable on HDR displays, so Monitor Wellness only dims that display and doesn't warm its colour. Turn HDR off for that display if you want colour warmth too."
                    : "One of your displays has Windows HDR turned on. Color/brightness adjustments haven't been tested against HDR displays and may not look right — if something seems off, try turning HDR off for this monitor.",
                ToolTipIcon.Warning);
        }

        startup.Mark("HDR check");
        if (NightLightDetector.IsFluxRunning())
        {
            // f.lux is this app's own predecessor and writes to the exact same last-write-wins
            // gamma ramp state — a real, likely-to-occur conflict for anyone who migrated from
            // it without uninstalling.
            DebugLog.Write("f.lux appears to be running — likely gamma ramp conflict");
            QueueStartupBalloon(
                "Monitor Wellness",
                "Another screen-color app (f.lux) is also running. Using two at once can cause flickering — we'd suggest closing one.",
                ToolTipIcon.Warning);
        }

        startup.Mark("f.lux check");
        RegisterEmergencyRestoreHotkey();
        RebuildHotkey(isStartup: true);

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

        startup.Mark("hotkeys");
        _trayIcon.ContextMenuStrip = BuildTrayMenu();
        startup.Mark("tray menu");

        _timer = new DispatcherTimer { Interval = ScheduleTickInterval };
        _timer.Tick += (_, _) => RunScheduleTick();
        _timer.Start();

        StartApplicationRuleMonitoring();
        startup.Mark("timers and rules");

        RunScheduleTick();
        startup.Mark("first schedule tick");
        _breakReminder.Rebuild();

        // One reminder, later than the first run (onboarding already asks), for anyone who skipped
        // setting a location. The status line, tray tooltip and Settings banner keep saying it.
        if (_settings.HasCompletedOnboarding
            && !_settings.LocationReminderShown
            && LocationStatus.IsUsingUnconfirmedDefault(_settings))
        {
            QueueStartupBalloon("Monitor Wellness", LocationStatus.ReminderBalloon, ToolTipIcon.Info);
            _settings.LocationReminderShown = true;
            try
            {
                SettingsStore.Save(_settings);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                DebugLog.Write($"Couldn't persist LocationReminderShown: {ex.Message}");
            }
        }

        // On a first run, let onboarding take the screen by itself before any detection
        // balloons start appearing — see the queue/drain pair below.
        if (!_settings.HasCompletedOnboarding)
            ShowFirstRunOnboarding();
        else
            StartDrainingStartupBalloons();

        startup.Mark("onboarding and balloons");
        _startupFinished = true;
        _overlay.PrewarmWhenIdle(); // build the hidden overlay windows in idle time, so showing one later is instant
        StartMemoryTrimming();
        StartResetWatch();
        _ = CheckForUpdatesIfDueAsync();

        // A tray app can run for weeks. The check itself still runs at most once a day
        // (LastUpdateCheckUtc); this just gives a long-running session the chance to do it.
        _updateCheckTimer = new DispatcherTimer { Interval = UpdateCheckPollInterval };
        _updateCheckTimer.Tick += (_, _) => _ = CheckForUpdatesIfDueAsync();
        _updateCheckTimer.Start();

        startup.Mark("remaining startup");
        DebugLog.Write(startup.Summary());
        foreach (var (step, duration) in startup.SlowSteps(TimeSpan.FromSeconds(1)))
            DebugLog.Write($"Startup was slow at \"{step}\": {duration.TotalSeconds:0.0} s.");
    }

    /// <summary>
    /// Per-monitor settings used to be stored against the Windows display name, which Windows
    /// renumbers. Converts any such entry to the monitor's physical identity once that monitor is
    /// connected, and saves if anything changed. (See MonitorKeys.)
    /// </summary>
    private void MigrateMonitorSettings()
    {
        try
        {
            if (!MonitorKeys.MigrateLegacyNames(_settings, MonitorEnumerator.GetActiveMonitors(forceRefresh: true)))
                return;

            DebugLog.Write("Per-monitor settings now follow each monitor's hardware identity instead of its Windows display number.");
            SettingsStore.Save(_settings);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            DebugLog.Write($"Couldn't save migrated per-monitor settings: {ex.Message}");
        }
    }

    /// <summary>
    /// A restart starts the replacement process while this one is still shutting down; the
    /// replacement waits here (briefly) so it does not mistake the old instance for "already running".
    /// </summary>
    private static void WaitForPreviousInstance(int processId)
    {
        try
        {
            using var previous = System.Diagnostics.Process.GetProcessById(processId);
            DebugLog.Write($"Restart: waiting for the previous instance (process {processId}) to exit.");
            if (!previous.WaitForExit(20_000))
                DebugLog.Write("Restart: the previous instance did not exit within 20 seconds; carrying on anyway.");
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            DebugLog.Write($"Restart: couldn't wait for the previous instance: {ex.Message}");
        }
    }

    /// <summary>Starts a replacement instance that waits for this one to exit, then exits this one (which gives the display back first).</summary>
    private void RestartApplication()
    {
        try
        {
            string exePath = Environment.ProcessPath ?? throw new InvalidOperationException("The path of this program is unknown.");
            DebugLog.Write("Restart requested from the tray menu.");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exePath, StartupArguments.BuildRestartArguments(Environment.ProcessId))
            {
                UseShellExecute = false,
                WorkingDirectory = System.IO.Path.GetDirectoryName(exePath) ?? "",
            })?.Dispose();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            DebugLog.Write($"Restart failed, staying open: {ex.Message}");
            System.Windows.MessageBox.Show(
                "Monitor Wellness couldn't restart itself. It is still running.",
                "Monitor Wellness",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Shutdown();
    }

    /// <summary>
    /// Windows is signing out or shutting down. Hand the displays back immediately, before anything
    /// else, since the process may be ended soon after; the normal exit path then repeats it harmlessly.
    /// The log lines here and in OnExit show, after the fact, how that sequence went.
    /// </summary>
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        DebugLog.Write($"Windows is ending the session ({e.ReasonSessionEnding}); giving the displays back now.");
        try
        {
            _hardwareBrightness.RestoreAll(TimeSpan.FromSeconds(1.5));
            _gammaManager?.RestoreAllToBaseline();
            _overlay?.Clear();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"Giving the displays back at session end failed: {ex.Message}");
        }

        base.OnSessionEnding(e);
    }

    private const string RestoreSwitch = "--restore";

    /// <summary>
    /// <c>MonitorWellness.exe --restore</c>: a standalone recovery path that does not start the
    /// tray app. Puts every display's colour ramp and any hardware brightness this app changed
    /// back to normal, tells the person what happened, and exits. Usable when the tray icon is
    /// gone (for example after the app was killed while the screen was dimmed).
    /// </summary>
    private static void RunRestoreOnly()
    {
        DebugLog.Write("--restore: restoring display state, then exiting");
        string message;
        MessageBoxImage icon = MessageBoxImage.Information;
        try
        {
            DisplayRecoveryResult result = DisplayStateRecovery.RestoreForUser();
            message = $"Your display has been put back to normal ({result.GammaRestored} display(s) reset"
                + (result.BrightnessRestored > 0 ? $", {result.BrightnessRestored} monitor brightness setting(s) restored" : "")
                + ").";
            if (result.BrightnessStillPending > 0)
            {
                message += "\n\nA monitor's own brightness couldn't be restored automatically. If it still looks dim, raise it from the monitor's own menu.";
                icon = MessageBoxImage.Warning;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"--restore failed: {ex}");
            message = "Couldn't restore the display automatically. Restart Windows, or use the monitor's own menu to reset brightness and colour.";
            icon = MessageBoxImage.Warning;
        }

        System.Windows.MessageBox.Show(message, "Monitor Wellness — Restore display", MessageBoxButton.OK, icon);
    }

    /// <summary>
    /// Fire-and-forget: runs the opt-in update check at most once/day (AppSettings.
    /// LastUpdateCheckUtc), regardless of how often the app is actually launched/restarted in
    /// between. Deliberately independent of the startup-balloon queue above — this can take a
    /// few seconds (network round trip) and has nothing time-sensitive about when it shows up,
    /// unlike the HDR/f.lux/hotkey conflict balloons that matter most right at launch.
    /// </summary>
    private async Task CheckForUpdatesIfDueAsync()
    {
        if (!_settings.CheckForUpdatesEnabled)
            return;

        if (_settings.LastUpdateCheckUtc is DateTime last && DateTime.UtcNow - last < UpdateCheckInterval)
            return;

        var result = await UpdateChecker.CheckForUpdateAsync(_settings.UpdateNotifyPreReleases);
        var update = result.Update;

        // A failed check (offline at startup, a timeout) isn't recorded as done, so the next
        // poll or launch retries it instead of the app going a whole day without checking.
        if (result.Failed)
            return;

        _settings.LastUpdateCheckUtc = DateTime.UtcNow;
        try
        {
            SettingsStore.Save(_settings);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // This is a fire-and-forget async method (`_ = CheckForUpdatesIfDueAsync()`) —
            // an unobserved exception here wouldn't crash the app, but it also wouldn't be
            // logged anywhere, so a real save failure would go completely unnoticed.
            DebugLog.Write($"Couldn't persist LastUpdateCheckUtc: {ex.Message}");
        }

        if (update is null || _trayIcon is null)
            return;

        // One balloon per release per session; the daily re-check would otherwise repeat it.
        if (string.Equals(_lastNotifiedUpdateTag, update.TagName, StringComparison.Ordinal))
            return;
        _lastNotifiedUpdateTag = update.TagName;

        DebugLog.Write($"UpdateChecker: newer version available ({update.TagName})");
        ShowTrayBalloon(
            10_000,
            "Monitor Wellness",
            $"Monitor Wellness {update.Version.ToString(3)}{(update.IsPreRelease ? " (pre-release)" : "")} is available (you have {typeof(App).Assembly.GetName().Version?.ToString(3)}). Click here to open the release page.",
            ToolTipIcon.Info,
            clickUrl: update.ReleaseUrl.ToString());
    }

    /// <summary>Shows the real first-run onboarding flow — the only path that persists HasCompletedOnboarding. Reopening this content later from the tray menu was replaced by the distinct About/Troubleshooting windows (see ShowAboutWindow/ShowTroubleshootingWindow) rather than resurfacing the welcome-voiced wizard, per MonitorWellness_UX_Accessibility_Audit.html §2.1/§6 (P1).</summary>
    private void ShowFirstRunOnboarding()
    {
        var onboarding = new OnboardingWindow(OpenSettingsWindow);
        onboarding.Closed += (_, _) =>
        {
            _settings.HasCompletedOnboarding = true;
            try
            {
                SettingsStore.Save(_settings);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                // A save failure here must not also block the startup balloon queue (HDR,
                // f.lux conflict, hotkey conflict, auto-start drift) — those are independent
                // of whether HasCompletedOnboarding persisted.
                DebugLog.Write($"Couldn't persist onboarding completion: {ex.Message}");
            }
            StartDrainingStartupBalloons();
        };
        onboarding.Show();
        onboarding.Activate();
    }

    private static void ShowAboutWindow()
    {
        var window = new AboutWindow();
        window.Show();
        window.Activate();
    }

    private static void ShowTroubleshootingWindow()
    {
        var window = new TroubleshootingWindow();
        window.Show();
        window.Activate();
    }

    private void ShowDisplayCapabilityWindow()
    {
        var window = new DisplayCapabilityWindow(_settings);
        window.Show();
        window.Activate();
    }

    /// <summary>
    /// Shows a balloon and records what clicking it should do. Whatever an earlier balloon wanted
    /// is forgotten first, so clicking a later balloon can never open an earlier one's release page.
    /// </summary>
    private void ShowTrayBalloon(int milliseconds, string title, string message, ToolTipIcon icon, string? clickUrl = null, Action? clickAction = null)
    {
        if (_trayIcon is null)
            return;

        _pendingBalloonClickUrl = clickUrl;
        _pendingBalloonClickAction = clickAction;
        _trayIcon.ShowBalloonTip(milliseconds, title, message, icon);
    }

    /// <summary>Queues a startup-detection balloon instead of showing it immediately — see the field doc comment on _pendingStartupBalloons for why.</summary>
    private void QueueStartupBalloon(string title, string message, ToolTipIcon icon) => _pendingStartupBalloons.Enqueue((title, message, icon));

    private bool _startupBalloonsReleased;

    private void StartDrainingStartupBalloons()
    {
        _startupBalloonsReleased = true;
        ShowNextStartupBalloon();
    }

    /// <summary>
    /// For a notice whose answer arrives after start-up (the auto-start check now runs in the background):
    /// queued like the others, and shown straight away if the queue has already been let go (not while the
    /// first-run onboarding still has the screen).
    /// </summary>
    private void QueueLateStartupBalloon(string title, string message, ToolTipIcon icon)
    {
        QueueStartupBalloon(title, message, icon);
        if (_startupBalloonsReleased && _startupBalloonTimer?.IsEnabled != true)
            ShowNextStartupBalloon();
    }

    private void ShowNextStartupBalloon()
    {
        if (_pendingStartupBalloons.Count == 0)
            return;

        var (title, message, icon) = _pendingStartupBalloons.Dequeue();
        ShowTrayBalloon(10_000, title, message, icon);

        if (_pendingStartupBalloons.Count == 0)
            return;

        _startupBalloonTimer?.Stop();
        _startupBalloonTimer = new DispatcherTimer { Interval = StartupBalloonSpacing };
        _startupBalloonTimer.Tick += (_, _) =>
        {
            _startupBalloonTimer!.Stop();
            ShowNextStartupBalloon();
        };
        _startupBalloonTimer.Start();
    }

    /// <summary>Opens %AppData%\MonitorWellness\ in Explorer — closes the gap where a user filing a bug report had to be told the exact path and find it manually (TECHNICAL_UX_REVIEW.md §7.2).</summary>
    private static void OpenLogsFolder()
    {
        string folder = AppDataFolder.FullPath;
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

    private void OpenSettingsWindow() => OpenSettingsWindow(null);

    private void OpenSettingsWindow(string? initialRuleProcessName)
    {
        if (_settingsWindow is not null)
        {
            if (!string.IsNullOrWhiteSpace(initialRuleProcessName))
                _settingsWindow.AddApplicationRuleTemplate(initialRuleProcessName);
            _settingsWindow.Activate();
            return;
        }

        // Set, then guaranteed to be cleared by the catch below if anything here throws
        // (constructing SettingsWindow does real work — monitor enumeration, an
        // embedded-resource load — either of which can throw). Without this, a failed
        // construction leaves _settingsWindow null (so no Closed handler ever exists to
        // clear the flag) with _overrides.SettingsPreviewActive stuck true, silently suspending the
        // normal schedule for the rest of the session.
        _overrides.SettingsPreviewActive = true;
        try
        {
            _settingsWindow = new SettingsWindow(_settings, _gammaManager!, _overlay!, OnSettingsSaved, ComputeStatusText);
            if (!string.IsNullOrWhiteSpace(initialRuleProcessName))
                _settingsWindow.AddApplicationRuleTemplate(initialRuleProcessName);
            _settingsWindow.Closed += (_, _) =>
            {
                _settingsWindow = null;
                _overrides.SettingsPreviewActive = false;
                RunScheduleTick(); // clears any live preview left on screen the instant the window closes
            };
            _settingsWindow.Show();
            _settingsWindow.Activate();
        }
        catch
        {
            _settingsWindow = null;
            _overrides.SettingsPreviewActive = false;
            throw;
        }
    }

    private void CaptureForegroundApplicationRule()
    {
        ForegroundApplicationDetector.ForegroundApplicationInfo? foregroundApplication = ForegroundApplicationDetector.TryGetForegroundApplication();
        if (foregroundApplication is null || string.Equals(foregroundApplication.ProcessName, "MonitorWellness", StringComparison.OrdinalIgnoreCase))
        {
            System.Windows.MessageBox.Show(
                "Bring the app you want to configure to the front, then choose Add Foreground App Rule from Monitor Wellness' tray menu.",
                "Monitor Wellness",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        OpenSettingsWindow(foregroundApplication.ProcessName);
        ShowTrayBalloon(
            4_000,
            "Monitor Wellness",
            $"Added {foregroundApplication.ProcessName}. Choose native display or a comfort plan, then Save.",
            ToolTipIcon.Info);
    }

    private void OnSettingsSaved()
    {
        DebugLog.Write("Settings saved from settings window — rebuilding hotkey");
        if (_overlay is not null)
            _overlay.ExcludeFromCapture = _settings.ExcludeOverlayFromCapture;
        RebuildHotkey();
        _breakReminder?.Rebuild();
        if (_focusBreakMenuItem is not null)
            _focusBreakMenuItem.Text = FocusBreakMenuText();
        // Deliberately not calling RunScheduleTick() here: the settings window's own preview
        // already has the correct saved values on screen right now (that's the whole point
        // of previewing before Save), and the window is about to close anyway, which reapplies
        // the schedule fresh from the just-saved settings.
    }

    private BreakPromptWindow? _breakPrompt;

    /// <summary>Shows the break reminder prompt. False means it could not be shown and the caller should fall back to a balloon.</summary>
    private bool ShowBreakPrompt(Action<BreakPromptChoice> onChoice)
    {
        if (_breakPrompt is not null)
            return true; // one is already waiting for an answer

        try
        {
            var prompt = new BreakPromptWindow(onChoice, _settings.ExcludeOverlayFromCapture, _settings.BreakFocusSeconds);
            prompt.Closed += (_, _) => _breakPrompt = null;
            _breakPrompt = prompt;
            prompt.Show();
            return true;
        }
        catch (Exception ex)
        {
            _breakPrompt = null;
            DebugLog.Write($"Couldn't show the break prompt: {ex.Message}");
            return false;
        }
    }

    private void ShowBreakFocusWindow()
    {
        if (_breakFocusWindow is not null)
        {
            _breakFocusWindow.Activate();
            return;
        }

        _breakFocusWindow = new BreakFocusWindow(_settings.BreakFocusSeconds);
        _breakFocusWindow.Closed += (_, _) =>
        {
            if (_breakFocusWindow?.CountsAsBreak == true)
                _breakReminder?.NotifyBreakTaken();
            _breakFocusWindow = null;
        };
        _breakFocusWindow.Show();
        _breakFocusWindow.Activate();
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
        ShowTrayBalloon(
            4_000,
            "Monitor Wellness",
            nowActive ? "Migraine relief is on." : "Migraine relief is easing off — back to normal in about 20 seconds.",
            ToolTipIcon.None);

        if (_settings.PlaySoundOnMigraineToggle)
        {
            try { (nowActive ? System.Media.SystemSounds.Exclamation : System.Media.SystemSounds.Asterisk).Play(); }
            catch (Exception ex) { DebugLog.Write($"PlaySoundOnMigraineToggle failed: {ex.Message}"); }
        }
    }

    /// <summary>
    /// Shows the "how did that feel?" prompt (AppSettings.PromptForMigraineRating) — kept
    /// here rather than in MigraineModeController so that class stays free of a WPF window
    /// dependency it doesn't otherwise have; only appends a history event if the user actually
    /// answers (a skipped/auto-dismissed prompt leaves no trace). "Don't ask again" on the
    /// popup itself turns the setting off immediately, so opting out doesn't require finding
    /// the same checkbox in Settings afterward.
    /// </summary>
    private void ShowMigraineRatingPrompt()
    {
        var window = new MigraineRatingWindow((rating, dontAskAgain) =>
        {
            if (rating.HasValue)
                HistoryStore.Append(new HistoryEvent(DateTime.UtcNow, "MigraineRating", null, null, rating));

            if (dontAskAgain)
            {
                _settings.PromptForMigraineRating = false;
                try
                {
                    SettingsStore.Save(_settings);
                    DebugLog.Write("Migraine rating prompt: user chose 'Don't ask again'");
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
                {
                    DebugLog.Write($"Couldn't persist PromptForMigraineRating=false: {ex.Message}");
                }
            }
        });
        window.Show();
    }

    /// <summary>
    /// Disposes any existing hotkey and registers one from the current settings. Called at
    /// startup (isStartup: true, so a conflict balloon joins the startup queue instead of
    /// popping immediately alongside HDR/f.lux/auto-start-drift balloons and onboarding) and
    /// again after the settings window saves a rebind (isStartup: false — that's a direct
    /// response to the user's own action, so it should show right away).
    /// </summary>
    private void RebuildHotkey(bool isStartup = false)
    {
        uint modifiers = _settings.MigraineHotkeyModifiers;
        uint key = _settings.MigraineHotkeyKey;
        bool hadWorkingShortcut = _hotkey?.IsRegistered == true;
        if (HotkeyRebindPolicy.IsUnchanged(hadWorkingShortcut, _hotkeyModifiers, _hotkeyKey, modifiers, key))
            return;

        // Try the new shortcut BEFORE giving up the old one. Releasing first meant that changing to a
        // shortcut another app already owned left the person with no shortcut at all.
        var candidate = new GlobalHotkey(modifiers, key);
        candidate.Pressed += () =>
        {
            DebugLog.Write("Global hotkey pressed: toggling migraine mode");
            ToggleMigraineModeWithFeedback();
        };

        if (HotkeyRebindPolicy.ChooseAfterAttempt(hadWorkingShortcut, candidate.IsRegistered) == HotkeyRebindChoice.KeepPreviousBecauseNewFailed)
        {
            candidate.Dispose();
            DebugLog.Write("Hotkey rebind refused: the new shortcut is already used by another app; keeping the previous one.");
            ShowTrayBalloon(
                10_000,
                "Monitor Wellness",
                "That shortcut is already used by another app, so Monitor Wellness is keeping your previous migraine shortcut. Pick a different one in Settings.",
                ToolTipIcon.Warning);
            return;
        }

        _hotkey?.Dispose();
        _hotkey = candidate;
        _hotkeyModifiers = modifiers;
        _hotkeyKey = key;

        if (!_hotkey.IsRegistered && _trayIcon is not null)
        {
            // A silently-failed hotkey looks like the app just doesn't work — surface it
            // visibly rather than only logging it.
            const string title = "Monitor Wellness";
            const string message = "That migraine mode shortcut is already used by another app. Use the tray menu to trigger Migraine Mode, or pick a different shortcut in Settings.";
            if (isStartup)
                QueueStartupBalloon(title, message, ToolTipIcon.Warning);
            else
                ShowTrayBalloon(10_000, title, message, ToolTipIcon.Warning);
        }
    }

    /// <summary>Registers a fixed, independent recovery shortcut that cannot be changed in Settings.</summary>
    private void RegisterEmergencyRestoreHotkey()
    {
        _emergencyRestoreHotkey?.Dispose();
        _emergencyRestoreHotkey = new GlobalHotkey(EmergencyRestoreModifiers, EmergencyRestoreKey, EmergencyRestoreHotkeyId);
        _emergencyRestoreHotkey.Pressed += EmergencyRestoreDisplay;

        if (!_emergencyRestoreHotkey.IsRegistered)
            QueueStartupBalloon(
                "Monitor Wellness",
                "Emergency Restore Screen (Ctrl+Alt+Shift+R) is already used by another app. You can still use Emergency Restore Screen from this tray menu.",
                ToolTipIcon.Warning);
    }

    /// <summary>
    /// Returns every controlled display to an immediately visible state, then pauses normal
    /// scheduling so it cannot reapply a dim overlay before the user can recover.
    /// </summary>
    private void EmergencyRestoreDisplay()
    {
        DebugLog.Write("Emergency Restore Screen activated");
        if (_overrides.ManualComfortPlanName is not null)
        {
            DebugLog.Write($"Emergency Restore Screen cleared temporary comfort plan: {_overrides.ManualComfortPlanName}.");
            _overrides.ManualComfortPlanName = null;
        }
        _migraine?.RestoreImmediately();
        _displayApplier?.RestoreNative();
        PauseScheduleFor(EmergencyRestorePauseDuration);
        ShowTrayBalloon(
            10_000,
            "Monitor Wellness",
            "Your screen has been restored. The normal schedule is paused for one hour.",
            ToolTipIcon.Info);
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
            : diagnostics.Method != AutoStartManager.TaskSchedulerMethod
            ? "Auto-start is registered as a standard sign-in entry for your account. It starts Monitor Wellness each time you sign in to Windows; Windows keeps no last-run history for this kind of entry."
            : $"Status: {diagnostics.Status ?? "unknown"}\n" +
              $"Last Run Time: {diagnostics.LastRunTime ?? "unknown"}\n" +
              $"Last Result: {diagnostics.LastResult ?? "unknown"}\n" +
              $"Next Run Time: {diagnostics.NextRunTime ?? "unknown"}\n\n" +
              "\"Last Run Time\" only updates after an actual Windows logon triggers the task " +
              "-- it won't change just from toggling \"Start with Windows\" or running the app " +
              "manually. If you want to confirm auto-start survives a real reboot, restart " +
              "Windows normally and check this again afterward.";

        System.Windows.MessageBox.Show(message, "Monitor Wellness — Auto-start Diagnostics", MessageBoxButton.OK,
            diagnostics.IsRegistered ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private static async Task ExportDiagnosticBundleAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Monitor Wellness diagnostic bundle",
            Filter = "ZIP archive (*.zip)|*.zip",
            FileName = $"MonitorWellness-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            // Building the bundle probes every monitor over DDC/CI, which can take seconds; do it
            // off the UI thread so the tray and any open window stay responsive.
            string destination = dialog.FileName;
            await Task.Run(() => DiagnosticBundleService.Create(destination));
            System.Windows.MessageBox.Show(
                "Diagnostic bundle created. It contains technical environment data and debug.log, but not settings, location, history, or profiles. Review it before sharing.",
                "Monitor Wellness",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            // An explicit user action: report any failure instead of letting it vanish.
            DebugLog.Write($"Diagnostic bundle export failed: {ex}");
            System.Windows.MessageBox.Show($"Couldn't create the diagnostic bundle: {ex.Message}", "Monitor Wellness", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private DispatcherTimer? _memoryTrimTimer;

    private DispatcherTimer? _resetWatchTimer;
    private readonly ResetRepairThrottle _resetRepairThrottle = new();

    /// <summary>
    /// Another program (a game exiting, Windows Night Light, an HDR toggle) can reset the colour
    /// ramp without any display-change notification, which used to leave the screen wrong until
    /// the next 30-second tick. Every few seconds this reads each ramp (about 1.5 ms) and reapplies
    /// our colour only if it was changed. A throttle stops it from fighting another colour tool.
    /// </summary>
    private void StartResetWatch()
    {
        _resetWatchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _resetWatchTimer.Tick += (_, _) =>
        {
            if (_gammaManager is null || !_resetRepairThrottle.IsAllowed(DateTime.UtcNow))
                return;

            int repaired = _gammaManager.RepairResetDisplays();
            if (repaired > 0 && _resetRepairThrottle.RecordRepairs(repaired, DateTime.UtcNow))
                DebugLog.Write("Something keeps resetting the colour ramp (another colour tool?); the quick reset check is standing down for 10 minutes. The regular 30-second schedule still applies.");
        };
        _resetWatchTimer.Start();
    }
    private int _memoryTrimCount;

    /// <summary>Gives the idle app's resident memory back to Windows once startup has settled, and again every so often. See MemoryTrimmer.</summary>
    private void StartMemoryTrimming()
    {
        _memoryTrimTimer = new DispatcherTimer { Interval = MemoryTrimPolicy.FirstTrimDelay };
        _memoryTrimTimer.Tick += (_, _) =>
        {
            _memoryTrimTimer!.Interval = MemoryTrimPolicy.Interval;
            if (!MemoryTrimPolicy.ShouldTrimNow(_settingsWindow is not null))
                return;

            MemoryTrimmer.TrimResult result = MemoryTrimmer.Trim();
            _memoryTrimCount++;
            // The first trim, then about once an hour: enough to see it working without filling the log.
            if (_memoryTrimCount == 1 || _memoryTrimCount % 6 == 0)
                DebugLog.Write($"MemoryTrimmer: working set {result.BeforeBytes / (1024 * 1024)} MB -> {result.AfterBytes / (1024 * 1024)} MB (trim #{_memoryTrimCount}).");
        };
        _memoryTrimTimer.Start();
    }

    private static readonly TimeSpan DisplayReapplyDebounce = TimeSpan.FromMilliseconds(1500);
    private DispatcherTimer? _displayReapplyTimer;
    private bool _startupFinished;

    /// <summary>
    /// Reapplies the display shortly after something may have reset it (a display layout change, an
    /// unlock). Several signals usually arrive together, so one quiet reapply replaces the burst.
    /// </summary>
    private void RequestDisplayReapply(string reason)
    {
        DebugLog.Write($"Display reapply requested: {reason}");
        _displayReapplyTimer ??= new DispatcherTimer { Interval = DisplayReapplyDebounce };
        _displayReapplyTimer.Tick -= OnDisplayReapplyTimerTick;
        _displayReapplyTimer.Tick += OnDisplayReapplyTimerTick;
        _displayReapplyTimer.Stop();
        _displayReapplyTimer.Start();
    }

    private void OnDisplayReapplyTimerTick(object? sender, EventArgs e)
    {
        _displayReapplyTimer?.Stop();
        if (_migraine?.IsActive == true)
            _migraine.Reapply();
        else
            RunScheduleTick();
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (!CrashSafety.ShouldReapplyAfterSessionChange(e.Reason))
            return;

        // SystemEvents isn't guaranteed to raise on the UI thread.
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnSessionSwitch(sender, e));
            return;
        }
        RequestDisplayReapply($"the Windows session changed ({e.Reason})");
    }

    /// <summary>Tells the person (once startup is over, and the startup HDR balloon already covers the rest) that a display just went overlay-only because HDR was turned on.</summary>
    private void OnCompatibilityFallbackStarted(string deviceName, string reason, bool isHdr)
    {
        if (!isHdr || !_startupFinished || _trayIcon is null)
            return;

        string shortName = deviceName[(deviceName.LastIndexOf('\\') + 1)..];
        ShowTrayBalloon(
            10_000,
            "Monitor Wellness",
            $"Windows HDR is on for {shortName}, so Monitor Wellness is only dimming it and not changing its colour. Turn HDR off for that display to get colour warmth back.",
            ToolTipIcon.Info);
    }

    /// <summary>
    /// A background-thread exception is about to end the process. Log it (there was no record before)
    /// and make a last attempt to give the person their screen back, since nothing will restore it
    /// until the app is started again. The WPF overlay windows disappear with the process, so only
    /// the colour ramps and any hardware brightness need handling.
    /// </summary>
    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        DebugLog.Write($"UNHANDLED EXCEPTION (process terminating={args.IsTerminating}): {args.ExceptionObject}");
        if (!args.IsTerminating)
            return;

        int failures = CrashSafety.RunBestEffort(new (string, Action)[]
        {
            ("restore hardware brightness", () => _hardwareBrightness.RestoreAll()),
            ("restore colour ramps", () => _gammaManager?.RestoreAllToBaseline()),
        });
        DebugLog.Write(failures == 0
            ? "CrashSafety: display restored before exit."
            : $"CrashSafety: {failures} restore step(s) failed; the recovery journal will finish the job at the next start.");
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
        _hardwareBrightness.RefreshAfterWake();

        // Deadlines are enforced by the clock: timers set for hours don't reliably count time
        // spent asleep, so an auto-revert or pause that ended during sleep is applied now.
        _migraine?.CheckAutoRevertDue(DateTime.UtcNow);

        if (_migraine?.IsActive == true)
            _migraine.Reapply(); // keeps Mild/Full, the auto-revert deadline and history as they were
        else
            RunScheduleTick();

        // Monitors often finish coming back a few seconds after the resume notification, and
        // Windows may reset the colour ramp as they do; one debounced reapply covers that.
        RequestDisplayReapply("the system resumed from sleep");
    }

    private void OnMigraineStateChanged()
    {
        if (_trayIcon is null || _migraine is null) return;

        // The tray-icon swap below is easy to miss — especially for an activation nobody
        // intended (e.g. the hotkey colliding with another app's own shortcut for the same
        // combination), which otherwise silently suspends the whole day/night schedule with
        // no obvious signal until someone happens to notice. Fires once per false->true
        // transition, not on every StateChanged (which also fires on deactivate/fade-complete).
        if (_migraine.IsActive && !_migraineWasActive)
        {
            ShowTrayBalloon(
                6_000,
                "Monitor Wellness",
                _migraine.IsMild
                    ? "Migraine relief turned ON (mild) — the normal day/night schedule is paused until you turn it off."
                    : "Migraine relief turned ON — the normal day/night schedule is paused until you turn it off.",
                ToolTipIcon.Info);
        }
        _migraineWasActive = _migraine.IsActive;

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
    /// Plain-language answer to "what is this app doing to my screen right now" — the
    /// persistent status line pinned at the top of the Settings window (CurrentStatusText)
    /// reads this on load and on a short refresh timer, so a user isn't limited to hovering the
    /// tray icon or catching a transient balloon to find out. See
    /// MonitorWellness_UX_Accessibility_Audit.html §2.1/§2.2/§6 (P0). Underlying state
    /// (IsActive, AutoRevertAtUtc, _overrides.PauseUntilUtc) already existed — this is new UI/plain-
    /// language surfacing of it, not new state.
    /// </summary>
    private string ComputeStatusText()
    {
        if (_migraine?.IsActive == true)
        {
            string mildSuffix = _migraine.IsMild ? " (mild)" : "";
            return _migraine.AutoRevertAtUtc is DateTime revertAt
                ? $"Currently: Migraine relief ON{mildSuffix} — turns off automatically at {revertAt.ToLocalTime():h:mm tt}."
                : $"Currently: Migraine relief ON{mildSuffix} — stays on until you turn it off.";
        }
        if (_migraine?.IsFadingOut == true)
            return "Currently: Migraine relief easing off — back to normal shortly.";

        if (_overrides.DescribeStatus() is string overrideStatus)
            return overrideStatus;

        double elevation = ScheduleSource.GetElevationDegrees(_settings, DateTime.UtcNow, DateTime.Now);
        string phase = elevation <= ScheduleCurve.DeepNightThresholdDeg ? "Deep Night mode"
            : elevation <= ScheduleCurve.NightThresholdDeg ? "Night mode"
            : elevation >= ScheduleCurve.DayThresholdDeg ? "Day mode"
            : "transitioning between day and night";
        string unconfirmedLocation = LocationStatus.IsUsingUnconfirmedDefault(_settings) ? LocationStatus.StatusSuffix : "";
        return $"Currently: {phase}.{unconfirmedLocation}";
    }

    /// <summary>
    /// Pure computation of the current schedule target — no side effects, so migraine mode's
    /// fade-out can call it repeatedly to know what to fade toward. Includes the deep-night
    /// bedtime-like phase (see ScheduleCurve/AppSettings doc comments): once past civil
    /// twilight, brightness keeps dropping toward DeepNightBrightness and the dim overlay's
    /// own color shifts from black toward DeepNightOverlayColorHex, approximating the extra
    /// warmth research supports close to bedtime that gamma ramp can't reach on its own.
    /// </summary>
    private (int Kelvin, IReadOnlyDictionary<string, double> BrightnessByDevice, System.Windows.Media.Color DimColor) ComputeScheduleTarget(SensoryComfortSchedule? scheduleOverride = null)
    {
        // The pure schedule math lives in ScheduleTargetCalculator. Only the stateful/hardware
        // parts stay here: the ambient sensor read and its smoother. Scaled by dayFactor so
        // ambient has zero effect at night; any failure to read the sensor (including simply
        // not having one, the common case) leaves brightness untouched.
        Func<double, double?>? ambientAdjustment = null;
        if (_settings.MatchAmbientLight)
        {
            ambientAdjustment = dayFactor =>
            {
                double? lux = AmbientLightSensor.TryGetCurrentLux();
                return lux.HasValue
                    ? _ambientLightSmoother.Update(AmbientLightAdapter.ComputeBrightnessAdjustment(lux.Value) * dayFactor, DateTime.UtcNow)
                    : null;
            };
        }
        else
        {
            _ambientLightSmoother.Reset();
        }

        double elevation = ScheduleSource.GetElevationDegrees(_settings, DateTime.UtcNow, DateTime.Now);
        ScheduleTarget target = ScheduleTargetCalculator.Compute(
            new ScheduleTargetInputs(
                DayKelvin: scheduleOverride?.DayKelvin ?? _settings.DayKelvin,
                NightKelvin: scheduleOverride?.NightKelvin ?? _settings.NightKelvin,
                DayBrightness: scheduleOverride?.DayBrightness ?? _settings.DayBrightness,
                NightBrightness: scheduleOverride?.NightBrightness ?? _settings.NightBrightness,
                DeepNightBrightness: scheduleOverride?.DeepNightBrightness ?? _settings.DeepNightBrightness,
                SolarElevationDegrees: elevation,
                BedtimeLocal: _settings.BedtimeLocal,
                WakeTimeLocal: _settings.WakeTimeLocal,
                NowLocal: DateTime.Now,
                DeepNightOverlayColorHex: _settings.DeepNightOverlayColorHex),
            ambientAdjustment);
        int kelvin = target.Kelvin;
        double globalBrightness = target.GlobalBrightness;
        var dimColor = target.DimColor;

        // Target set comes from the overlay/active displays, not gamma controllers: a display
        // whose gamma-ramp DC could not be created still has a working overlay and must still
        // be dimmed. Gamma controllers are unioned in only so hardware-brightness targets that
        // previously existed keep their entry.
        var activeMonitors = MonitorEnumerator.GetActiveMonitors();
        var primaryMonitorNames = activeMonitors
            .Where(monitor => monitor.IsPrimary)
            .Select(monitor => monitor.DeviceName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targetDevices = new HashSet<string>(_overlay?.DeviceNames ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        targetDevices.UnionWith(activeMonitors.Select(monitor => monitor.DeviceName));
        targetDevices.UnionWith((_gammaManager?.Controllers ?? Array.Empty<GammaRampController>()).Select(controller => controller.DeviceName));

        // Per-monitor settings are stored against each monitor's physical identity; resolve them
        // to the display names the schedule is applied by.
        var (excludedDevices, dimMultipliers) = MonitorKeys.ResolveDimSettings(_settings, targetDevices, activeMonitors);
        var brightnessByDevice = ScheduleBrightnessTargets.Build(
            targetDevices,
            globalBrightness,
            excludedDevices,
            dimMultipliers,
            primaryMonitorNames);

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
        _overrides.PauseUntilUtc = DateTime.UtcNow + duration;
        DebugLog.Write($"Schedule paused until {_overrides.PauseUntilUtc:yyyy-MM-dd HH:mm} UTC");

        if (_settings.HistoryTrackingEnabled)
            HistoryStore.Append(new HistoryEvent(DateTime.UtcNow, "SchedulePaused", null, (int)duration.TotalSeconds));

        _pauseTimer?.Stop();
        _pauseTimer = new DispatcherTimer { Interval = duration };
        _pauseTimer.Tick += (_, _) => ResumeSchedule();
        _pauseTimer.Start();

        if (_resumeScheduleMenuItem is not null)
            _resumeScheduleMenuItem.Enabled = true;
        if (_trayIcon is not null)
            _trayIcon.Text = $"Monitor Wellness — paused until {_overrides.PauseUntilUtc.Value.ToLocalTime():HH:mm}";
    }

    private void ResumeSchedule()
    {
        _pauseTimer?.Stop();
        _pauseTimer = null;
        _overrides.PauseUntilUtc = null;
        DebugLog.Write("Schedule resumed");

        if (_resumeScheduleMenuItem is not null)
            _resumeScheduleMenuItem.Enabled = false;

        RunScheduleTick();
    }

    /// <summary>
    /// A tray-level, non-persistent way to use the same conservative built-in plans available
    /// in Settings. This is intentionally separate from saved profiles: it is a quick personal
    /// comfort choice, and returning to the saved schedule is always one click away.
    /// </summary>
    private ToolStripMenuItem BuildQuickComfortPlansMenu()
    {
        var menu = new ToolStripMenuItem("Temporary &Comfort Plan");
        foreach (string plan in new[]
        {
            SensoryComfortPlans.Balanced,
            SensoryComfortPlans.Reading,
            SensoryComfortPlans.ColourCritical,
            SensoryComfortPlans.EarlySensitivity,
            SensoryComfortPlans.Recovery,
        })
        {
            var item = new ToolStripMenuItem(plan) { Tag = plan };
            item.Click += (_, _) => SelectManualComfortPlan(plan);
            menu.DropDownItems.Add(item);
        }
        menu.DropDownItems.Add(new ToolStripSeparator());
        menu.DropDownItems.Add("Return to saved schedule", null, (_, _) => ClearManualComfortPlan());
        menu.DropDownOpening += (_, _) =>
        {
            foreach (ToolStripMenuItem item in menu.DropDownItems.OfType<ToolStripMenuItem>())
            {
                if (item.Tag is string plan)
                    item.Checked = string.Equals(plan, _overrides.ManualComfortPlanName, StringComparison.Ordinal);
            }
        };
        return menu;
    }

    private void SelectManualComfortPlan(string plan)
    {
        if (!SensoryComfortPlans.IsSupported(plan))
            return;

        _overrides.ManualComfortPlanName = plan;
        DebugLog.Write($"Temporary comfort plan selected: {plan}.");
        RunScheduleTick();
    }

    private void ClearManualComfortPlan()
    {
        if (_overrides.ManualComfortPlanName is null)
            return;

        DebugLog.Write($"Temporary comfort plan cleared: {_overrides.ManualComfortPlanName}.");
        _overrides.ManualComfortPlanName = null;
        RunScheduleTick();
    }

    /// <summary>
    /// Ends the schedule pause if its end time has passed according to the clock. The pause
    /// timer alone is not enough: it may not account for time spent asleep. Returns true if the
    /// pause was just ended (ResumeSchedule has already applied the schedule).
    /// </summary>
    private bool ExpirePauseIfDue()
    {
        if (!SchedulePause.IsExpired(_overrides.PauseUntilUtc, DateTime.UtcNow))
            return false;

        DebugLog.Write("Schedule pause ended (detected by clock check)");
        ResumeSchedule();
        return true;
    }

    private void RunScheduleTick()
    {
        _overlay?.ReassertTopmost();
        _migraine?.CheckAutoRevertDue(DateTime.UtcNow);
        if (ExpirePauseIfDue())
            return;

        ScheduleDecision decision = SchedulePolicy.Decide(new ScheduleDecisionInputs(
            MigraineSuspendsSchedule: _migraine?.SuspendsNormalSchedule == true,
            SettingsPreviewActive: _overrides.SettingsPreviewActive,
            Paused: _overrides.PauseUntilUtc.HasValue,
            FullscreenGuardEnabled: _settings.RestoreNativeDisplayInFullscreen,
            IsForegroundFullscreen: FullscreenDetector.IsForegroundWindowLikelyFullscreen,
            FindForegroundRule: () =>
            {
                if (!_settings.ApplicationComfortRules.Any(rule => rule.IsEnabled))
                    return null; // no rule can match, so don't look up the foreground process at all
                ForegroundApplicationDetector.ForegroundApplicationInfo? foreground = ForegroundApplicationDetector.TryGetForegroundApplication();
                return ApplicationComfortRules.FindForegroundRule(_settings.ApplicationComfortRules, foreground?.ProcessName, foreground?.WindowTitle);
            },
            ManualComfortPlanName: _overrides.ManualComfortPlanName));

        // Migraine mode, a live settings preview, or an explicit pause owns the gamma ramp + overlay right now.
        if (decision.Action == ScheduleActionKind.Skip)
            return;

        if (decision.Action == ScheduleActionKind.FullscreenGuard)
        {
            _overrides.ActiveComfortPlanRuleDescription = null;
            ActivateFullscreenPresentationGuard();
            return;
        }
        if (_overrides.FullscreenPresentationGuardActive)
        {
            DebugLog.Write("Fullscreen presentation guard ended; resuming the selected schedule or application rule.");
            _overrides.FullscreenPresentationGuardActive = false;
        }
        if (decision.Action == ScheduleActionKind.NativeDisplayRule)
        {
            _overrides.ActiveComfortPlanRuleDescription = null;
            ActivateNativeDisplayApplicationRule(decision.NativeDisplayRule!);
            return;
        }

        if (_overrides.ActiveNativeDisplayRuleProcessName is not null)
        {
            DebugLog.Write($"Application comfort rule ended for {_overrides.ActiveNativeDisplayRuleProcessName}; resuming the schedule.");
            _overrides.ActiveNativeDisplayRuleProcessName = null;
        }

        SensoryComfortSchedule? scheduleOverride = decision.Override;
        switch (decision.OverrideSource)
        {
            case ScheduleOverrideSource.ManualPlan:
                _overrides.ActiveComfortPlanRuleDescription = null;
                break;
            case ScheduleOverrideSource.ApplicationRule:
                if (!string.Equals(_overrides.ActiveComfortPlanRuleDescription, decision.RuleDescription, StringComparison.Ordinal))
                    DebugLog.Write($"Application comfort rule active: {decision.RuleDescription}.");
                _overrides.ActiveComfortPlanRuleDescription = decision.RuleDescription;
                break;
            default:
                if (_overrides.ActiveComfortPlanRuleDescription is not null)
                {
                    DebugLog.Write($"Application comfort rule ended for {_overrides.ActiveComfortPlanRuleDescription}; resuming the saved schedule.");
                    _overrides.ActiveComfortPlanRuleDescription = null;
                }
                break;
        }

        var (kelvin, brightnessByDevice, dimColor) = ComputeScheduleTarget(scheduleOverride);

        _displayApplier?.Apply(kelvin, brightnessByDevice, dimColor);

        if (_trayIcon is not null && _migraine?.IsActive != true)
        {
            double elevation = ScheduleSource.GetElevationDegrees(_settings, DateTime.UtcNow, DateTime.Now);
            // NotifyIcon.Text is limited to 63 characters; this stays well under it.
            string locationHint = LocationStatus.IsUsingUnconfirmedDefault(_settings) ? " · set location" : "";
            string source = ScheduleSource.UsesFixedTimes(_settings) ? "fixed times" : $"sun {elevation:F1}°";
            _trayIcon.Text = $"Monitor Wellness — {kelvin}K, {source}{locationHint}";
        }
    }

    private void StartApplicationRuleMonitoring()
    {
        _applicationRuleTimer = new DispatcherTimer { Interval = ApplicationRulePollInterval };
        _applicationRuleTimer.Tick += (_, _) =>
        {
            bool anyRuleStateToClear = _overrides.AnyRuleStateActive;
            if (!ApplicationRuleRefreshPolicy.ShouldPollForeground(_settings.RestoreNativeDisplayInFullscreen, _settings.ApplicationComfortRules, anyRuleStateToClear))
            {
                _lastForegroundApplicationContext = null; // nothing to watch for: skip the foreground lookup
                return;
            }

            ForegroundApplicationDetector.ForegroundApplicationInfo? foregroundApplication = ForegroundApplicationDetector.TryGetForegroundApplication();
            bool fullscreenGuardShouldApply = _settings.RestoreNativeDisplayInFullscreen
                && FullscreenDetector.IsForegroundWindowLikelyFullscreen();
            string? context = foregroundApplication is null
                ? null
                : $"{foregroundApplication.ProcessName}\u001f{foregroundApplication.WindowTitle}\u001f{fullscreenGuardShouldApply}";
            if (string.Equals(_lastForegroundApplicationContext, context, StringComparison.OrdinalIgnoreCase))
                return;

            _lastForegroundApplicationContext = context;
            ApplicationComfortRule? matchingRule = ApplicationComfortRules.FindForegroundRule(
                _settings.ApplicationComfortRules,
                foregroundApplication?.ProcessName,
                foregroundApplication?.WindowTitle);
            if (!ApplicationRuleRefreshPolicy.ShouldRefresh(
                matchingRule,
                _overrides.ActiveNativeDisplayRuleProcessName is not null,
                _overrides.ActiveComfortPlanRuleDescription is not null,
                fullscreenGuardShouldApply,
                _overrides.FullscreenPresentationGuardActive))
            {
                // The regular schedule timer will handle its next gradual adjustment. Do not
                // write the same gamma/overlay state merely because an unrelated window opened.
                VisualStabilityDiagnostics.RecordForegroundDisplayWriteAvoided();
                return;
            }

            RunScheduleTick();
        };
        _applicationRuleTimer.Start();
    }

    private void ActivateFullscreenPresentationGuard()
    {
        if (_overrides.FullscreenPresentationGuardActive)
            return;

        _overrides.FullscreenPresentationGuardActive = true;
        DebugLog.Write("Fullscreen presentation guard active: restoring native display state.");
        _displayApplier?.RestoreNative();
        if (_trayIcon is not null)
            _trayIcon.Text = "Monitor Wellness — native display for fullscreen app";
    }

    private void ActivateNativeDisplayApplicationRule(ApplicationComfortRule rule)
    {
        if (string.Equals(_overrides.ActiveNativeDisplayRuleProcessName, rule.ProcessName, StringComparison.OrdinalIgnoreCase))
            return;

        _overrides.ActiveNativeDisplayRuleProcessName = rule.ProcessName;
        DebugLog.Write($"Application comfort rule active for {rule.ProcessName}: restoring native display state.");
        _displayApplier?.RestoreNative();
        if (_trayIcon is not null)
            _trayIcon.Text = $"Monitor Wellness — native display for {rule.ProcessName}";
    }

    private static System.Drawing.Icon LoadEmbeddedIcon(string resourceName)
    {
        using var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        return new System.Drawing.Icon(stream);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        var exitTimer = System.Diagnostics.Stopwatch.StartNew();
        DebugLog.Write("App exiting: shutting down and handing the displays back.");
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _displayReapplyTimer?.Stop();
        _memoryTrimTimer?.Stop();
        AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
        _timer?.Stop();
        _applicationRuleTimer?.Stop();
        _breakReminder?.Dispose();
        _breakFocusWindow?.Close();
        _pauseTimer?.Stop();
        _startupBalloonTimer?.Stop();
        _updateCheckTimer?.Stop();
        _hotkey?.Dispose();
        _emergencyRestoreHotkey?.Dispose();
        _hardwareBrightness.Dispose();
        _gammaManager?.Dispose();
        _overlay?.Dispose();
        _trayIcon?.Dispose();
        _iconOn?.Dispose();
        _iconOff?.Dispose();
        _singleInstanceGuard?.Dispose();
        DebugLog.Write($"App exit complete in {exitTimer.ElapsedMilliseconds} ms; displays handed back.");
        base.OnExit(e);
    }
}
