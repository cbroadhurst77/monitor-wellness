using System.Collections.Concurrent;
using System.Diagnostics;

namespace MonitorWellness.Core;

/// <summary>What the manager needs from one monitor's DDC/CI session (implemented by the real Windows session, and by fakes in tests).</summary>
internal interface IHardwareBrightnessSession : IDisposable
{
    bool TryApplyNormalizedBrightness(double normalizedBrightness, out string error);
    bool TryRefreshHandles(out string error);
    void ForgetLastApplied();
}

/// <summary>
/// Uses physical brightness only for monitors a user explicitly approved after testing.
/// Any failure restores the original brightness and leaves the normal overlay as fallback.
///
/// Every DDC/CI call is a blocking round trip to the monitor: tens to hundreds of milliseconds, and
/// on some monitors and docks a hang. They therefore all run on one dedicated worker thread and the
/// UI thread never waits for them. Callers submit the latest target with <see cref="RequestApply"/>
/// (a newer request replaces an older one that has not started yet), read what is currently in
/// effect from <see cref="AppliedDevices"/>, and are told through <see cref="ResultChanged"/> (on the
/// UI thread) when something worth acting on happened. The worker thread is only created once a
/// monitor is actually approved, so most people never get one.
/// </summary>
public sealed class HardwareBrightnessControllerManager : IDisposable, IHardwareBrightness
{
    internal delegate bool SessionOpener(string deviceName, out IHardwareBrightnessSession? session, out string error);

    private sealed record ApplyRequest(List<string> ApprovedDevices, Dictionary<string, double> BrightnessByDevice);

    /// <summary>How long one call may run before the worker is considered stuck and the overlay takes over.</summary>
    public static readonly TimeSpan DefaultStuckAfter = TimeSpan.FromSeconds(10);

    private static readonly HardwareBrightnessApplicationResult NothingApplied =
        new(new HashSet<string>(StringComparer.OrdinalIgnoreCase), new Dictionary<string, string>());

    private readonly SessionOpener _open;
    private readonly Action<Action> _postToUi;
    private readonly TimeSpan _stuckAfter;
    private readonly object _gate = new();
    private readonly BlockingCollection<Action> _work = new();
    private readonly Dictionary<string, IHardwareBrightnessSession> _sessions = new(StringComparer.OrdinalIgnoreCase); // worker thread only
    private Thread? _thread;
    private ApplyRequest? _latest;                    // guarded by _gate
    private volatile HardwareBrightnessApplicationResult _lastResult = NothingApplied;
    private HardwareBrightnessApplicationResult? _lastReported; // worker thread only
    private volatile bool _mayHaveSessions;
    private long _busySinceTimestamp;                 // 0 while idle
    private bool _disposed;

    public HardwareBrightnessControllerManager()
        : this(OpenRealSession, PostToApplicationDispatcher, DefaultStuckAfter)
    {
    }

    internal HardwareBrightnessControllerManager(SessionOpener open, Action<Action> postToUi, TimeSpan stuckAfter)
    {
        _open = open;
        _postToUi = postToUi;
        _stuckAfter = stuckAfter;
    }

    /// <summary>
    /// Raised on the UI thread when the set of displays under hardware control changed, or a
    /// display failed. The handler should persist failures (quarantine) and re-apply the schedule so
    /// the overlay stops (or starts) dimming the affected displays.
    /// </summary>
    public event Action<HardwareBrightnessApplicationResult>? ResultChanged;

    /// <summary>True while a single DDC/CI call has been running longer than the stuck limit.</summary>
    public bool IsStuck
    {
        get
        {
            long since = Interlocked.Read(ref _busySinceTimestamp);
            return since != 0 && Stopwatch.GetElapsedTime(since) > _stuckAfter;
        }
    }

    /// <summary>
    /// Displays whose physical brightness is currently set by us, as of the last finished request.
    /// Empty while the worker is stuck, so that the overlay dims those displays instead.
    /// </summary>
    public IReadOnlySet<string> AppliedDevices => IsStuck ? NothingApplied.AppliedDeviceNames : _lastResult.AppliedDeviceNames;

    /// <summary>
    /// Asks for these displays to be set to these brightness levels. Returns at once; the work
    /// happens on the worker thread.
    /// </summary>
    public void RequestApply(IReadOnlyCollection<string> approvedDevices, IReadOnlyDictionary<string, double> brightnessByDevice)
    {
        ArgumentNullException.ThrowIfNull(approvedDevices);
        ArgumentNullException.ThrowIfNull(brightnessByDevice);
        if (_disposed)
            return;

        // Nothing approved and nothing open: nothing to do, and no thread needed.
        if (approvedDevices.Count == 0 && !_mayHaveSessions)
            return;

        if (approvedDevices.Count > 0)
            _mayHaveSessions = true;

        lock (_gate)
            _latest = new ApplyRequest(approvedDevices.ToList(), new Dictionary<string, double>(brightnessByDevice, StringComparer.OrdinalIgnoreCase));
        Enqueue(RunLatestRequest);
    }

    /// <summary>
    /// Called after resume from sleep: re-opens every session's handles and makes the next
    /// apply write unconditionally, since a monitor may have reset its own brightness while
    /// asleep (the "target unchanged" short-circuit would otherwise never correct that).
    /// </summary>
    public void RefreshAfterWake()
    {
        if (!_mayHaveSessions)
            return;

        Enqueue(() =>
        {
            foreach (var (deviceName, session) in _sessions.ToList())
            {
                if (!session.TryRefreshHandles(out string error))
                {
                    DebugLog.Write($"Hardware brightness handle refresh after wake failed for {deviceName}: {error}");
                    session.ForgetLastApplied();
                }
            }
        });
    }

    /// <summary>
    /// Puts every monitor's own brightness back and forgets any pending request. Waits for the
    /// worker, but never longer than <paramref name="timeout"/>: a monitor that hangs must not hang
    /// the caller (the recovery journal restores it at the next start). Returns false on timeout.
    /// </summary>
    public bool RestoreAll(TimeSpan timeout)
    {
        lock (_gate)
            _latest = null;

        if (!_mayHaveSessions && _thread is null)
            return true;

        using var done = new ManualResetEventSlim();
        if (!Enqueue(() =>
        {
            DisposeAllSessions();
            done.Set();
        }))
        {
            return true;
        }

        bool finished = done.Wait(timeout);
        if (!finished)
            DebugLog.Write($"Hardware brightness restore did not finish within {timeout.TotalSeconds:0.#}s; the recovery journal will restore it at the next start if needed.");
        return finished;
    }

    public void RestoreAll() => RestoreAll(TimeSpan.FromSeconds(3));

    public void Dispose()
    {
        if (_disposed)
            return;

        RestoreAll(TimeSpan.FromSeconds(3));
        lock (_gate)
        {
            _disposed = true;
            _work.CompleteAdding();
        }
    }

    // ---- worker side ----

    private bool Enqueue(Action work)
    {
        lock (_gate)
        {
            if (_disposed || _work.IsAddingCompleted)
                return false;

            if (_thread is null)
            {
                _thread = new Thread(WorkerLoop) { IsBackground = true, Name = "MonitorWellness-DDC" };
                _thread.Start();
            }
            _work.Add(work);
            return true;
        }
    }

    private void WorkerLoop()
    {
        foreach (Action work in _work.GetConsumingEnumerable())
        {
            Interlocked.Exchange(ref _busySinceTimestamp, Stopwatch.GetTimestamp());
            try
            {
                work();
            }
            catch (Exception ex)
            {
                // A background thread must never take the process down over a flaky monitor.
                DebugLog.Write($"Hardware brightness worker: {ex}");
            }
            finally
            {
                Interlocked.Exchange(ref _busySinceTimestamp, 0);
            }
        }

        DisposeAllSessions();
    }

    private void RunLatestRequest()
    {
        ApplyRequest? request;
        lock (_gate)
        {
            request = _latest;
            _latest = null;
        }
        if (request is null)
            return; // a newer request already ran, or a restore cancelled it

        HardwareBrightnessApplicationResult result = ApplyCore(request.ApprovedDevices, request.BrightnessByDevice);
        _mayHaveSessions = _sessions.Count > 0;
        _lastResult = result;

        if (!IsWorthReporting(result))
            return;

        _lastReported = result;
        _postToUi(() => ResultChanged?.Invoke(result));
    }

    private bool IsWorthReporting(HardwareBrightnessApplicationResult result)
        => result.FailuresByDeviceName.Count > 0
            || _lastReported is null && result.AppliedDeviceNames.Count > 0
            || _lastReported is not null && !_lastReported.AppliedDeviceNames.SetEquals(result.AppliedDeviceNames);

    private HardwareBrightnessApplicationResult ApplyCore(List<string> approvedDevices, Dictionary<string, double> brightnessByDevice)
    {
        var approved = approvedDevices.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string staleDevice in _sessions.Keys.Where(deviceName => !approved.Contains(deviceName)).ToList())
            DisposeSession(staleDevice);

        var applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string deviceName in approved)
        {
            if (!brightnessByDevice.TryGetValue(deviceName, out double brightness))
            {
                DisposeSession(deviceName);
                continue;
            }

            if (!_sessions.TryGetValue(deviceName, out IHardwareBrightnessSession? session))
            {
                if (!_open(deviceName, out session, out string openError) || session is null)
                {
                    DebugLog.Write($"Hardware brightness unavailable for {deviceName}; using overlay fallback: {openError}");
                    failures[deviceName] = openError;
                    continue;
                }
                _sessions[deviceName] = session;
            }

            if (!session.TryApplyNormalizedBrightness(brightness, out string applyError))
            {
                // Physical-monitor handles commonly go stale after sleep or a dock/replug, and
                // a stale handle looks exactly like a broken monitor. Re-open the handles and
                // try once more before treating this as a real failure (which quarantines).
                string refreshError = "";
                bool retried = session.TryRefreshHandles(out refreshError)
                    && session.TryApplyNormalizedBrightness(brightness, out applyError);
                if (!retried)
                {
                    DebugLog.Write($"Hardware brightness apply failed for {deviceName}; using overlay fallback: {applyError}{(string.IsNullOrEmpty(refreshError) ? "" : $" (handle refresh: {refreshError})")}");
                    DisposeSession(deviceName);
                    failures[deviceName] = applyError;
                    continue;
                }
                DebugLog.Write($"Hardware brightness for {deviceName} recovered after re-opening its monitor handles.");
            }

            applied.Add(deviceName);
        }

        return new HardwareBrightnessApplicationResult(applied, failures);
    }

    private void DisposeAllSessions()
    {
        foreach (string deviceName in _sessions.Keys.ToList())
            DisposeSession(deviceName);
        _mayHaveSessions = false;
        _lastResult = NothingApplied;
        _lastReported = null;
    }

    private void DisposeSession(string deviceName)
    {
        if (_sessions.Remove(deviceName, out IHardwareBrightnessSession? session))
            session.Dispose();
    }

    private static bool OpenRealSession(string deviceName, out IHardwareBrightnessSession? session, out string error)
    {
        bool opened = DdcCiBrightnessProbe.TryOpenTestSession(deviceName, out DdcCiBrightnessTestSession? real, out error);
        session = opened ? real : null;
        return opened && session is not null;
    }

    private static void PostToApplicationDispatcher(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
            action();
        else
            dispatcher.BeginInvoke(action);
    }
}

/// <summary>Outcome of one scheduled DDC/CI application attempt.</summary>
public sealed record HardwareBrightnessApplicationResult(
    IReadOnlySet<string> AppliedDeviceNames,
    IReadOnlyDictionary<string, string> FailuresByDeviceName);
