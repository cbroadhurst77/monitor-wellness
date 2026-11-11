using System.Windows;
using System.Windows.Controls;
using MonitorWellness.Core;
using Button = System.Windows.Controls.Button;

namespace MonitorWellness.Tests;

/// <summary>
/// The break prompt is the only way the reminder reaches the person, and if the window fails to load
/// the app silently falls back to a balloon, so a markup or resource mistake would go unnoticed.
/// This builds the real window (never shown, so nothing appears on screen) and presses its buttons.
/// </summary>
public class BreakPromptWindowTests
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

    private static Button FindButton(DependencyObject root, string automationName)
    {
        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is Button button && System.Windows.Automation.AutomationProperties.GetName(button) == automationName)
                return button;
            if (child is DependencyObject nested)
            {
                try { return FindButton(nested, automationName); }
                catch (InvalidOperationException) { /* not under this branch */ }
            }
        }
        throw new InvalidOperationException($"No button named '{automationName}'.");
    }

    private static void Press(Button button)
        => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

    [Theory]
    [InlineData("Start the 20-second break", BreakPromptChoice.StartBreak)]
    [InlineData("Remind me again in 5 minutes", BreakPromptChoice.Snooze)]
    [InlineData("Skip this reminder", BreakPromptChoice.Skip)]
    public void Each_button_reports_its_choice_exactly_once(string buttonName, BreakPromptChoice expected)
    {
        OnStaThread(() =>
        {
            var choices = new List<BreakPromptChoice>();
            var window = new BreakPromptWindow(choices.Add);

            Press(FindButton(window, buttonName));
            Press(FindButton(window, "Skip this reminder")); // a second answer must be ignored

            Assert.Equal(new[] { expected }, choices);
        });
    }

    [Fact]
    public void The_window_is_a_small_non_activating_topmost_window_that_stays_out_of_the_taskbar()
    {
        OnStaThread(() =>
        {
            var window = new BreakPromptWindow(_ => { });

            Assert.True(window.Topmost);
            Assert.False(window.ShowActivated);
            Assert.False(window.ShowInTaskbar);
            Assert.Equal(WindowStartupLocation.Manual, window.WindowStartupLocation);
            Assert.False(string.IsNullOrWhiteSpace(window.Title));
            window.Close();
        });
    }
}

public class HardwareBrightnessShutdownTests
{
    private sealed class Session : IHardwareBrightnessSession
    {
        public bool Disposed { get; private set; }
        public bool TryApplyNormalizedBrightness(double normalizedBrightness, out string error) { error = ""; return true; }
        public bool TryRefreshHandles(out string error) { error = ""; return true; }
        public void ForgetLastApplied() { }
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void Disposing_at_exit_gives_monitors_back_ignores_later_requests_and_can_be_repeated()
    {
        var session = new Session();
        var manager = new HardwareBrightnessControllerManager(
            (string _, out IHardwareBrightnessSession? s, out string e) => { s = session; e = ""; return true; },
            action => action(), TimeSpan.FromSeconds(30));
        manager.RequestApply(new List<string> { "D1" }, new Dictionary<string, double> { ["D1"] = 0.5 });
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (manager.AppliedDevices.Count == 0 && timer.Elapsed < TimeSpan.FromSeconds(10))
            Thread.Sleep(5);

        manager.Dispose();
        manager.Dispose();
        manager.RequestApply(new List<string> { "D1" }, new Dictionary<string, double> { ["D1"] = 0.4 });

        Assert.True(session.Disposed);
        Assert.Empty(manager.AppliedDevices);
    }

    [Fact]
    public void Disposing_a_manager_that_was_never_used_is_instant()
    {
        var manager = new HardwareBrightnessControllerManager();
        var timer = System.Diagnostics.Stopwatch.StartNew();

        manager.Dispose();

        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(1));
    }
}
