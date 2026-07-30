namespace MonitorWellness.Core;

/// <summary>
/// Pure time-math for the "Pause Schedule" tray feature — kept separate from App.xaml.cs so
/// the "until tomorrow" calculation (the one non-trivial piece; the fixed-duration options
/// are just DateTime.UtcNow + TimeSpan) has automated test coverage rather than being buried
/// in a WPF-hosted class with no test access.
/// </summary>
public static class SchedulePause
{
    /// <summary>
    /// "Until tomorrow" always resolves to 08:00 on the next calendar day, regardless of the
    /// current time — simpler and more predictable than trying to guess a wake time.
    /// </summary>
    public static DateTime ComputeUntilTomorrowLocal(DateTime nowLocal)
        => nowLocal.Date.AddDays(1).AddHours(8);
}
