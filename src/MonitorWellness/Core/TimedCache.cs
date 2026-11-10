namespace MonitorWellness.Core;

/// <summary>
/// Remembers one value for a short time. Used to stop the display-topology query (several Win32
/// calls per monitor) running on every schedule tick and five times a second while migraine
/// mode fades out, when the answer cannot change that fast. The clock is passed in, so tests need
/// no real time; thread-safe because diagnostics also ask from a background thread.
/// </summary>
public sealed class TimedCache<T>
{
    private readonly TimeSpan _lifetime;
    private readonly object _lock = new();
    private T? _value;
    private DateTime _expiresUtc = DateTime.MinValue;

    public TimedCache(TimeSpan lifetime) => _lifetime = lifetime;

    /// <summary>Returns the cached value if it is still fresh, otherwise calls <paramref name="create"/> and remembers its result.</summary>
    public T Get(Func<T> create, DateTime nowUtc)
    {
        lock (_lock)
        {
            if (_value is not null && nowUtc < _expiresUtc)
                return _value;

            T fresh = create();
            _value = fresh;
            _expiresUtc = nowUtc + _lifetime;
            return fresh;
        }
    }

    /// <summary>Forgets the value so the next <see cref="Get"/> recomputes it (for example after a display change).</summary>
    public void Invalidate()
    {
        lock (_lock)
        {
            _value = default;
            _expiresUtc = DateTime.MinValue;
        }
    }
}
