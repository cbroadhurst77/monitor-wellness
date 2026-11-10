using System.Windows;
using System.Windows.Threading;
using MonitorWellness.Core;

namespace MonitorWellness;

/// <summary>
/// The break reminder itself: a small window in the corner of the screen with Start / In 5 minutes /
/// Skip. Unlike a tray balloon (which Windows hides under Focus Assist, replaces with the next
/// balloon, and never reports back on) it always appears, stays until answered, and tells the app
/// what was chosen. It does not take focus from what the person is typing, never dims or blocks
/// anything, and goes away on its own after a minute.
/// </summary>
public partial class BreakPromptWindow : Window
{
    private static readonly TimeSpan AutoDismissAfter = TimeSpan.FromSeconds(60);
    private readonly Action<BreakPromptChoice> _onChoice;
    private readonly DispatcherTimer _autoDismiss;
    private bool _answered;

    public BreakPromptWindow(Action<BreakPromptChoice> onChoice)
    {
        _onChoice = onChoice;
        InitializeComponent();
        ThemeDetector.ApplyDarkThemeIfNeeded(this);
        Icon = AppIconSource.Default;

        Loaded += (_, _) =>
        {
            Rect workArea = SystemParameters.WorkArea;
            Left = workArea.Right - ActualWidth - 16;
            Top = workArea.Bottom - ActualHeight - 16;
        };

        _autoDismiss = new DispatcherTimer { Interval = AutoDismissAfter };
        _autoDismiss.Tick += (_, _) => Answer(BreakPromptChoice.TimedOut);
        Closed += (_, _) =>
        {
            _autoDismiss.Stop();
            Answer(BreakPromptChoice.Skip, close: false); // closed with the window's own close button
        };
        _autoDismiss.Start();
    }

    private void Start_Click(object sender, RoutedEventArgs e) => Answer(BreakPromptChoice.StartBreak);

    private void Snooze_Click(object sender, RoutedEventArgs e) => Answer(BreakPromptChoice.Snooze);

    private void Skip_Click(object sender, RoutedEventArgs e) => Answer(BreakPromptChoice.Skip);

    private void Answer(BreakPromptChoice choice, bool close = true)
    {
        if (_answered)
            return;

        _answered = true;
        _autoDismiss.Stop();
        try
        {
            _onChoice(choice);
        }
        finally
        {
            if (close)
                Close();
        }
    }
}
