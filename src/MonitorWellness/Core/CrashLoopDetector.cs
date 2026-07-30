namespace MonitorWellness.Core;

/// <summary>
/// Distinguishes "one rare hiccup, worth logging and quietly recovering from" from "something
/// is seriously broken and repeatedly failing" — see TECHNICAL_UX_REVIEW.md §3.2. The app's
/// DispatcherUnhandledException handler previously swallowed every unhandled exception
/// unconditionally, forever, with a code comment marking that as a decision still owed before
/// v1 shipped. Unconditionally swallowing risks the app limping along in a silently broken
/// state indefinitely; this gives App.xaml.cs a way to tell "isolated" from "looping" and only
/// bother the user in the latter case.
/// </summary>
public sealed class CrashLoopDetector
{
    private readonly int _threshold;
    private readonly TimeSpan _window;
    private readonly Queue<DateTime> _recentUtc = new();

    public CrashLoopDetector(int threshold = 5, TimeSpan? window = null)
    {
        _threshold = threshold;
        _window = window ?? TimeSpan.FromMinutes(1);
    }

    /// <summary>
    /// Records an occurrence at nowUtc and returns true once the threshold has been reached
    /// within the trailing window. Takes nowUtc as a parameter (rather than reading
    /// DateTime.UtcNow itself) specifically so this is testable without wall-clock sleeps.
    /// </summary>
    public bool RecordAndCheckIsLooping(DateTime nowUtc)
    {
        _recentUtc.Enqueue(nowUtc);
        while (_recentUtc.Count > 0 && (nowUtc - _recentUtc.Peek()) > _window)
            _recentUtc.Dequeue();

        return _recentUtc.Count >= _threshold;
    }
}
