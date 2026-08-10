using System.Windows;
using System.Windows.Threading;
using MonitorWellness.Core;

namespace MonitorWellness;

/// <summary>Time-limited consent dialog for a reversible physical monitor brightness test.</summary>
public partial class HardwareBrightnessTestDialog : Window
{
    private const int TestDurationSeconds = 10;
    private readonly DispatcherTimer _timer;
    private int _secondsRemaining = TestDurationSeconds;

    public bool Confirmed { get; private set; }

    public HardwareBrightnessTestDialog()
    {
        InitializeComponent();
        ThemeDetector.ApplyDarkThemeIfNeeded(this);
        ThemeDetector.EnableLiveThemeUpdates(this);
        Icon = AppIconSource.Default;
        UpdateCountdownText();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            _secondsRemaining--;
            if (_secondsRemaining <= 0)
                Close();
            else
                UpdateCountdownText();
        };
        Loaded += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    private void UpdateCountdownText() => CountdownText.Text = $"Restoring automatically in {_secondsRemaining} seconds.";

    private void RestoreButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ConfirmedButton_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }
}
