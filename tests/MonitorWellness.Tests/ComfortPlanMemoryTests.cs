#pragma warning disable CA1861 // small literal arrays in tests are clearer than shared static fields
using System.IO;
using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class ComfortPlanMemoryTests
{
    private static readonly DateOnly Today = new(2026, 11, 11);
    private static readonly DateOnly Yesterday = new(2026, 11, 10);

    [Fact]
    public void A_plan_chosen_today_is_still_the_plan_after_a_restart()
        => Assert.Equal("Reading", ComfortPlanMemory.PlanAtStartup(ComfortPlanMemory.Remember("Reading", Today), null, Today));

    [Fact]
    public void Going_back_to_the_saved_schedule_today_is_remembered_too_even_with_a_daily_default()
        => Assert.Null(ComfortPlanMemory.PlanAtStartup(ComfortPlanMemory.Remember(null, Today), "Reading", Today));

    [Fact]
    public void A_choice_from_yesterday_has_ended_and_the_daily_default_applies()
    {
        var yesterdays = ComfortPlanMemory.Remember("Recovery", Yesterday);

        Assert.Equal("Reading", ComfortPlanMemory.PlanAtStartup(yesterdays, "Reading", Today));
        Assert.Null(ComfortPlanMemory.PlanAtStartup(yesterdays, "", Today));
    }

    [Fact]
    public void With_no_choice_at_all_the_default_is_used_and_nothing_means_nothing()
    {
        Assert.Equal("Balanced", ComfortPlanMemory.PlanAtStartup(null, "Balanced", Today));
        Assert.Null(ComfortPlanMemory.PlanAtStartup(null, "", Today));
        Assert.Null(ComfortPlanMemory.PlanAtStartup(null, null, Today));
    }

    [Fact]
    public void An_unknown_plan_name_is_never_applied()
    {
        Assert.Null(ComfortPlanMemory.PlanAtStartup(null, "Turbo", Today));
        Assert.Null(ComfortPlanMemory.PlanAtStartup(new ComfortPlanState("Turbo", ComfortPlanMemory.DayKey(Today)), "Reading", Today));
        Assert.Equal("", ComfortPlanMemory.Remember("Turbo", Today).Plan);
    }

    [Fact]
    public void A_new_day_falls_back_to_the_default()
    {
        Assert.Equal("Reading", ComfortPlanMemory.PlanWhenDayChanges("Reading"));
        Assert.Null(ComfortPlanMemory.PlanWhenDayChanges(""));
    }

    [Fact]
    public void Whether_a_choice_exists_for_today_depends_only_on_the_day()
    {
        Assert.True(ComfortPlanMemory.HasChoiceFor(ComfortPlanMemory.Remember(null, Today), Today));
        Assert.False(ComfortPlanMemory.HasChoiceFor(ComfortPlanMemory.Remember("Reading", Yesterday), Today));
        Assert.False(ComfortPlanMemory.HasChoiceFor(null, Today));
        Assert.Equal("2026-11-11", ComfortPlanMemory.DayKey(Today));
    }

    [Fact]
    public void The_default_comfort_plan_is_off_by_default_and_survives_clone_and_copy()
    {
        Assert.Equal("", new AppSettings().DefaultComfortPlan);

        var original = new AppSettings { DefaultComfortPlan = "Reading" };
        Assert.Equal("Reading", original.Clone().DefaultComfortPlan);
        var target = new AppSettings();
        target.CopyFrom(original);
        Assert.Equal("Reading", target.DefaultComfortPlan);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("Reading", true)]
    [InlineData("Recovery", true)]
    [InlineData("Turbo", false)]
    public void Only_known_plans_or_none_are_valid_and_a_bad_one_is_repaired_alone(string plan, bool valid)
    {
        var settings = new AppSettings { DefaultComfortPlan = plan, DayKelvin = 5900 };

        Assert.Equal(valid, AppSettingsValidator.TryValidate(settings, out _));
        if (!valid)
        {
            Assert.Contains("default comfort plan", SettingsRepair.Repair(settings));
            Assert.Equal("", settings.DefaultComfortPlan);
            Assert.Equal(5900, settings.DayKelvin);
        }
    }
}

[Collection("AppDataStores")]
public sealed class ComfortPlanStoreTests : IDisposable
{
    private static string FilePath => Path.Combine(AppDataFolder.FullPath, "comfort-plan.json");

    public ComfortPlanStoreTests()
    {
        Directory.CreateDirectory(AppDataFolder.FullPath);
        if (File.Exists(FilePath))
            File.Delete(FilePath);
    }

    public void Dispose()
    {
        if (File.Exists(FilePath))
            File.Delete(FilePath);
    }

    [Fact]
    public void The_choice_round_trips_through_the_file()
    {
        ComfortPlanStore.Save(new ComfortPlanState("Reading", "2026-11-11"));

        Assert.Equal(new ComfortPlanState("Reading", "2026-11-11"), ComfortPlanStore.Load());
    }

    [Fact]
    public void No_file_or_a_damaged_file_just_means_no_remembered_choice()
    {
        Assert.Null(ComfortPlanStore.Load());

        File.WriteAllText(FilePath, "{ this is not json");
        Assert.Null(ComfortPlanStore.Load());

        File.WriteAllText(FilePath, "null");
        Assert.Null(ComfortPlanStore.Load());
    }

    [Fact]
    public void Saving_again_replaces_the_choice()
    {
        ComfortPlanStore.Save(ComfortPlanMemory.Remember("Reading", new DateOnly(2026, 11, 11)));
        ComfortPlanStore.Save(ComfortPlanMemory.Remember(null, new DateOnly(2026, 11, 11)));

        Assert.Equal("", ComfortPlanStore.Load()!.Plan);
    }
}
