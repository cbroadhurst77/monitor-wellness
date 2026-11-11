using System.Globalization;
using System.IO;
using System.Text.Json;

namespace MonitorWellness.Core;

/// <summary>
/// What the person last chose from the tray's Temporary Comfort Plan menu, and on which day.
/// <see cref="Plan"/> is empty when they chose "Return to saved schedule" (or Emergency Restore).
/// </summary>
public sealed record ComfortPlanState(string Plan, string Day);

/// <summary>
/// Makes the temporary comfort plan outlast a restart. The tray's plan used to vanish every time the app
/// restarted, and the log showed it being picked again after nearly every start (the "Reading" plan on 26
/// of 63 ordinary days). Now a manual choice is remembered until midnight, and a "start each day with
/// this plan" setting supplies the plan when there is no choice for today. Pure, apart from the small store below.
/// </summary>
public static class ComfortPlanMemory
{
    public static string DayKey(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>True if the person made a choice (including "back to the saved schedule") on this day.</summary>
    public static bool HasChoiceFor(ComfortPlanState? saved, DateOnly today)
        => saved is not null && string.Equals(saved.Day, DayKey(today), StringComparison.Ordinal);

    /// <summary>The plan to use when the app starts: today's own choice if there is one, otherwise the daily default (or none).</summary>
    public static string? PlanAtStartup(ComfortPlanState? saved, string? defaultPlan, DateOnly today)
        => HasChoiceFor(saved, today) ? Normalise(saved!.Plan) : Normalise(defaultPlan);

    /// <summary>The plan for a new day: yesterday's choice has expired, so the default applies again.</summary>
    public static string? PlanWhenDayChanges(string? defaultPlan) => Normalise(defaultPlan);

    public static ComfortPlanState Remember(string? plan, DateOnly today) => new(Normalise(plan) ?? "", DayKey(today));

    private static string? Normalise(string? plan) => SensoryComfortPlans.IsSupported(plan) ? plan : null;
}

/// <summary>Where the choice is kept: a tiny file next to settings.json, written by the app only (not part of the settings the Settings window edits).</summary>
public static class ComfortPlanStore
{
    private static string Path => System.IO.Path.Combine(AppDataFolder.FullPath, "comfort-plan.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>The saved choice, or null if there is none or the file is unreadable (it is only a convenience; nothing depends on it).</summary>
    public static ComfortPlanState? Load()
    {
        try
        {
            return File.Exists(Path)
                ? JsonSerializer.Deserialize<ComfortPlanState>(File.ReadAllText(Path), JsonOptions)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            DebugLog.Write($"ComfortPlanStore: couldn't read the remembered comfort plan: {ex.Message}");
            return null;
        }
    }

    public static void Save(ComfortPlanState state)
    {
        try
        {
            DurableFile.WriteAllText(Path, JsonSerializer.Serialize(state, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DebugLog.Write($"ComfortPlanStore: couldn't remember the comfort plan: {ex.Message}");
        }
    }
}
