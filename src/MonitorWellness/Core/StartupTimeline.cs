using System.Diagnostics;
using System.Globalization;

namespace MonitorWellness.Core;

/// <summary>
/// Records how long each step of starting the app took, so that a slow start (a cold boot took 43
/// seconds once, with a 28-second silent gap on the UI thread) says afterwards which step was slow
/// instead of leaving a guess. Cheap enough to leave on. The clock is injectable so it can be tested.
/// </summary>
public sealed class StartupTimeline
{
    private readonly Func<TimeSpan> _elapsed;
    private readonly List<(string Step, TimeSpan Duration)> _steps = new();
    private TimeSpan _lastMark;

    public StartupTimeline(Func<TimeSpan>? elapsed = null)
    {
        if (elapsed is null)
        {
            var stopwatch = Stopwatch.StartNew();
            elapsed = () => stopwatch.Elapsed;
        }
        _elapsed = elapsed;
    }

    /// <summary>Ends the current step: the time since the previous mark (or the start) is attributed to <paramref name="step"/>.</summary>
    public void Mark(string step)
    {
        TimeSpan now = _elapsed();
        _steps.Add((step, now - _lastMark));
        _lastMark = now;
    }

    public TimeSpan Total => _lastMark;

    public IReadOnlyList<(string Step, TimeSpan Duration)> Steps => _steps;

    /// <summary>The steps that took at least <paramref name="threshold"/>, slowest first.</summary>
    public IReadOnlyList<(string Step, TimeSpan Duration)> SlowSteps(TimeSpan threshold)
        => _steps.Where(step => step.Duration >= threshold).OrderByDescending(step => step.Duration).ToList();

    /// <summary>One line for the log: the total and every step with its time in milliseconds.</summary>
    public string Summary()
        => string.Create(CultureInfo.InvariantCulture,
            $"Startup took {Total.TotalMilliseconds:0} ms: {string.Join(", ", _steps.Select(step => $"{step.Step} {step.Duration.TotalMilliseconds:0} ms"))}");
}
