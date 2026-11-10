namespace MonitorWellness.Core;

/// <summary>
/// Limits how often the quick "did another program reset the colour?" check may repair a display.
/// If something else keeps resetting the ramp (a second colour tool such as f.lux or Night Light),
/// repairing every few seconds would turn a slow tug-of-war into visible flicker, so after a few
/// repairs in a short window the quick check stands down for a while and only the normal 30-second
/// schedule tick applies the colour. Pure apart from the clock passed in, so it can be tested.
/// </summary>
public sealed class ResetRepairThrottle
{
    public const int MaxRepairsInWindow = 3;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan StandDown = TimeSpan.FromMinutes(10);

    private readonly Queue<DateTime> _repairs = new();
    private DateTime _standDownUntilUtc = DateTime.MinValue;

    /// <summary>Whether the quick check may run now.</summary>
    public bool IsAllowed(DateTime nowUtc) => nowUtc >= _standDownUntilUtc;

    /// <summary>Records repairs just made. Returns true if this tipped the throttle into standing down.</summary>
    public bool RecordRepairs(int count, DateTime nowUtc)
    {
        for (int i = 0; i < count; i++)
            _repairs.Enqueue(nowUtc);
        while (_repairs.Count > 0 && nowUtc - _repairs.Peek() > Window)
            _repairs.Dequeue();

        if (_repairs.Count < MaxRepairsInWindow)
            return false;

        _repairs.Clear();
        _standDownUntilUtc = nowUtc + StandDown;
        return true;
    }
}
