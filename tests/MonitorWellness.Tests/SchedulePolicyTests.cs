using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class SchedulePolicyTests
{
    private static ApplicationComfortRule NativeRule(string process = "photoshop")
        => new() { ProcessName = process, Action = ApplicationComfortActions.RestoreNativeDisplay };

    private static ApplicationComfortRule PlanRule(string plan, string process = "winword")
        => new() { ProcessName = process, Action = ApplicationComfortActions.ApplySensoryComfortPlan, ComfortPlanName = plan };

    private static ScheduleDecision Decide(
        bool migraine = false,
        bool preview = false,
        bool paused = false,
        bool guardEnabled = false,
        bool fullscreen = false,
        ApplicationComfortRule? rule = null,
        string? manualPlan = null,
        Action? onFullscreenProbe = null,
        Action? onRuleProbe = null)
        => SchedulePolicy.Decide(new ScheduleDecisionInputs(
            migraine, preview, paused, guardEnabled,
            () => { onFullscreenProbe?.Invoke(); return fullscreen; },
            () => { onRuleProbe?.Invoke(); return rule; },
            manualPlan));

    [Fact]
    public void NothingSpecial_AppliesTheSavedSchedule()
    {
        ScheduleDecision decision = Decide();

        Assert.Equal(ScheduleActionKind.ApplySchedule, decision.Action);
        Assert.Null(decision.Override);
        Assert.Equal(ScheduleOverrideSource.None, decision.OverrideSource);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void MigraineAPreviewOrAPause_SkipsBeforeTouchingAnyDetector(bool migraine, bool preview, bool paused)
    {
        bool probed = false;

        ScheduleDecision decision = Decide(migraine, preview, paused, guardEnabled: true, fullscreen: true,
            rule: NativeRule(), onFullscreenProbe: () => probed = true, onRuleProbe: () => probed = true);

        Assert.Equal(ScheduleActionKind.Skip, decision.Action);
        Assert.False(probed); // the Win32-backed detectors must not run when the answer is already "skip"
    }

    [Fact]
    public void TheFullscreenGuard_BeatsEveryRuleAndPlan()
    {
        ScheduleDecision decision = Decide(guardEnabled: true, fullscreen: true, rule: NativeRule(), manualPlan: SensoryComfortPlans.Reading);

        Assert.Equal(ScheduleActionKind.FullscreenGuard, decision.Action);
    }

    [Fact]
    public void AFullscreenAppDoesNothing_WhenTheGuardIsOff_AndTheFullscreenProbeIsNotEvenRun()
    {
        bool probed = false;

        ScheduleDecision decision = Decide(guardEnabled: false, fullscreen: true, onFullscreenProbe: () => probed = true);

        Assert.Equal(ScheduleActionKind.ApplySchedule, decision.Action);
        Assert.False(probed);
    }

    [Fact]
    public void ANativeDisplayRule_BeatsAManualPlan()
    {
        ApplicationComfortRule rule = NativeRule();

        ScheduleDecision decision = Decide(rule: rule, manualPlan: SensoryComfortPlans.Recovery);

        Assert.Equal(ScheduleActionKind.NativeDisplayRule, decision.Action);
        Assert.Same(rule, decision.NativeDisplayRule);
    }

    [Fact]
    public void AManualPlan_BeatsAnApplicationComfortPlanRule()
    {
        ScheduleDecision decision = Decide(rule: PlanRule(SensoryComfortPlans.Reading), manualPlan: SensoryComfortPlans.Recovery);

        Assert.Equal(ScheduleActionKind.ApplySchedule, decision.Action);
        Assert.Equal(ScheduleOverrideSource.ManualPlan, decision.OverrideSource);
        Assert.True(SensoryComfortPlans.TryGetSchedule(SensoryComfortPlans.Recovery, out SensoryComfortSchedule recovery));
        Assert.Equal(recovery, decision.Override);
    }

    [Fact]
    public void AnApplicationComfortPlanRule_AppliesItsPlan_AndDescribesItForTheStatusLine()
    {
        ScheduleDecision decision = Decide(rule: PlanRule(SensoryComfortPlans.Reading, "winword"));

        Assert.Equal(ScheduleOverrideSource.ApplicationRule, decision.OverrideSource);
        Assert.NotNull(decision.Override);
        Assert.Equal($"{SensoryComfortPlans.Reading} comfort plan for winword", decision.RuleDescription);
    }

    [Fact]
    public void AnUnknownPlanName_FallsThroughToTheNextOption()
    {
        // A stale manual selection or a hand-edited rule must not break the schedule.
        ScheduleDecision manualUnknown = Decide(manualPlan: "No such plan", rule: PlanRule(SensoryComfortPlans.Reading));
        Assert.Equal(ScheduleOverrideSource.ApplicationRule, manualUnknown.OverrideSource);

        ScheduleDecision bothUnknown = Decide(manualPlan: "No such plan", rule: PlanRule("Also missing"));
        Assert.Equal(ScheduleActionKind.ApplySchedule, bothUnknown.Action);
        Assert.Equal(ScheduleOverrideSource.None, bothUnknown.OverrideSource);
    }
}
