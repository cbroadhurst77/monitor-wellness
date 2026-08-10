using System.Windows;
using MonitorWellness.Core;

namespace MonitorWellness;

/// <summary>
/// Explicit consent before the settings preview intentionally blacks out all adjusted displays.
/// It is topmost so it remains visible above the app's click-through dimming overlays.
/// </summary>
public partial class BlackoutPreviewDialog : Window
{
    public bool ShouldPreviewBlackout { get; private set; }

    public BlackoutPreviewDialog()
    {
        InitializeComponent();
        ThemeDetector.ApplyDarkThemeIfNeeded(this);
        ThemeDetector.EnableLiveThemeUpdates(this);
        Icon = AppIconSource.Default;
        Loaded += (_, _) => RevertButton.Focus();
    }

    private void RevertButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void KeepButton_Click(object sender, RoutedEventArgs e)
    {
        ShouldPreviewBlackout = true;
        DialogResult = true;
    }
}
