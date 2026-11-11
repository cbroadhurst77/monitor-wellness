using System.Windows.Forms;

namespace MonitorWellness.Core;

/// <summary>What clicking the balloon on screen should do: run an action, open a link, or nothing.</summary>
public readonly record struct BalloonClick(Action? Action, string? Url);

/// <summary>
/// NotifyIcon says "a balloon was clicked" without saying which, and only one is ever showing, so this
/// remembers what the balloon on screen wants done and forgets it the moment that balloon closes, or is
/// replaced by another. A stale link or action once survived into a later, unrelated balloon (an update
/// notice's release page could be opened by clicking a migraine-mode message); the rule that every balloon
/// sets or clears this lives here so it can be tested.
/// </summary>
public sealed class BalloonClickState
{
    private string? _url;
    private Action? _action;

    /// <summary>Records what the balloon being shown should do when clicked (null for nothing). Replaces whatever the previous one wanted.</summary>
    public void Set(string? url, Action? action)
    {
        _url = url;
        _action = action;
    }

    /// <summary>Forgets it (the balloon closed).</summary>
    public void Clear() => Set(null, null);

    /// <summary>What a click should do now; the click is used up, so a second click does nothing.</summary>
    public BalloonClick TakeClick()
    {
        var click = new BalloonClick(_action, _action is null ? _url : null);
        Clear();
        return click;
    }
}

/// <summary>One notice waiting to be shown at start-up.</summary>
public sealed record StartupBalloon(string Title, string Message, ToolTipIcon Icon);

/// <summary>
/// The notices raised while the app is starting (an HDR display, f.lux, a hotkey conflict, auto-start gone
/// missing, a settings file that needed repair) must not all pop up at once on a first launch, on top of
/// the welcome window. They are queued, let go once the welcome is out of the way, and shown one at a time
/// with a gap between them. This is that queue; the caller owns the timer for the gap. Pure, so the
/// ordering rules can be tested.
/// </summary>
public sealed class StartupBalloons
{
    /// <summary>The gap between two start-up notices.</summary>
    public static readonly TimeSpan Spacing = TimeSpan.FromSeconds(6);

    private readonly Queue<StartupBalloon> _pending = new();

    /// <summary>True once the queue has been let go (the welcome window, if any, is closed).</summary>
    public bool Released { get; private set; }

    /// <summary>True while the gap before the next notice is running (the caller's timer is on).</summary>
    public bool Waiting { get; private set; }

    public int Count => _pending.Count;

    /// <summary>Queues a notice to be shown once the queue is released.</summary>
    public void Enqueue(string title, string message, ToolTipIcon icon) => _pending.Enqueue(new StartupBalloon(title, message, icon));

    /// <summary>Lets the queue go; returns the first notice to show now, if there is one.</summary>
    public StartupBalloon? Release()
    {
        Released = true;
        return Next();
    }

    /// <summary>
    /// A notice whose answer arrives after start-up. Queued like the others; returned for showing straight
    /// away only if the queue has already been released and is not in the middle of a gap.
    /// </summary>
    public StartupBalloon? EnqueueLate(string title, string message, ToolTipIcon icon)
    {
        Enqueue(title, message, icon);
        return Released && !Waiting ? Next() : null;
    }

    /// <summary>The gap is over: the next notice to show, if any.</summary>
    public StartupBalloon? OnSpacingElapsed()
    {
        Waiting = false;
        return Next();
    }

    private StartupBalloon? Next()
    {
        if (_pending.Count == 0)
        {
            Waiting = false;
            return null;
        }

        StartupBalloon next = _pending.Dequeue();
        Waiting = _pending.Count > 0; // more to come: the caller starts the gap timer
        return next;
    }
}
