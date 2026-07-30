using System.IO;
using System.Text.Json;

namespace MonitorWellness.Core;

/// <summary>
/// One logged occurrence — a migraine-mode activation/deactivation, a schedule pause/resume,
/// or a "MigraineRating" answer (Rating 1-5) to the optional post-use helpfulness prompt
/// (AppSettings.PromptForMigraineRating). Rating defaults to null so every event logged before
/// this field existed still deserializes correctly — System.Text.Json fills in a missing JSON
/// member from the constructor parameter's default rather than failing.
/// </summary>
public sealed record HistoryEvent(DateTime TimestampUtc, string EventType, bool? Mild, int? DurationSeconds, int? Rating = null);

/// <summary>
/// Optional, fully local log of migraine-mode activations and schedule pauses — opt-in via
/// AppSettings.HistoryTrackingEnabled (default false, so this app's "no telemetry" story stays
/// true by default). See TECHNICAL_UX_REVIEW.md §1.5/§7.1: this app already has all the data
/// needed to help a user notice their own patterns (does migraine mode frequency change with a
/// lower daytime brightness? does it cluster on certain days?) and previously threw it away.
/// Nothing here is ever sent anywhere — same %AppData%\MonitorWellness\ local-only pattern as
/// SettingsStore and ProfileStore.
/// </summary>
public static class HistoryStore
{
    private static readonly string HistoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MonitorWellness",
        "history.jsonl");

    private static readonly JsonSerializerOptions JsonOptions = new();

    /// <summary>Appends one event as a single JSON line — cheap to append without rewriting the whole file, unlike settings.json/profiles.</summary>
    public static void Append(HistoryEvent evt)
    {
        try
        {
            string? directory = Path.GetDirectoryName(HistoryPath);
            if (directory is not null)
                Directory.CreateDirectory(directory);
            File.AppendAllText(HistoryPath, JsonSerializer.Serialize(evt, JsonOptions) + Environment.NewLine);
        }
        catch (IOException ex)
        {
            DebugLog.Write($"HistoryStore.Append failed: {ex.Message}");
        }
    }

    /// <summary>Loads every recorded event. A malformed individual line (e.g. from an interrupted write) is skipped rather than losing the rest of the history.</summary>
    public static IReadOnlyList<HistoryEvent> Load()
    {
        try
        {
            if (!File.Exists(HistoryPath))
                return Array.Empty<HistoryEvent>();

            var events = new List<HistoryEvent>();
            foreach (string line in File.ReadAllLines(HistoryPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var evt = JsonSerializer.Deserialize<HistoryEvent>(line, JsonOptions);
                    if (evt is not null)
                        events.Add(evt);
                }
                catch (JsonException)
                {
                    // one malformed line shouldn't lose the rest of the log
                }
            }
            return events;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DebugLog.Write($"HistoryStore.Load failed: {ex.Message}");
            return Array.Empty<HistoryEvent>();
        }
    }

    /// <summary>Deletes the whole history log — the user's own data to discard whenever they want, no confirmation needed at this layer (the UI asks).</summary>
    public static void Clear()
    {
        try
        {
            if (File.Exists(HistoryPath))
                File.Delete(HistoryPath);
        }
        catch (IOException ex)
        {
            DebugLog.Write($"HistoryStore.Clear failed: {ex.Message}");
        }
    }
}

/// <summary>Aggregated counts a user can actually read at a glance, rather than a raw event list.</summary>
public sealed record HistorySummary(
    int TotalActivations,
    int ActivationsLast7Days,
    int ActivationsLast30Days,
    double? AverageDurationMinutes,
    int MildCount,
    int FullCount,
    int PauseCount,
    double? AverageRating,
    int RatingCount);

/// <summary>Pure aggregation over a HistoryEvent list — kept separate from HistoryStore's file I/O specifically so it's testable without touching disk.</summary>
public static class HistorySummarizer
{
    public static HistorySummary Summarize(IReadOnlyList<HistoryEvent> events, DateTime nowUtc)
    {
        var activations = events.Where(e => e.EventType == "MigraineActivated").ToList();
        var deactivationsWithDuration = events
            .Where(e => e.EventType == "MigraineDeactivated" && e.DurationSeconds.HasValue)
            .ToList();
        int pauseCount = events.Count(e => e.EventType == "SchedulePaused");
        var ratings = events
            .Where(e => e.EventType == "MigraineRating" && e.Rating.HasValue)
            .Select(e => e.Rating!.Value)
            .ToList();

        int last7 = activations.Count(e => (nowUtc - e.TimestampUtc).TotalDays is >= 0 and <= 7);
        int last30 = activations.Count(e => (nowUtc - e.TimestampUtc).TotalDays is >= 0 and <= 30);
        int mild = activations.Count(e => e.Mild == true);
        int full = activations.Count(e => e.Mild != true);

        double? averageDurationMinutes = deactivationsWithDuration.Count > 0
            ? deactivationsWithDuration.Average(e => e.DurationSeconds!.Value) / 60.0
            : null;
        double? averageRating = ratings.Count > 0 ? ratings.Average() : null;

        return new HistorySummary(activations.Count, last7, last30, averageDurationMinutes, mild, full, pauseCount, averageRating, ratings.Count);
    }
}
