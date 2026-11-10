using System.Windows.Forms;
using System.Windows.Threading;
using MonitorWellness.Core;

namespace MonitorWellness;

/// <summary>
/// The opt-in 20-20-20 break reminder: owns the repeating timer, the session-only snooze state
/// and the tray "Snooze Break Reminders" submenu. The rules themselves stay in
/// BreakReminderState/BreakReminderPolicy (tested); this class is only the wiring between those
/// rules, the Windows signals that feed them, and the tray balloon. Extracted from App.
/// </summary>
public sealed class BreakReminderService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly Func<bool> _isMigraineActive;
    private readonly Action<int, string, ToolTipIcon> _showBalloon;
    private readonly BreakReminderState _state = new();
    private DispatcherTimer? _timer;

    /// <param name="showBalloon">Shows a tray balloon: (milliseconds, message, icon).</param>
    public BreakReminderService(AppSettings settings, Func<bool> isMigraineActive, Action<int, string, ToolTipIcon> showBalloon)
    {
        _settings = settings;
        _isMigraineActive = isMigraineActive;
        _showBalloon = showBalloon;
    }

    /// <summary>
    /// (Re)starts the timer from the current settings, or stops it if reminders are off. Called at
    /// startup and again after Settings saves, since the interval or on/off state may have changed.
    /// </summary>
    public void Rebuild()
    {
        _timer?.Stop();
        _timer = null;

        if (!_settings.BreakReminderEnabled)
        {
            _state.Reset();
            DebugLog.Write("BreakReminder: disabled, timer not (re)started");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _settings.BreakReminderIntervalMinutes));
        DebugLog.Write($"BreakReminder: timer (re)started, interval={interval.TotalMinutes} min");
        _timer = new DispatcherTimer { Interval = interval };
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
    }

    private void OnTick()
    {
        // Skip while migraine mode is active, a fullscreen app likely owns the screen, or
        // Windows says the person has stepped away. The timer continues on its regular
        // cadence so returning from a real break never triggers an immediate catch-up prompt.
        TimeSpan idleDuration = TimeSpan.Zero;
        BreakReminderTick tick = _state.Tick(DateTime.UtcNow, () =>
        {
            bool migraineActive = _isMigraineActive();
            bool likelyFullscreen = !migraineActive && FullscreenDetector.IsForegroundWindowLikelyFullscreen();
            idleDuration = migraineActive || likelyFullscreen ? TimeSpan.Zero : UserIdleDetector.GetIdleDuration();
            return new BreakReminderSignals(migraineActive, likelyFullscreen, idleDuration);
        });

        if (tick.Outcome == BreakReminderTickOutcome.SkippedSnoozed)
        {
            DebugLog.Write($"BreakReminder: tick skipped (snoozed until {tick.SnoozedUntilUtc:yyyy-MM-dd HH:mm} UTC)");
            return;
        }
        if (tick.SnoozeExpired)
            DebugLog.Write("BreakReminder: snooze expired; reminders resumed.");

        if (tick.Outcome == BreakReminderTickOutcome.Show)
        {
            DebugLog.Write("BreakReminder: tick fired, showing balloon");
            _showBalloon(
                8_000,
                "Time for a break — look at something about 20 feet away for 20 seconds. Use the tray menu if you'd like a quiet timer.",
                ToolTipIcon.Info);
        }
        else
        {
            DebugLog.Write($"BreakReminder: tick fired but skipped ({tick.Decision}, idle={idleDuration.TotalSeconds:F0}s)");
        }
    }

    public ToolStripMenuItem BuildSnoozeMenu()
    {
        var menu = new ToolStripMenuItem("Snooze &Break Reminders");
        menu.DropDownItems.Add("30 minutes", null, (_, _) => Snooze(TimeSpan.FromMinutes(30)));
        menu.DropDownItems.Add("1 hour", null, (_, _) => Snooze(TimeSpan.FromHours(1)));
        menu.DropDownItems.Add("Until tomorrow", null, (_, _) =>
            Snooze(DateTime.Today.AddDays(1).ToUniversalTime() - DateTime.UtcNow));
        menu.DropDownItems.Add(new ToolStripSeparator());
        menu.DropDownItems.Add("Resume break reminders", null, (_, _) => Resume());
        menu.DropDownOpening += (_, _) =>
        {
            bool enabled = _settings.BreakReminderEnabled;
            menu.Text = !enabled
                ? "Snooze &Break Reminders (off)"
                : _state.IsSnoozed(DateTime.UtcNow)
                    ? $"Snooze &Break Reminders (until {_state.SnoozedUntilUtc!.Value.ToLocalTime():HH:mm})"
                    : "Snooze &Break Reminders";
        };
        return menu;
    }

    private void Snooze(TimeSpan duration)
    {
        if (!_state.TrySnooze(_settings.BreakReminderEnabled, DateTime.UtcNow, duration))
            return;

        DateTime until = _state.SnoozedUntilUtc!.Value;
        DebugLog.Write($"BreakReminder: snoozed until {until:yyyy-MM-dd HH:mm} UTC.");
        _showBalloon(4_000, $"Break reminders snoozed until {until.ToLocalTime():HH:mm}.", ToolTipIcon.Info);
    }

    private void Resume()
    {
        if (!_state.Resume())
            return;

        DebugLog.Write("BreakReminder: snooze cleared; reminders resumed.");
    }

    public void Dispose()
    {
        _timer?.Stop();
        _timer = null;
    }
}
