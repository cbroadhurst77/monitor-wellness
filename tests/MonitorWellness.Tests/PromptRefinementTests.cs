#pragma warning disable CA1861 // small literal arrays in tests are clearer than shared static fields
using System.Windows;
using MonitorWellness.Core;
using Button = System.Windows.Controls.Button;
using Size = System.Windows.Size;

namespace MonitorWellness.Tests;

public class PromptRefinementServiceTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class Rig
    {
        public Clock Clock { get; } = new();
        public AppSettings Settings { get; } = new() { BreakReminderEnabled = true, BreakReminderIntervalMinutes = 10, BreakHistoryEnabled = true };
        public Action<BreakPromptChoice>? EyeAnswer { get; private set; }
        public Action<BreakPromptChoice>? StandUpAnswer { get; private set; }
        public int EyePrompts { get; private set; }
        public int StandUpPrompts { get; private set; }
        public int SettingsOpened { get; private set; }
        public List<(string Message, Action? OnClick)> Balloons { get; } = new();
        public List<string> Types { get; } = new();
        public BreakReminderService Service { get; }

        public Rig()
        {
            Service = new BreakReminderService(
                Settings, () => false, (_, message, _, onClick) => Balloons.Add((message, onClick)),
                showPrompt: onChoice => { EyePrompts++; EyeAnswer = onChoice; return true; },
                readIdle: () => TimeSpan.FromSeconds(1), isFullscreen: () => false, clock: Clock, isInCall: () => false,
                showStandUpPrompt: onChoice => { StandUpPrompts++; StandUpAnswer = onChoice; return true; },
                recordHistory: evt => Types.Add(evt.EventType),
                openSettings: () => SettingsOpened++);
        }

        public void StepMinutes(double minutes)
        {
            for (int i = 0; i < (int)Math.Round(minutes * 4); i++)
            {
                Clock.Now += BreakReminderPolicy.SampleInterval;
                Service.SampleOnce();
            }
        }

        /// <summary>Lets the next eye reminder come and answers it.</summary>
        public void Answer(BreakPromptChoice choice)
        {
            int before = EyePrompts;
            StepMinutes(11);
            Assert.Equal(before + 1, EyePrompts);
            EyeAnswer!(choice);
        }
    }

    [Fact]
    public void I_already_took_one_counts_as_a_break_taken_and_restarts_the_count()
    {
        var rig = new Rig();
        rig.StepMinutes(11);

        rig.EyeAnswer!(BreakPromptChoice.AlreadyTookBreak);

        Assert.Contains(BreakHistory.BreakTaken, rig.Types);
        Assert.Contains("Next break in about", rig.Service.DescribeStatus());
        rig.StepMinutes(6);
        Assert.Equal(1, rig.EyePrompts);           // the next one is a full interval away
        Assert.DoesNotContain(BreakHistory.ReminderSkipped, rig.Types);
    }

    [Fact]
    public void I_already_moved_restarts_both_counts_and_is_a_movement_break()
    {
        var rig = new Rig();
        rig.Settings.BreakReminderIntervalMinutes = 120;
        rig.Settings.StandUpReminderEnabled = true;
        rig.Settings.StandUpIntervalMinutes = 30;
        rig.StepMinutes(31);
        Assert.Equal(1, rig.StandUpPrompts);

        rig.StandUpAnswer!(BreakPromptChoice.AlreadyTookBreak);

        Assert.Contains(BreakHistory.MovementBreakTaken, rig.Types);
        rig.StepMinutes(20);
        Assert.Equal(1, rig.StandUpPrompts);
    }

    [Fact]
    public void Three_skipped_reminders_in_a_row_offer_to_change_how_often_and_the_offer_opens_settings()
    {
        var rig = new Rig();

        rig.Answer(BreakPromptChoice.Skip);
        rig.Answer(BreakPromptChoice.Skip);
        Assert.Empty(rig.Balloons);
        rig.Answer(BreakPromptChoice.Skip);

        var (message, onClick) = Assert.Single(rig.Balloons);
        Assert.Contains("skipped the last 3 break reminders", message);
        Assert.NotNull(onClick);
        onClick!();
        Assert.Equal(1, rig.SettingsOpened);
    }

    [Fact]
    public void An_ignored_reminder_counts_as_skipped_too()
    {
        var rig = new Rig();

        rig.Answer(BreakPromptChoice.TimedOut);
        rig.Answer(BreakPromptChoice.Skip);
        rig.Answer(BreakPromptChoice.TimedOut);

        Assert.Single(rig.Balloons);
    }

    [Theory]
    [InlineData(BreakPromptChoice.StartBreak)]
    [InlineData(BreakPromptChoice.Snooze)]
    [InlineData(BreakPromptChoice.AlreadyTookBreak)]
    public void Engaging_with_a_reminder_ends_the_streak(BreakPromptChoice engaged)
    {
        var rig = new Rig();

        rig.Answer(BreakPromptChoice.Skip);
        rig.Answer(BreakPromptChoice.Skip);
        rig.Answer(engaged);
        rig.Answer(BreakPromptChoice.Skip);
        rig.Answer(BreakPromptChoice.Skip);

        Assert.Empty(rig.Balloons);
    }

    [Fact]
    public void A_break_taken_by_itself_ends_the_streak_too()
    {
        var rig = new Rig();
        rig.Answer(BreakPromptChoice.Skip);
        rig.Answer(BreakPromptChoice.Skip);

        rig.Service.NotifyBreakTaken(); // the 20-second timer was used from the tray, say
        rig.Answer(BreakPromptChoice.Skip);

        Assert.Empty(rig.Balloons);
    }

    [Fact]
    public void The_offer_is_made_once_a_day_not_after_every_further_skip()
    {
        var rig = new Rig();
        for (int i = 0; i < 9; i++)
            rig.Answer(BreakPromptChoice.Skip);

        Assert.Single(rig.Balloons);

        rig.Clock.Now = rig.Clock.Now.AddDays(1);
        for (int i = 0; i < 3; i++)
            rig.Answer(BreakPromptChoice.Skip);

        Assert.Equal(2, rig.Balloons.Count);
    }

    [Fact]
    public void Skipped_stand_up_reminders_are_counted_apart_and_named_as_such()
    {
        var rig = new Rig();
        rig.Settings.BreakReminderIntervalMinutes = 120;
        rig.Settings.StandUpReminderEnabled = true;
        rig.Settings.StandUpIntervalMinutes = 30;

        for (int i = 0; i < 3; i++)
        {
            rig.StepMinutes(31);
            rig.StandUpAnswer!(BreakPromptChoice.Skip);
        }

        var (message, _) = Assert.Single(rig.Balloons);
        Assert.Contains("stand-up reminders", message);
    }
}

public class PromptRefinementWindowTests
{
    private static void OnStaThread(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw new InvalidOperationException("The window test failed on its UI thread.", failure);
    }

    [Theory]
    [InlineData(BreakPromptKind.Eye, 20)]
    [InlineData(BreakPromptKind.Eye, 120)]
    [InlineData(BreakPromptKind.StandUp, 20)]
    public void Every_button_is_wide_enough_for_its_text_and_the_window_is_a_sensible_size(BreakPromptKind kind, int seconds)
    {
        OnStaThread(() =>
        {
            var window = new BreakPromptWindow(_ => { }, focusSeconds: seconds, kind: kind);
            // An unshown window does not lay its children out, so work from the markup's own numbers:
            // 18 px margin each side, two equal columns, 4 px between a button and its neighbour.
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(window.Width, double.PositiveInfinity));
            double availablePerButton = (window.Width - 36) / 2 - 4;

            var buttons = new List<Button>();
            void Collect(DependencyObject node)
            {
                foreach (object child in LogicalTreeHelper.GetChildren(node))
                {
                    if (child is Button button) buttons.Add(button);
                    if (child is DependencyObject next) Collect(next);
                }
            }
            Collect(window);

            Assert.Equal(4, buttons.Count);
            foreach (Button button in buttons)
            {
                button.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Assert.True(button.DesiredSize.Width <= availablePerButton,
                    $"'{button.Content}' needs {button.DesiredSize.Width:0} px but only {availablePerButton:0} are available");
            }
            Assert.InRange(content.DesiredSize.Height, 100, 260);
        });
    }

    [Theory]
    [InlineData(BreakPromptKind.Eye, "I already took one", "I already took a break")]
    [InlineData(BreakPromptKind.StandUp, "I already moved", "I already took a movement break")]
    public void The_prompt_has_an_I_already_took_one_button_that_reports_it(BreakPromptKind kind, string text, string automationName)
    {
        OnStaThread(() =>
        {
            var choices = new List<BreakPromptChoice>();
            var window = new BreakPromptWindow(choices.Add, kind: kind);

            var button = (Button)window.FindName("AlreadyButton");
            Assert.Equal(text, button.Content);
            Assert.Equal(automationName, System.Windows.Automation.AutomationProperties.GetName(button));
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

            Assert.Equal(new[] { BreakPromptChoice.AlreadyTookBreak }, choices);
        });
    }
}
