using System.Windows;
using MonitorWellness.Core;

namespace MonitorWellness;

public partial class TroubleshootingWindow : Window
{
    public TroubleshootingWindow()
    {
        InitializeComponent();
        ThemeDetector.ApplyDarkThemeIfNeeded(this);
        ThemeDetector.EnableLiveThemeUpdates(this);
        Icon = AppIconSource.Default;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
