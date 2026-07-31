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
///
/// "Don't ask again" lives on this window itself (not only in Settings) so someone who opts
/// out mid-flow doesn't have to go hunt for the checkbox afterward — see UX review's P1 finding
/// on this popup.
/// </summary>
public partial class MigraineRatingWindow : Window
{
    private static readonly TimeSpan AutoDismiss = TimeSpan.FromSeconds(20);

    private readonly Action<int?, bool> _onAnswered;
    private readonly DispatcherTimer _autoCloseTimer;
    private bool _answered;

    public MigraineRatingWindow(Action<int?, bool> onAnswered)
    {
        InitializeComponent();
        ThemeDetector.ApplyDarkThemeIfNeeded(this);
        Icon = AppIconSource.Default;
        _onAnswered = onAnswered;

        _autoCloseTimer = new DispatcherTimer { Interval = AutoDismiss };
        _autoCloseTimer.Tick += (_, _) => Answer(null, dontAskAgain: false);
        _autoCloseTimer.Start();
    }

    private void RatingButton_Click(object sender, RoutedEventArgs e)
    {
        int rating = int.Parse((string)((Button)sender).Content);
        Answer(rating, dontAskAgain: false);
    }

    private void Skip_Click(object sender, RoutedEventArgs e) => Answer(null, dontAskAgain: false);

    private void DontAskAgain_Click(object sender, RoutedEventArgs e) => Answer(null, dontAskAgain: true);

    private void Answer(int? rating, bool dontAskAgain)
    {
        if (_answered) return; // guards against the auto-close timer firing after a button click already closed this
        _answered = true;
        _autoCloseTimer.Stop();
        _onAnswered(rating, dontAskAgain);
        Close();
    }
}
