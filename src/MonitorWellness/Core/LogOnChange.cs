namespace MonitorWellness.Core;

/// <summary>
/// Lets a periodic event log only when something changed (or as an occasional heartbeat), so a
/// timer that fires every half hour doesn't make up a fifth of the log and push out older,
/// more useful history. Pure apart from the clock passed in, so it can be tested.
/// </summary>
public sealed class LogOnChange(TimeSpan heartbeat)
{
    private string? _lastKey;
    private DateTime _lastLoggedUtc;

    /// <summary>True when <paramref name="key"/> differs from the last logged one, or the heartbeat has elapsed.</summary>
    public bool ShouldLog(string key, DateTime nowUtc)
    {
        if (string.Equals(key, _lastKey, StringComparison.Ordinal) && nowUtc - _lastLoggedUtc < heartbeat)
            return false;

        _lastKey = key;
        _lastLoggedUtc = nowUtc;
        return true;
    }
}
