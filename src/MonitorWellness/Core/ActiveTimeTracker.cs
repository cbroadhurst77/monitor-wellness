namespace MonitorWellness.Core;

/// <summary>
/// Counts how long the person has actually been at the computer without a real break, from
/// periodic samples of "how long since the last keyboard/mouse input". Pure: all times are passed in.
///
/// A free-running timer cannot tell lunch from work: it reminds you a minute after you sit back
/// down, or misses a cycle because you happened to be idle when it ticked. This restarts the count
/// whenever a sample shows the person away, and also when the gap between samples is far longer
/// than the sampling period (the computer was asleep or the app was suspended).
/// </summary>
public sealed class ActiveTimeTracker
{
    /// <summary>A gap this many times the sampling period means the machine was not running normally.</summary>
    public const int SuspendedGapPeriods = 3;

    private DateTime? _lastSampleUtc;

    /// <summary>Time at the computer since the last break, as of the latest sample.</summary>
    public TimeSpan Accumulated { get; private set; }

    public bool IsDue(TimeSpan interval) => Accumulated >= interval;

    /// <summary>Starts counting again from zero (a break was taken, or reminders were turned on).</summary>
    public void Reset()
    {
        Accumulated = TimeSpan.Zero;
        _lastSampleUtc = null;
    }

    /// <summary>Sets the count directly, e.g. to make the next reminder come after a chosen delay instead of a full interval.</summary>
    public void SetAccumulated(TimeSpan accumulated) => Accumulated = accumulated < TimeSpan.Zero ? TimeSpan.Zero : accumulated;

    public void Sample(DateTime nowUtc, TimeSpan idle, TimeSpan samplePeriod, TimeSpan awayThreshold)
    {
        if (_lastSampleUtc is not DateTime last)
        {
            _lastSampleUtc = nowUtc; // the first sample only anchors the clock
            return;
        }

        TimeSpan elapsed = nowUtc - last;
        _lastSampleUtc = nowUtc;
        if (elapsed <= TimeSpan.Zero)
            return; // the clock moved backwards; count nothing for this sample

        if (elapsed > samplePeriod * SuspendedGapPeriods || idle >= awayThreshold)
        {
            Accumulated = TimeSpan.Zero;
            return;
        }

        Accumulated += elapsed;
    }
}
