using System.Windows;
using System.Windows.Threading;
using MonitorWellness.Core;

namespace MonitorWellness;

/// <summary>
/// A user-initiated, non-modal visual aid for a 20-second distance-focus break. It never dims,
/// captures input, or prevents dismissal, so it cannot create a recovery hazard.
/// </summary>
public partial class BreakFocusWindow : Window
{
    private static readonly TimeSpan FocusDuration = TimeSpan.FromSeconds(20);
    private readonly DispatcherTimer _timer;
    private readonly DateTime _endsAtUtc;

    public BreakFocusWindow()
    {
        InitializeComponent();
        ThemeDetector.ApplyDarkThemeIfNeeded(this);
        Icon = AppIconSource.Default;

        _endsAtUtc = DateTime.UtcNow + FocusDuration;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateCountdown();
        Closed += (_, _) => _timer.Stop();
        UpdateCountdown();
        _timer.Start();
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();

    private void UpdateCountdown()
    {
        int secondsRemaining = Math.Max(0, (int)Math.Ceiling((_endsAtUtc - DateTime.UtcNow).TotalSeconds));
        CountdownText.Text = $"{secondsRemaining} seconds";
        if (secondsRemaining == 0)
            Close();
    }
}
