using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public sealed class BreakReminderPolicyTests
{
    [Fact]
    public void Decide_shows_reminder_when_person_is_active_and_screen_is_available()
    {
        BreakReminderDecision result = BreakReminderPolicy.Decide(false, false, TimeSpan.FromSeconds(30));

        Assert.Equal(BreakReminderDecision.Show, result);
    }

    [Fact]
    public void Decide_suppresses_reminder_during_migraine_mode()
    {
        BreakReminderDecision result = BreakReminderPolicy.Decide(true, false, TimeSpan.Zero);

        Assert.Equal(BreakReminderDecision.SuppressedForMigraine, result);
    }

    [Fact]
    public void Decide_suppresses_reminder_for_fullscreen_work()
    {
        BreakReminderDecision result = BreakReminderPolicy.Decide(false, true, TimeSpan.Zero);

        Assert.Equal(BreakReminderDecision.SuppressedForFullscreen, result);
    }

    [Fact]
    public void Decide_suppresses_reminder_when_person_has_already_taken_a_break()
    {
        BreakReminderDecision result = BreakReminderPolicy.Decide(false, false, BreakReminderPolicy.AwayThreshold);

        Assert.Equal(BreakReminderDecision.SuppressedForIdle, result);
    }
}
