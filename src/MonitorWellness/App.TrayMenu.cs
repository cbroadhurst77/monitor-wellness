using System.IO;
using System.Windows.Forms;
using MonitorWellness.Core;

namespace MonitorWellness;

/// <summary>The tray icon's right-click menu. Split out of App.xaml.cs so startup reads as a sequence of steps rather than a 100-line menu declaration; every item still just calls back into App.</summary>
public partial class App
{
    private ContextMenuStrip BuildTrayMenu()
    {
        MigraineModeController migraine = _migraine ?? throw new InvalidOperationException("The tray menu is built after the migraine controller exists.");
        BreakReminderService breakReminder = _breakReminder ?? throw new InvalidOperationException("The tray menu is built after the break reminder service exists.");

        var menu = new ContextMenuStrip();
        menu.Items.Add("&Toggle Migraine Mode", null, (_, _) => migraine.Toggle());
        menu.Items.Add("Emergency &Restore Screen (Ctrl+Alt+Shift+R)", null, (_, _) => EmergencyRestoreDisplay());
        menu.Items.Add("Activate Migraine Mode (&Full)", null, (_, _) => migraine.Activate(mild: false));
        menu.Items.Add("Activate Migraine Mode (Mi&ld)", null, (_, _) => migraine.Activate(mild: true));
        menu.Items.Add("&Deactivate Migraine Mode", null, (_, _) => migraine.Deactivate());
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
        var breakStatusItem = new ToolStripMenuItem(breakReminder.DescribeStatus()) { Enabled = false };
        menu.Items.Add(breakStatusItem);
        menu.Opening += (_, _) => breakStatusItem.Text = breakReminder.DescribeStatus();
        _focusBreakMenuItem = new ToolStripMenuItem(FocusBreakMenuText(), null, (_, _) => ShowBreakFocusWindow());
        menu.Items.Add(_focusBreakMenuItem);
        menu.Items.Add("Show a test reminder", null, (_, _) => breakReminder.ShowTestReminder());
        menu.Items.Add(breakReminder.BuildSnoozeMenu());
        _resumeScheduleMenuItem = new ToolStripMenuItem("&Resume Schedule", null, (_, _) => ResumeSchedule()) { Enabled = false };
        menu.Items.Add(_resumeScheduleMenuItem);
        menu.Items.Add(BuildQuickComfortPlansMenu());
        menu.Items.Add(new ToolStripSeparator());
        // Whether auto-start is registered needs a Task Scheduler query, which can take seconds right after a
        // boot, so it is asked in the background (below); until the answer arrives the saved preference is shown.
        var autoStartItem = new ToolStripMenuItem("Start with &Windows") { Checked = _settings.AutoStartEnabled };
        autoStartItem.Click += (_, _) =>
        {
            bool wasOn = autoStartItem.Checked;
            bool success = wasOn ? AutoStartManager.Unregister() : AutoStartManager.Register();
            if (success)
            {
                autoStartItem.Checked = !wasOn;
                _settings.AutoStartEnabled = !wasOn;
                if (!wasOn && AutoStartManager.LastRegistrationNote is string registrationNote)
                    ShowTrayBalloon(10_000, "Monitor Wellness", registrationNote, ToolTipIcon.Info);
                try
                {
                    SettingsStore.Save(_settings);
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
                {
                    DebugLog.Write($"Couldn't persist AutoStartEnabled after toggling: {ex.Message}");
                }
            }
            else if (_trayIcon is not null)
            {
                ShowTrayBalloon(
                    10_000,
                    "Monitor Wellness",
                    wasOn
                        ? "Couldn't remove the auto-start entry — the UAC prompt may have been cancelled."
                        : "Couldn't set up auto-start — this needs administrator approval (UAC prompt), which may have been cancelled or blocked by IT policy.",
                    ToolTipIcon.Warning);
            }
        };
        menu.Items.Add(autoStartItem);
        _ = ResolveAutoStartStateAsync(autoStartItem);
        // Diagnostic-only items nested under one submenu, rather than sitting at the same menu
        // level as everyday actions (Settings, Migraine Mode) — shortens the list a first-time
        // user has to scan. See MonitorWellness_UX_Accessibility_Audit.html §2.1/§6 (P2).
        var diagnosticsMenu = new ToolStripMenuItem("&Diagnostics");
        diagnosticsMenu.DropDownItems.Add("Display Capability Passport...", null, (_, _) => ShowDisplayCapabilityWindow());
        diagnosticsMenu.DropDownItems.Add("Auto-start Diagnostics...", null, (_, _) => ShowAutoStartDiagnostics());
        diagnosticsMenu.DropDownItems.Add("Export Diagnostic Bundle...", null, async (_, _) => await ExportDiagnosticBundleAsync());
        diagnosticsMenu.DropDownItems.Add("Open Logs Folder", null, (_, _) => OpenLogsFolder());
        menu.Items.Add(diagnosticsMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("&Settings...", null, (_, _) => OpenSettingsWindow());
        menu.Items.Add("Add &Foreground App Rule...", null, (_, _) => CaptureForegroundApplicationRule());
        // Split out of the old single "Help / About..." item, which previously just reopened
        // the first-run onboarding wizard (the wrong voice for someone looking something up
        // later, rather than seeing it for the first time). See
        // MonitorWellness_UX_Accessibility_Audit.html §2.1/§2.6/§3.5/§6 (P1).
        menu.Items.Add("&About Monitor Wellness...", null, (_, _) => ShowAboutWindow());
        menu.Items.Add("&Troubleshooting...", null, (_, _) => ShowTroubleshootingWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("&Restart Monitor Wellness", null, (_, _) => RestartApplication());
        menu.Items.Add("E&xit", null, (_, _) => Shutdown());
        return menu;
    }

    /// <summary>
    /// Finds out in the background whether auto-start is registered and updates the menu item (and, if
    /// it was turned on but has gone missing, tells the person). "Unknown" is never read as "no".
    /// </summary>
    private async Task ResolveAutoStartStateAsync(ToolStripMenuItem item)
    {
        try
        {
            AutoStartCheck check = await Task.Run(AutoStartManager.CheckRegistration);
            bool registered = check == AutoStartCheck.Registered
                || (check == AutoStartCheck.Unknown && _settings.AutoStartEnabled);
            if (check == AutoStartCheck.Unknown)
                DebugLog.Write("Auto-start check could not be completed at startup; leaving the saved preference as it is.");

            item.Checked = registered;
            if (_settings.AutoStartEnabled && check is AutoStartCheck.NotRegistered or AutoStartCheck.Disabled)
            {
                // The user turned this on at some point and it's since disappeared or been disabled —
                // e.g. a Windows update or IT policy silently removed the Task Scheduler entry. Worth
                // saying so explicitly rather than letting auto-start just quietly stop working.
                DebugLog.Write($"Auto-start drift detected: AppSettings.AutoStartEnabled is true but the Task Scheduler entry is {(check == AutoStartCheck.Disabled ? "disabled" : "missing")}");
                QueueLateStartupBalloon(
                    "Monitor Wellness",
                    "Auto-start seems to have been turned off (possibly by a Windows update or IT policy) — re-enable it from this tray menu if you still want it.",
                    ToolTipIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"Auto-start check failed: {ex.Message}");
        }
    }

    private string FocusBreakMenuText() => $"Start a &{_settings.BreakFocusSeconds}-second focus break";
}
