namespace MonitorWellness.Core;

/// <summary>Where the remembered choice is kept; the app uses a small file, tests use a fake.</summary>
public interface IComfortPlanStore
{
    ComfortPlanState? Load();
    void Save(ComfortPlanState state);
}

/// <summary>The real store: <see cref="ComfortPlanStore"/>'s file in the app-data folder.</summary>
public sealed class FileComfortPlanStore : IComfortPlanStore
{
    public ComfortPlanState? Load() => ComfortPlanStore.Load();

    public void Save(ComfortPlanState state) => ComfortPlanStore.Save(state);
}

/// <summary>
/// The whole life of the temporary comfort plan, in one place: restored at start-up, picked from the tray,
/// returned to the saved schedule, cleared by Emergency Restore, replaced by the daily default at midnight,
/// and the default re-applied when it is changed in Settings. This was spread over eight methods in the main
/// App class, where none of it could be tested. It acts on the shared <see cref="ScheduleOverrideState"/>
/// (the schedule reads the plan from there) and returns whether something changed so the caller knows to
/// re-apply the schedule.
/// </summary>
public sealed class ComfortPlanCoordinator
{
    private readonly ScheduleOverrideState _overrides;
    private readonly IComfortPlanStore _store;
    private readonly Func<string?> _defaultPlan;
    private readonly Func<DateOnly> _today;
    private readonly Action<string> _log;
    private DateOnly _day;

    public ComfortPlanCoordinator(
        ScheduleOverrideState overrides,
        IComfortPlanStore store,
        Func<string?> defaultPlan,
        Func<DateOnly> today,
        Action<string>? log = null)
    {
        _overrides = overrides;
        _store = store;
        _defaultPlan = defaultPlan;
        _today = today;
        _log = log ?? (_ => { });
        _day = today();
    }

    /// <summary>The plan in force now, or null for the normal schedule.</summary>
    public string? Current => _overrides.ManualComfortPlanName;

    /// <summary>At start-up: today's remembered choice if there is one, otherwise the "start each day with" plan.</summary>
    public void RestoreAtStart()
    {
        _day = _today();
        string? plan = ComfortPlanMemory.PlanAtStartup(_store.Load(), _defaultPlan(), _day);
        _overrides.ManualComfortPlanName = plan;
        if (plan is not null)
            _log($"Comfort plan restored at start: {plan}.");
    }

    /// <summary>A plan picked from the tray. Lasts until midnight and survives a restart. False if it is not a known plan.</summary>
    public bool Select(string plan)
    {
        if (!SensoryComfortPlans.IsSupported(plan))
            return false;

        Remember(plan);
        _overrides.ManualComfortPlanName = plan;
        _log($"Temporary comfort plan selected: {plan}.");
        return true;
    }

    /// <summary>"Return to saved schedule": no plan for the rest of today, even after a restart. False if there was no plan to end.</summary>
    public bool ReturnToSavedSchedule()
    {
        if (_overrides.ManualComfortPlanName is null)
            return false;

        _log($"Temporary comfort plan cleared: {_overrides.ManualComfortPlanName}.");
        Remember(null);
        _overrides.ManualComfortPlanName = null;
        return true;
    }

    /// <summary>Emergency Restore means "native screen today", also after a restart. False if no plan was active.</summary>
    public bool EmergencyRestore()
    {
        if (_overrides.ManualComfortPlanName is null)
            return false;

        _log($"Emergency Restore Screen cleared temporary comfort plan: {_overrides.ManualComfortPlanName}.");
        Remember(null);
        _overrides.ManualComfortPlanName = null;
        return true;
    }

    /// <summary>Call regularly. At a new day yesterday's choice has ended and the daily default applies again. True if the plan changed.</summary>
    public bool OnTick()
    {
        DateOnly today = _today();
        if (today == _day)
            return false;

        _day = today;
        string? plan = ComfortPlanMemory.PlanWhenDayChanges(_defaultPlan());
        if (string.Equals(plan, _overrides.ManualComfortPlanName, StringComparison.Ordinal))
            return false;

        _overrides.ManualComfortPlanName = plan;
        _log(plan is null ? "New day: the comfort plan chosen yesterday has ended." : $"New day: starting with the {plan} comfort plan.");
        return true;
    }

    /// <summary>Changing "start each day with" in Settings takes effect now, unless the person already chose something today. True if the plan changed.</summary>
    public bool ApplyDefaultIfNoChoiceToday()
    {
        DateOnly today = _today();
        if (ComfortPlanMemory.HasChoiceFor(_store.Load(), today))
            return false;

        string? plan = ComfortPlanMemory.PlanAtStartup(null, _defaultPlan(), today);
        if (string.Equals(plan, _overrides.ManualComfortPlanName, StringComparison.Ordinal))
            return false;

        _overrides.ManualComfortPlanName = plan;
        _log(plan is null ? "Default comfort plan cleared." : $"Default comfort plan set to {plan}.");
        return true;
    }

    private void Remember(string? plan) => _store.Save(ComfortPlanMemory.Remember(plan, _today()));
}
