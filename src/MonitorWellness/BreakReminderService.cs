using System.Windows.Forms;
using System.Windows.Threading;
using MonitorWellness.Core;

namespace MonitorWellness;

/// <summary>
/// The opt-in 20-20-20 break reminder. It samples keyboard/mouse activity every few seconds and
/// counts the time the person has actually been at the computer (<see cref="ActiveTimeTracker"/>);
/// when that reaches the chosen interval it shows a reminder, unless migraine mode, a fullscreen
/// app, a call or a snooze says not to. A reminder that is held back stays due and is shown as soon as
/// the reason goes away, instead of being skipped until the next cycle. Outside the person's chosen
/// hours nothing is counted. Taking a break (the focus timer, or simply stepping away) restarts the count.
/// The rules live in BreakReminderState/BreakReminderPolicy/ActiveTimeTracker (tested); this class
/// is the wiring to the Windows signals, the prompt, the tray balloon and the tray menu.
/// </summary>
public sealed class BreakReminderService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly Func<bool> _isMigraineActive;
    private readonly Action<int, string, ToolTipIcon, Action?> _showBalloon;
    private readonly Action? _startBreak;
    private readonly Func<Action<BreakPromptChoice>, bool>? _showPrompt;
    private readonly Func<Action<BreakPromptChoice>, bool>? _showStandUpPrompt;
    private readonly Action<HistoryEvent> _recordHistory;
    private readonly Action? _openSettings;
    private int _eyeSkips;
    private int _standUpSkips;
    private DateOnly? _suggestedOn;

    /// <summary>After this many reminders in a row that were skipped or ignored, the app offers to make them less frequent.</summary>
    internal const int SkipsBeforeSuggestion = 3;
    private TimeSpan _unrecordedActive;
    private DateTime _lastCountedUtc;

    /// <summary>Time at the computer is written to the history in chunks this big (and on exit and at midnight), not every few seconds.</summary>
    private static readonly TimeSpan ActiveTimeFlushEvery = TimeSpan.FromMinutes(10);
    private readonly Func<TimeSpan> _readIdle;
    private readonly Func<bool> _isFullscreen;
    private readonly Func<bool> _isInCall;
    private readonly Action _playSound;
    private readonly TimeProvider _clock;
    private readonly BreakReminderState _state = new();
    private readonly ActiveTimeTracker _activeTime = new();
    private readonly ActiveTimeTracker _standUpTime = new();
    private DispatcherTimer? _timer;
    private ToolStripMenuItem[] _snoozeItems = Array.Empty<ToolStripMenuItem>();
    private DateTime? _callHeldSinceUtc;
    private BreakReminderDecision? _heldFor;
    private readonly LogOnChange _tickLog = new(TimeSpan.FromHours(6));

    /// <param name="showBalloon">Shows a tray balloon: (milliseconds, message, icon, action to run if the balloon is clicked).</param>
    /// <param name="startBreak">Starts the focus break; offered by the prompt and when the fallback balloon is clicked.</param>
    /// <param name="showPrompt">Shows the reminder prompt and reports the person's choice to the callback; returns false if it could not be shown (the balloon is used instead).</param>
    /// <param name="isInCall">Whether a microphone or camera is in use; only asked when reminders are set to wait during calls.</param>
    /// <param name="playSound">Plays the reminder sound; only called when the sound setting is on.</param>
    /// <param name="showStandUpPrompt">Shows the stand-up prompt; without it (or if it cannot be shown) a balloon is used.</param>
    /// <param name="openSettings">Opens Settings; offered when reminders keep being skipped (so the person can make them less frequent).</param>
    /// <param name="recordHistory">Writes one history event (only used when the break history setting is on); the local history file by default.</param>
    public BreakReminderService(
        AppSettings settings,
        Func<bool> isMigraineActive,
        Action<int, string, ToolTipIcon, Action?> showBalloon,
        Action? startBreak = null,
        Func<Action<BreakPromptChoice>, bool>? showPrompt = null,
        Func<TimeSpan>? readIdle = null,
        Func<bool>? isFullscreen = null,
        TimeProvider? clock = null,
        Func<bool>? isInCall = null,
        Action? playSound = null,
        Func<Action<BreakPromptChoice>, bool>? showStandUpPrompt = null,
        Action<HistoryEvent>? recordHistory = null,
        Action? openSettings = null)
    {
        _settings = settings;
        _isMigraineActive = isMigraineActive;
        _showBalloon = showBalloon;
        _startBreak = startBreak;
        _showPrompt = showPrompt;
        _showStandUpPrompt = showStandUpPrompt;
        _recordHistory = recordHistory ?? HistoryStore.Append;
        _openSettings = openSettings;
        _readIdle = readIdle ?? UserIdleDetector.GetIdleDuration;
        _isFullscreen = isFullscreen ?? FullscreenDetector.IsForegroundWindowLikelyFullscreen;
        _isInCall = isInCall ?? CallActivityDetector.IsInCall;
        _playSound = playSound ?? (() => System.Media.SystemSounds.Asterisk.Play());
        _clock = clock ?? TimeProvider.System;
    }

    private TimeSpan Interval => TimeSpan.FromMinutes(BreakReminderPolicy.ClampIntervalMinutes(_settings.BreakReminderIntervalMinutes));

    private TimeSpan StandUpInterval => TimeSpan.FromMinutes(Math.Clamp(
        _settings.StandUpIntervalMinutes, BreakReminderPolicy.MinimumStandUpMinutes, BreakReminderPolicy.MaximumStandUpMinutes));

    private int FocusSeconds => Math.Clamp(_settings.BreakFocusSeconds, BreakReminderStyles.MinimumFocusSeconds, BreakReminderStyles.MaximumFocusSeconds);

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
            FlushActiveTime();
            _state.Reset();
            _activeTime.Reset();
            _standUpTime.Reset();
            _callHeldSinceUtc = null;
            _heldFor = null;
            return;
        }

        if (_timer is not null)
            return;

        DebugLog.Write($"BreakReminder: sampling started, interval={Interval.TotalMinutes} min of active time");
        _activeTime.Reset();
        _standUpTime.Reset();
        _timer = new DispatcherTimer { Interval = BreakReminderPolicy.SampleInterval };
        _timer.Tick += (_, _) => SampleOnce();
        _timer.Start();
    }

    /// <summary>
    /// One line saying when the next break is due, or why a due one has not appeared (snoozed, outside
    /// the chosen hours, a call, a full-screen app, migraine mode, away from the keyboard). For the tray menu.
    /// </summary>
    public string DescribeStatus()
    {
        try
        {
            DateTime nowUtc = _clock.GetUtcNow().UtcDateTime;
            DateTime nowLocal = _clock.GetLocalNow().DateTime;
            DateTime? snoozedUntilLocal = _state.IsSnoozed(nowUtc) ? _state.SnoozedUntilUtc!.Value.ToLocalTime() : null;
            bool withinHours = BreakReminderPolicy.IsWithinActiveHours(
                nowLocal, _settings.BreakReminderLimitToHours, _settings.BreakReminderFromLocal, _settings.BreakReminderToLocal);
            string status = BreakReminderStatusText.Describe(
                _settings.BreakReminderEnabled, snoozedUntilLocal, withinHours, _settings.BreakReminderFromLocal,
                _activeTime.Accumulated, Interval, _heldFor);
            return _settings.BreakReminderEnabled && snoozedUntilLocal is null && withinHours
                ? status + BreakReminderStatusText.StandUpSuffix(_settings.StandUpReminderEnabled, _standUpTime.Accumulated, StandUpInterval)
                : status;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"BreakReminder: couldn't describe the status: {ex.Message}");
            return "Break reminders";
        }
    }

    /// <summary>Shows the reminder now, the way a real one would arrive (prompt or notification, with the sound if on), without touching the count. To preview the settings.</summary>
    public void ShowTestReminder()
    {
        try
        {
            Deliver(record: false); // a test is not a real reminder and must not appear in the history
        }
        catch (Exception ex)
        {
            DebugLog.Write($"BreakReminder: the test reminder failed: {ex.Message}");
        }
    }

    /// <summary>The person took a break (the focus timer ran, or they chose to): start counting again.</summary>
    /// <param name="movement">True when the person got up and moved (the stand-up count restarts too); the 20-second eye break does not count as that.</param>
    public void NotifyBreakTaken(bool movement = false)
    {
        _activeTime.Reset();
        _eyeSkips = 0;
        if (movement)
        {
            _standUpTime.Reset();
            _standUpSkips = 0;
        }
        Record(movement ? BreakHistory.MovementBreakTaken : BreakHistory.BreakTaken);
        DebugLog.Write(movement
            ? "BreakReminder: a movement break was taken; both counts restart."
            : "BreakReminder: a break was taken; the count restarts.");
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
            if (!BreakReminderPolicy.IsWithinActiveHours(
                    _clock.GetLocalNow().DateTime, _settings.BreakReminderLimitToHours,
                    _settings.BreakReminderFromLocal, _settings.BreakReminderToLocal))
            {
                // Outside the chosen hours nothing counts, so the first reminder of the day is a
                // full interval after the hours begin, not one the moment they do.
                _activeTime.Reset();
                _standUpTime.Reset();
                _callHeldSinceUtc = null;
                _heldFor = null;
                return;
            }

            TimeSpan idle = _readIdle();
            _activeTime.Sample(nowUtc, idle, BreakReminderPolicy.SampleInterval, BreakReminderPolicy.AwayThreshold);
            CountActiveTime(nowUtc);
            if (_settings.StandUpReminderEnabled)
                _standUpTime.Sample(nowUtc, idle, BreakReminderPolicy.SampleInterval, BreakReminderPolicy.StandUpAwayThreshold);
            else
                _standUpTime.Reset();

            bool standUpDue = _settings.StandUpReminderEnabled && _standUpTime.IsDue(StandUpInterval);
            if (!standUpDue && !_activeTime.IsDue(Interval))
            {
                _heldFor = null;
                return;
            }

            BreakReminderTick tick = _state.Tick(nowUtc, () =>
            {
                bool migraineActive = _isMigraineActive();
                bool likelyFullscreen = !migraineActive && _isFullscreen();
                bool inCall = !migraineActive && !likelyFullscreen && IsHeldForCall(nowUtc);
                return new BreakReminderSignals(migraineActive, likelyFullscreen, idle, inCall);
            });

            if (tick.Outcome == BreakReminderTickOutcome.SkippedSnoozed)
                return; // still due; shown once the snooze ends
            if (tick.SnoozeExpired)
                DebugLog.Write("BreakReminder: snooze expired; reminders resumed.");
            _heldFor = tick.Outcome == BreakReminderTickOutcome.Suppressed ? tick.Decision : null;

            if (tick.Outcome == BreakReminderTickOutcome.Show)
            {
                _activeTime.Reset(); // standing up rests the eyes too, so the eye count restarts either way
                _callHeldSinceUtc = null;
                if (_tickLog.ShouldLog("show", nowUtc))
                    DebugLog.Write("BreakReminder: reminder shown (further identical reminders are logged only every 6 hours)");
                if (standUpDue)
                {
                    _standUpTime.Reset();
                    DebugLog.Write("BreakReminder: stand-up reminder shown.");
                    DeliverStandUp();
                }
                else
                {
                    Deliver();
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

    /// <summary>
    /// True while a microphone or camera is in use and reminders are set to wait for calls. A device that
    /// has held the reminder this long is not a call (an app that keeps the microphone open all day, say),
    /// so the reminder is shown after all.
    /// </summary>
    private bool IsHeldForCall(DateTime nowUtc)
    {
        if (!_settings.BreakReminderHoldDuringCalls || !_isInCall())
        {
            _callHeldSinceUtc = null;
            return false;
        }

        _callHeldSinceUtc ??= nowUtc;
        return nowUtc - _callHeldSinceUtc.Value < BreakReminderPolicy.MaximumCallHold;
    }

    private void Deliver(bool record = true)
    {
        if (record)
            Record(BreakHistory.EyeReminderShown);
        if (_settings.BreakReminderSound)
        {
            try { _playSound(); }
            catch (Exception ex) { DebugLog.Write($"BreakReminder: couldn't play the reminder sound: {ex.Message}"); }
        }

        bool windowStyle = !string.Equals(_settings.BreakReminderStyle, BreakReminderStyles.Notification, StringComparison.Ordinal);
        if (windowStyle && TryShowPrompt())
            return;

        _showBalloon(
            8_000,
            $"Time for a break — look at something about 20 feet away for {FocusSeconds} seconds. Click here for a {FocusSeconds}-second timer.",
            ToolTipIcon.Info,
            _startBreak);
    }

    private void DeliverStandUp()
    {
        Record(BreakHistory.StandUpReminderShown);
        if (_settings.BreakReminderSound)
        {
            try { _playSound(); }
            catch (Exception ex) { DebugLog.Write($"BreakReminder: couldn't play the reminder sound: {ex.Message}"); }
        }

        bool windowStyle = !string.Equals(_settings.BreakReminderStyle, BreakReminderStyles.Notification, StringComparison.Ordinal);
        if (windowStyle && _showStandUpPrompt is not null)
        {
            try
            {
                if (_showStandUpPrompt(OnStandUpChoice))
                    return;
            }
            catch (Exception ex)
            {
                DebugLog.Write($"BreakReminder: the stand-up prompt could not be shown, using a balloon: {ex.Message}");
            }
        }

        _showBalloon(
            8_000,
            "Time to stand up — you have been at the computer a while. Stretch or walk for a few minutes.",
            ToolTipIcon.Info,
            null);
    }

    private void OnStandUpChoice(BreakPromptChoice choice)
    {
        try
        {
            switch (choice)
            {
                case BreakPromptChoice.StartBreak:
                case BreakPromptChoice.AlreadyTookBreak:
                    NotifyBreakTaken(movement: true);
                    break;
                case BreakPromptChoice.Snooze:
                    _standUpSkips = 0;
                    _standUpTime.SetAccumulated(StandUpInterval - PromptSnoozeDelay);
                    Record(BreakHistory.ReminderSnoozed);
                    DebugLog.Write("BreakReminder: stand-up prompt snoozed for 5 minutes.");
                    break;
                case BreakPromptChoice.Skip:
                    Record(BreakHistory.ReminderSkipped);
                    DebugLog.Write("BreakReminder: stand-up prompt skipped.");
                    RegisterSkip(standUp: true);
                    break;
                case BreakPromptChoice.TimedOut:
                    Record(BreakHistory.ReminderTimedOut);
                    RegisterSkip(standUp: true);
                    break;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"BreakReminder: handling the stand-up choice failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Counts a skipped or ignored reminder; after a few in a row (at most once a day) offers to make the
    /// reminders less frequent instead of carrying on being skipped. In use, two of the first three real
    /// prompts were skipped within seconds.
    /// </summary>
    private void RegisterSkip(bool standUp)
    {
        int streak = standUp ? ++_standUpSkips : ++_eyeSkips;
        if (streak < SkipsBeforeSuggestion)
            return;

        if (standUp) _standUpSkips = 0; else _eyeSkips = 0;
        DateOnly today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        if (_suggestedOn == today)
            return;

        _suggestedOn = today;
        string what = standUp ? "stand-up reminders" : "break reminders";
        DebugLog.Write($"BreakReminder: the last {SkipsBeforeSuggestion} {what} were skipped; suggesting fewer.");
        _showBalloon(
            10_000,
            $"You've skipped the last {SkipsBeforeSuggestion} {what}. They may be coming too often — click here to change how often.",
            ToolTipIcon.Info,
            _openSettings);
    }

    /// <summary>Writes one history event when the person has turned the break history on. A history problem must never get in the way of a reminder.</summary>
    private void Record(string eventType)
    {
        if (!_settings.BreakHistoryEnabled)
            return;

        try
        {
            _recordHistory(new HistoryEvent(_clock.GetUtcNow().UtcDateTime, eventType, null, null));
        }
        catch (Exception ex)
        {
            DebugLog.Write($"BreakReminder: couldn't record {eventType}: {ex.Message}");
        }
    }

    /// <summary>Adds what the latest sample counted to the pending time at the computer, writing it out in chunks and at midnight so each day gets its own.</summary>
    private void CountActiveTime(DateTime nowUtc)
    {
        if (!_settings.BreakHistoryEnabled)
        {
            _unrecordedActive = TimeSpan.Zero;
            return;
        }

        if (_activeTime.LastCounted <= TimeSpan.Zero)
            return;

        if (_unrecordedActive > TimeSpan.Zero && LocalDay(_lastCountedUtc) != LocalDay(nowUtc))
            FlushActiveTime(); // belongs to the day before

        _unrecordedActive += _activeTime.LastCounted;
        _lastCountedUtc = nowUtc;
        if (_unrecordedActive >= ActiveTimeFlushEvery)
            FlushActiveTime();
    }

    private DateOnly LocalDay(DateTime utc)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _clock.LocalTimeZone));

    /// <summary>Writes the pending time at the computer to the history (stamped with the time of its last sample).</summary>
    public void FlushActiveTime()
    {
        if (_unrecordedActive < TimeSpan.FromSeconds(1))
        {
            _unrecordedActive = TimeSpan.Zero;
            return;
        }

        int seconds = (int)Math.Round(_unrecordedActive.TotalSeconds);
        _unrecordedActive = TimeSpan.Zero;
        if (!_settings.BreakHistoryEnabled)
            return;

        try
        {
            _recordHistory(new HistoryEvent(_lastCountedUtc, BreakHistory.ActiveTime, null, seconds));
        }
        catch (Exception ex)
        {
            DebugLog.Write($"BreakReminder: couldn't record time at the computer: {ex.Message}");
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
                    _eyeSkips = 0;
                    _startBreak?.Invoke();
                    break;
                case BreakPromptChoice.AlreadyTookBreak:
                    NotifyBreakTaken(); // counted as a break taken, and the count restarts
                    break;
                case BreakPromptChoice.Snooze:
                    // The count restarted when the prompt appeared; make the next one come in a few minutes, not a full interval.
                    _eyeSkips = 0;
                    _activeTime.SetAccumulated(Interval - PromptSnoozeDelay);
                    Record(BreakHistory.ReminderSnoozed);
                    DebugLog.Write("BreakReminder: prompt snoozed for 5 minutes.");
                    break;
                case BreakPromptChoice.Skip:
                    Record(BreakHistory.ReminderSkipped);
                    DebugLog.Write("BreakReminder: prompt skipped.");
                    RegisterSkip(standUp: false);
                    break;
                case BreakPromptChoice.TimedOut:
                    Record(BreakHistory.ReminderTimedOut);
                    RegisterSkip(standUp: false);
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
        FlushActiveTime();
        _timer?.Stop();
        _timer = null;
    }
}
