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
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
