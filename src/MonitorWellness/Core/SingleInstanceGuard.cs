namespace MonitorWellness.Core;

/// <summary>
/// Prevents a second copy of the app from running in the same user session at once, via a
/// named system Mutex. This app is both portable ("run the downloaded exe directly, no
/// installer needed" — README) and self-registers for Task Scheduler auto-start
/// (AutoStartManager) — a user auto-starting the app and later double-clicking the same
/// portable exe again (forgetting it's already running), or running two copies from different
/// folders, is a realistic scenario this combination invites, not a hypothetical edge case (see
/// TECHNICAL_UX_REVIEW.md §3.1). Deliberately not a "Global\" mutex: this only needs to stop a
/// second instance within the same user session, matching how the app is actually used (a
/// per-user tray app), and avoids requiring the elevated privilege "Global\" objects need in
/// some restricted/multi-session environments.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    public const string DefaultMutexName = "MonitorWellness-SingleInstance-3F1E9C2A";

    private Mutex? _mutex;
    private readonly bool _acquired;
    private bool _disposed;

    /// <summary>True if this process is the one-and-only instance (successfully acquired the mutex).</summary>
    public bool IsPrimaryInstance => _acquired;

    /// <summary>mutexName is overridable purely so automated tests can use a unique name per test run rather than colliding with each other or a real running app.</summary>
    public SingleInstanceGuard(string? mutexName = null)
    {
        _mutex = new Mutex(initiallyOwned: true, mutexName ?? DefaultMutexName, out bool createdNew);
        _acquired = createdNew;

        if (!_acquired)
        {
            // Someone else already holds it (or held it and abandoned it) — we're not the
            // owner regardless, so release this handle immediately rather than holding a
            // mutex we don't actually own.
            _mutex.Dispose();
            _mutex = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_acquired)
            _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        _mutex = null;
    }
}
