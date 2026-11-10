namespace MonitorWellness.Core;

/// <summary>
/// Keeps a repeatedly failing optional operation from hammering the system or flooding the
/// log. After a failure the caller is told to stop trying for a growing interval, and only
/// some failures are worth a log line. Pure state, so it is testable without a clock.
/// Used for the ambient-light sensor, which once logged the same empty-message failure 1,250
/// times in a day.
/// </summary>
public sealed class FailureThrottle
{
    private static readonly TimeSpan[] DefaultBackoff =
    {
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(15),
    };

    private readonly TimeSpan[] _backoff;
    private DateTime _retryAfterUtc = DateTime.MinValue;

    public FailureThrottle(TimeSpan[]? backoff = null) => _backoff = backoff is { Length: > 0 } ? backoff : DefaultBackoff;

    /// <summary>Consecutive failures since the last success.</summary>
    public int ConsecutiveFailures { get; private set; }

    /// <summary>False while the operation is in its back-off window after a failure.</summary>
    public bool ShouldAttempt(DateTime nowUtc) => nowUtc >= _retryAfterUtc;

    /// <summary>
    /// Records a failure and starts the next back-off window. Returns true if this failure is
    /// worth logging: the first three, then every 20th.
    /// </summary>
    public bool RecordFailure(DateTime nowUtc)
    {
        ConsecutiveFailures++;
        _retryAfterUtc = nowUtc + _backoff[Math.Min(ConsecutiveFailures, _backoff.Length) - 1];
        return ConsecutiveFailures <= 3 || ConsecutiveFailures % 20 == 0;
    }

    /// <summary>Records a success and returns how many failures it ended (0 if it wasn't failing), so the caller can log a recovery.</summary>
    public int RecordSuccess()
    {
        int ended = ConsecutiveFailures;
        ConsecutiveFailures = 0;
        _retryAfterUtc = DateTime.MinValue;
        return ended;
    }
}
