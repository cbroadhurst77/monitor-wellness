using System.Windows.Forms;
using System.Windows.Threading;
using MonitorWellness.Core;

namespace MonitorWellness;

/// <summary>
/// The opt-in 20-20-20 break reminder. It samples keyboard/mouse activity every few seconds and
/// counts the time the person has actually been at the computer (<see cref="ActiveTimeTracker"/>);
/// when that reaches the chosen interval it shows a reminder, unless migraine mode, a fullscreen
/// app or a snooze says not to. A reminder that is held back stays due and is shown as soon as the
/// reason goes away, instead of being skipped until the next cycle. Taking a break (the 20-second
/// focus timer, or simply stepping away) restarts the count.
/// The rules live in BreakReminderState/BreakReminderPolicy/ActiveTimeTracker (tested); this class
/// is the wiring to the Windows signals, the tray balloon and the tray menu.
/// </summary>
public sealed class BreakReminderService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly Func<bool> _isMigraineActive;
    private readonly Action<int, string, ToolTipIcon, Action?> _showBalloon;
    private readonly Action? _startBreak;
    private readonly Func<Action<BreakPromptChoice>, bool>? _showPrompt;
    private readonly Func<TimeSpan> _readIdle;
    private readonly Func<bool> _isFullscreen;
    private readonly TimeProvider _clock;
    private readonly BreakReminderState _state = new();
    private readonly ActiveTimeTracker _activeTime = new();
    private DispatcherTimer? _timer;
    private ToolStripMenuItem[] _snoozeItems = Array.Empty<ToolStripMenuItem>();
    private readonly LogOnChange _tickLog = new(TimeSpan.FromHours(6));

    /// <param name="showBalloon">Shows a tray balloon: (milliseconds, message, icon, action to run if the balloon is clicked).</param>
    /// <param name="startBreak">Starts the 20-second focus break; offered by the prompt and when the fallback balloon is clicked.</param>
    /// <param name="showPrompt">Shows the reminder prompt and reports the person's choice to the callback; returns false if it could not be shown (the balloon is used instead).</param>
    public BreakReminderService(
        AppSettings settings,
        Func<bool> isMigraineActive,
        Action<int, string, ToolTipIcon, Action?> showBalloon,
        Action? startBreak = null,
        Func<Action<BreakPromptChoice>, bool>? showPrompt = null,
        Func<TimeSpan>? readIdle = null,
        Func<bool>? isFullscreen = null,
        TimeProvider? clock = null)
    {
        _settings = settings;
        _isMigraineActive = isMigraineActive;
        _showBalloon = showBalloon;
        _startBreak = startBreak;
        _showPrompt = showPrompt;
        _readIdle = readIdle ?? UserIdleDetector.GetIdleDuration;
        _isFullscreen = isFullscreen ?? FullscreenDetector.IsForegroundWindowLikelyFullscreen;
        _clock = clock ?? TimeProvider.System;
    }

    private TimeSpan Interval => TimeSpan.FromMinutes(BreakReminderPolicy.ClampIntervalMinutes(_settings.BreakReminderIntervalMinutes));

    /// <summary>
    /// Starts or stops sampling to match the settings. Called at startup and after every Settings
    /// save. Saving unrelated settings, or changing only the interval, does not restart the count:
    /// the interval is read afresh at each sample.
    /// </summary>
    public void Rebuild()
    {
        if (!_settings.BreakReminderEnabled)
        {
            if (_timer is not null)
                DebugLog.Write("BreakReminder: disabled, sampling stopped");
            _timer?.Stop();
            _timer = null;
            _state.Reset();
            _activeTime.Reset();
            return;
        }

        if (_timer is not null)
            return;

        DebugLog.Write($"BreakReminder: sampling started, interval={Interval.TotalMinutes} min of active time");
        _activeTime.Reset();
        _timer = new DispatcherTimer { Interval = BreakReminderPolicy.SampleInterval };
        _timer.Tick += (_, _) => SampleOnce();
        _timer.Start();
    }

    /// <summary>The person took a break (the focus timer ran, or they chose to): start counting again.</summary>
    public void NotifyBreakTaken()
    {
        _activeTime.Reset();
        DebugLog.Write("BreakReminder: a break was taken; the count restarts.");
    }

    /// <summary>One look at the keyboard/mouse and, if the interval is reached, the decision whether to show the reminder.</summary>
    internal void SampleOnce()
    {
        // A convenience feature must not take the app's display-recovery path with it: anything
        // thrown here is logged and the next sample simply tries again.
        try
        {
            if (!_settings.BreakReminderEnabled)
                return;

            DateTime nowUtc = _clock.GetUtcNow().UtcDateTime;
            TimeSpan idle = _readIdle();
            _activeTime.Sample(nowUtc, idle, BreakReminderPolicy.SampleInterval, BreakReminderPolicy.AwayThreshold);
            if (!_activeTime.IsDue(Interval))
                return;

            BreakReminderTick tick = _state.Tick(nowUtc, () =>
            {
                bool migraineActive = _isMigraineActive();
                bool likelyFullscreen = !migraineActive && _isFullscreen();
                return new BreakReminderSignals(migraineActive, likelyFullscreen, idle);
            });

            if (tick.Outcome == BreakReminderTickOutcome.SkippedSnoozed)
                return; // still due; shown once the snooze ends
            if (tick.SnoozeExpired)
                DebugLog.Write("BreakReminder: snooze expired; reminders resumed.");

            if (tick.Outcome == BreakReminderTickOutcome.Show)
            {
                _activeTime.Reset();
                if (_tickLog.ShouldLog("show", nowUtc))
                    DebugLog.Write("BreakReminder: reminder shown (further identical reminders are logged only every 6 hours)");
                if (!TryShowPrompt())
                {
                    _showBalloon(
                        8_000,
                        "Time for a break — look at something about 20 feet away for 20 seconds. Click here for a 20-second timer.",
                        ToolTipIcon.Info,
                        _startBreak);
                }
            }
            else if (_tickLog.ShouldLog($"hold:{tick.Decision}", nowUtc))
            {
                DebugLog.Write($"BreakReminder: reminder is due but held back ({tick.Decision}); it will show when that clears (identical entries logged every 6 hours)");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"BreakReminder: sample failed: {ex.Message}");
        }
    }

    /// <summary>How long "In 5 minutes" on the prompt waits before asking again.</summary>
    internal static readonly TimeSpan PromptSnoozeDelay = TimeSpan.FromMinutes(5);

    private bool TryShowPrompt()
    {
        if (_showPrompt is null)
            return false;

        try
        {
            return _showPrompt(OnPromptChoice);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"BreakReminder: the prompt could not be shown, using a balloon: {ex.Message}");
            return false;
        }
    }

    private void OnPromptChoice(BreakPromptChoice choice)
    {
        try
        {
            switch (choice)
            {
                case BreakPromptChoice.StartBreak:
                    _startBreak?.Invoke();
                    break;
                case BreakPromptChoice.Snooze:
                    // The count restarted when the prompt appeared; make the next one come in a few minutes, not a full interval.
                    _activeTime.SetAccumulated(Interval - PromptSnoozeDelay);
                    DebugLog.Write("BreakReminder: prompt snoozed for 5 minutes.");
                    break;
                case BreakPromptChoice.Skip:
                    DebugLog.Write("BreakReminder: prompt skipped.");
                    break;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"BreakReminder: handling the prompt choice failed: {ex.Message}");
        }
    }

    public ToolStripMenuItem BuildSnoozeMenu()
    {
        var menu = new ToolStripMenuItem("Snooze &Break Reminders");
        var items = new List<ToolStripMenuItem>
        {
            (ToolStripMenuItem)menu.DropDownItems.Add("30 minutes", null, (_, _) => Snooze(TimeSpan.FromMinutes(30))),
            (ToolStripMenuItem)menu.DropDownItems.Add("1 hour", null, (_, _) => Snooze(TimeSpan.FromHours(1))),
            (ToolStripMenuItem)menu.DropDownItems.Add("Until morning (8:00)", null, (_, _) =>
                Snooze(BreakReminderPolicy.NextMorningLocal(DateTime.Now).ToUniversalTime() - DateTime.UtcNow)),
        };
        menu.DropDownItems.Add(new ToolStripSeparator());
        items.Add((ToolStripMenuItem)menu.DropDownItems.Add("Resume break reminders", null, (_, _) => Resume()));
        _snoozeItems = items.ToArray();

        menu.DropDownOpening += (_, _) =>
        {
            bool enabled = _settings.BreakReminderEnabled;
            foreach (ToolStripMenuItem item in _snoozeItems)
                item.Enabled = enabled;
            menu.Text = !enabled
                ? "Snooze &Break Reminders (off in Settings)"
                : _state.IsSnoozed(_clock.GetUtcNow().UtcDateTime)
                    ? $"Snooze &Break Reminders (until {_state.SnoozedUntilUtc!.Value.ToLocalTime():HH:mm})"
                    : "Snooze &Break Reminders";
        };
        return menu;
    }

    private void Snooze(TimeSpan duration)
    {
        DateTime nowUtc = _clock.GetUtcNow().UtcDateTime;
        if (!_state.TrySnooze(_settings.BreakReminderEnabled, nowUtc, duration))
            return;

        DateTime until = _state.SnoozedUntilUtc!.Value;
        DebugLog.Write($"BreakReminder: snoozed until {until:yyyy-MM-dd HH:mm} UTC.");
        _showBalloon(4_000, $"Break reminders snoozed until {until.ToLocalTime():HH:mm}.", ToolTipIcon.Info, null);
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
