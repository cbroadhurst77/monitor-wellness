using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MonitorWellness.Core;

namespace MonitorWellness;

/// <summary>
/// The break reminder itself: a small window in the bottom-right corner of the monitor the person is
/// working on, with Start / In 5 minutes / Skip. Unlike a tray balloon (which Windows hides under Focus
/// Assist, replaces with the next balloon, and never reports back on) it always appears, stays until
/// answered, and tells the app what was chosen. It does not take focus from what the person is typing,
/// never dims or blocks anything, stays out of screen sharing by default, and goes away on its own
/// after a minute.
/// </summary>
public partial class BreakPromptWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    private const uint WdaExcludeFromCapture = 0x00000011; // Windows 10 version 2004 and later
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const double MarginDips = 16;

    private static readonly TimeSpan AutoDismissAfter = TimeSpan.FromSeconds(60);
    private readonly Action<BreakPromptChoice> _onChoice;
    private readonly DispatcherTimer _autoDismiss;
    private readonly Screen _screen;
    private bool _answered;

    /// <param name="excludeFromCapture">Hide the prompt from screenshots, recordings and screen sharing (the person still sees it).</param>
    /// <param name="focusSeconds">Length of the break the Start button begins; shown in its text.</param>
    /// <param name="screen">The monitor to appear on; by default the one with the window the person is working in.</param>
    public BreakPromptWindow(Action<BreakPromptChoice> onChoice, bool excludeFromCapture = true, int focusSeconds = 20, Screen? screen = null)
    {
        _onChoice = onChoice;
        _screen = screen ?? PromptPlacement.ActiveScreen();
        InitializeComponent();
        ThemeDetector.ApplyDarkThemeIfNeeded(this);
        Icon = AppIconSource.Default;

        focusSeconds = Math.Clamp(focusSeconds, BreakReminderStyles.MinimumFocusSeconds, BreakReminderStyles.MaximumFocusSeconds);
        MessageText.Text = $"Look at something about 20 feet away for {focusSeconds} seconds.";
        StartButton.Content = $"Start {focusSeconds} seconds";
        System.Windows.Automation.AutomationProperties.SetName(StartButton, $"Start the {focusSeconds}-second break");

        SourceInitialized += (_, _) =>
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
                return;

            if (excludeFromCapture)
                SetWindowDisplayAffinity(handle, WdaExcludeFromCapture); // best effort: older Windows simply shows it

            // Move onto the chosen monitor first, so the window is laid out at that monitor's scale;
            // the exact corner is worked out once its size is known.
            Place(handle, _screen.WorkingArea.Location);
        };
        // Not in Loaded: the window sizes itself to its content (SizeToContent) after that and WPF puts it
        // back at its own idea of its position. After the first render the size is final, and one more pass
        // when the dispatcher is idle wins over anything WPF still does with the position.
        ContentRendered += (_, _) =>
        {
            PlaceInCorner();
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(PlaceInCorner));
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

    private static void Place(IntPtr handle, System.Drawing.Point topLeft)
        => SetWindowPos(handle, IntPtr.Zero, topLeft.X, topLeft.Y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);

    private void PlaceInCorner()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return;

        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        var size = new System.Drawing.Size(
            (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY));
        Place(handle, PromptPlacement.BottomRight(_screen.WorkingArea, size, (int)Math.Round(MarginDips * dpi.DpiScaleX)));
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
