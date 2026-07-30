using System.Windows;
using System.Windows.Threading;
using MonitorWellness.Core;
using Button = System.Windows.Controls.Button;

namespace MonitorWellness;

/// <summary>
/// Tiny, non-modal, auto-dismissing prompt shown after a Migraine Mode deactivation when
/// AppSettings.PromptForMigraineRating is on (see MigraineModeController.RatingRequested).
/// Deliberately not a MessageBox: this needs 1-5 plus a Skip, and shouldn't block anything —
/// the user may be mid-recovery from a migraine and this should be easy to ignore entirely.
/// Auto-closes (treated as Skip) after AutoDismiss if nobody answers, so it can never pile up
/// unattended prompts across multiple activations.
/// </summary>
public partial class MigraineRatingWindow : Window
{
    private static readonly TimeSpan AutoDismiss = TimeSpan.FromSeconds(20);

    private readonly Action<int?> _onAnswered;
    private readonly DispatcherTimer _autoCloseTimer;
    private bool _answered;

    public MigraineRatingWindow(Action<int?> onAnswered)
    {
        InitializeComponent();
        ThemeDetector.ApplyDarkThemeIfNeeded(this);
        _onAnswered = onAnswered;

        _autoCloseTimer = new DispatcherTimer { Interval = AutoDismiss };
        _autoCloseTimer.Tick += (_, _) => Answer(null);
        _autoCloseTimer.Start();
    }

    private void RatingButton_Click(object sender, RoutedEventArgs e)
    {
        int rating = int.Parse((string)((Button)sender).Content);
        Answer(rating);
    }

    private void Skip_Click(object sender, RoutedEventArgs e) => Answer(null);

    private void Answer(int? rating)
    {
        if (_answered) return; // guards against the auto-close timer firing after a button click already closed this
        _answered = true;
        _autoCloseTimer.Stop();
        _onAnswered(rating);
        Close();
    }
}
