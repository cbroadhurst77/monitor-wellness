using System.Windows;
using MonitorWellness.Core;

namespace MonitorWellness;

/// <summary>Read-only, local explanation of monitor capabilities and chosen safety fallbacks.</summary>
public partial class DisplayCapabilityWindow : Window
{
    public DisplayCapabilityWindow(AppSettings settings)
    {
        InitializeComponent();
        ThemeDetector.ApplyDarkThemeIfNeeded(this);
        ThemeDetector.EnableLiveThemeUpdates(this);
        Icon = AppIconSource.Default;
        ReportText.Text = DisplayCapabilityReporter.Create(settings).ToPlainText();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
