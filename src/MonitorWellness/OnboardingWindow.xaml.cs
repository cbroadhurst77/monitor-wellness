using System.Windows;
using MonitorWellness.Core;

namespace MonitorWellness;

/// <summary>
/// Shown once, on the very first launch (AppSettings.HasCompletedOnboarding starts false).
/// Either button just closes the window — App marks onboarding complete and saves on Closed,
/// regardless of which path was taken, so there's no way to end up stuck re-showing this.
/// </summary>
public partial class OnboardingWindow : Window
{
    private readonly Action _openSettings;

    public OnboardingWindow(Action openSettings)
    {
        InitializeComponent();
        ThemeDetector.ApplyDarkThemeIfNeeded(this);
        _openSettings = openSettings;
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        _openSettings();
        Close();
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
