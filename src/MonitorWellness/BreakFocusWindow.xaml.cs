using System.Windows;
using System.Windows.Threading;
using MonitorWellness.Core;

namespace MonitorWellness;

/// <summary>
/// A user-initiated, non-modal visual aid for a short distance-focus break (20 seconds by default; the
/// length is a setting). It never dims, captures input, or prevents dismissal, so it cannot create a
/// recovery hazard.
/// </summary>
public partial class BreakFocusWindow : Window
{
    private readonly DispatcherTimer _timer;
    private readonly DateTime _endsAtUtc;

    public BreakFocusWindow(int seconds = 20)
    {
        InitializeComponent();
        ThemeDetector.ApplyDarkThemeIfNeeded(this);
        Icon = AppIconSource.Default;

        seconds = Math.Clamp(seconds, BreakReminderStyles.MinimumFocusSeconds, BreakReminderStyles.MaximumFocusSeconds);
        Title = $"{seconds}-second focus break";
        HeadingText.Text = Title;
        System.Windows.Automation.AutomationProperties.SetName(HeadingText, Title);

        _endsAtUtc = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateCountdown();
        Closed += (_, _) => _timer.Stop();
        UpdateCountdown();
        _timer.Start();
    }

    /// <summary>True when the countdown ran (nearly) to the end, so it counts as a real break.</summary>
    public bool CountsAsBreak => DateTime.UtcNow >= _endsAtUtc - TimeSpan.FromSeconds(5);

    private void Done_Click(object sender, RoutedEventArgs e) => Close();

    private void UpdateCountdown()
    {
        int secondsRemaining = Math.Max(0, (int)Math.Ceiling((_endsAtUtc - DateTime.UtcNow).TotalSeconds));
        CountdownText.Text = $"{secondsRemaining} seconds";
        if (secondsRemaining == 0)
            Close();
    }
}
