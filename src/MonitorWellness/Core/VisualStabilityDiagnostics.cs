using System.Threading;

namespace MonitorWellness.Core;

/// <summary>
/// Startup-session counters for display-safety troubleshooting. They contain no window titles,
/// content, settings, or personal data—only whether the app avoided redundant display work.
/// </summary>
public sealed record VisualStabilitySnapshot(
    long DisplayTopologySignals,
    long CoalescedTopologyRefreshes,
    long ForegroundDisplayWritesAvoided);

public static class VisualStabilityDiagnostics
{
    private static long _displayTopologySignals;
    private static long _coalescedTopologyRefreshes;
    private static long _foregroundDisplayWritesAvoided;

    public static void RecordDisplayTopologySignal() => Interlocked.Increment(ref _displayTopologySignals);

    public static void RecordCoalescedTopologyRefresh() => Interlocked.Increment(ref _coalescedTopologyRefreshes);

    public static void RecordForegroundDisplayWriteAvoided() => Interlocked.Increment(ref _foregroundDisplayWritesAvoided);

    public static VisualStabilitySnapshot GetSnapshot() => new(
        Interlocked.Read(ref _displayTopologySignals),
        Interlocked.Read(ref _coalescedTopologyRefreshes),
        Interlocked.Read(ref _foregroundDisplayWritesAvoided));
}
