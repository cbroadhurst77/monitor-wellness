using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class BreakReminderStateTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly BreakReminderSignals Quiet = new(false, false, TimeSpan.Zero);

    [Fact]
    public void Tick_with_no_snooze_and_quiet_signals_shows()
    {
        var state = new BreakReminderState();

        var tick = state.Tick(Now, () => Quiet);

        Assert.Equal(BreakReminderTickOutcome.Show, tick.Outcome);
        Assert.False(tick.SnoozeExpired);
    }

    [Fact]
    public void Snoozed_tick_is_skipped_and_never_reads_signals()
    {
        var state = new BreakReminderState();
        Assert.True(state.TrySnooze(true, Now, TimeSpan.FromMinutes(30)));
        bool read = false;

        var tick = state.Tick(Now.AddMinutes(10), () => { read = true; return Quiet; });

        Assert.Equal(BreakReminderTickOutcome.SkippedSnoozed, tick.Outcome);
        Assert.Equal(Now.AddMinutes(30), tick.SnoozedUntilUtc);
        Assert.False(read);
    }

    [Fact]
    public void Expired_snooze_is_cleared_and_the_tick_proceeds()
    {
        var state = new BreakReminderState();
        state.TrySnooze(true, Now, TimeSpan.FromMinutes(30));

        var tick = state.Tick(Now.AddMinutes(31), () => Quiet);

        Assert.True(tick.SnoozeExpired);
        Assert.Equal(BreakReminderTickOutcome.Show, tick.Outcome);
        Assert.Null(state.SnoozedUntilUtc);
    }

    [Theory]
    [InlineData(true, false, 0, BreakReminderDecision.SuppressedForMigraine)]
    [InlineData(false, true, 0, BreakReminderDecision.SuppressedForFullscreen)]
    [InlineData(false, false, 180, BreakReminderDecision.SuppressedForIdle)]
    public void Suppressing_signals_produce_a_suppressed_tick(bool migraine, bool fullscreen, int idleSeconds, BreakReminderDecision expected)
    {
        var state = new BreakReminderState();

        var tick = state.Tick(Now, () => new BreakReminderSignals(migraine, fullscreen, TimeSpan.FromSeconds(idleSeconds)));

        Assert.Equal(BreakReminderTickOutcome.Suppressed, tick.Outcome);
        Assert.Equal(expected, tick.Decision);
    }

    [Fact]
    public void TrySnooze_is_ignored_when_disabled_or_duration_not_positive()
    {
        var state = new BreakReminderState();

        Assert.False(state.TrySnooze(false, Now, TimeSpan.FromMinutes(5)));
        Assert.False(state.TrySnooze(true, Now, TimeSpan.Zero));
        Assert.False(state.TrySnooze(true, Now, TimeSpan.FromMinutes(-1)));
        Assert.Null(state.SnoozedUntilUtc);
    }

    [Fact]
    public void Resume_reports_whether_anything_was_cleared()
    {
        var state = new BreakReminderState();
        Assert.False(state.Resume());

        state.TrySnooze(true, Now, TimeSpan.FromHours(1));

        Assert.True(state.Resume());
        Assert.False(state.IsSnoozed(Now));
    }

    [Fact]
    public void Reset_drops_a_stale_snooze()
    {
        var state = new BreakReminderState();
        state.TrySnooze(true, Now, TimeSpan.FromHours(1));

        state.Reset();

        Assert.False(state.IsSnoozed(Now));
    }
}
