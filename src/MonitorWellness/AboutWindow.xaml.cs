using System.Windows;
using MonitorWellness.Core;

namespace MonitorWellness;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        ThemeDetector.ApplyDarkThemeIfNeeded(this);
        ThemeDetector.EnableLiveThemeUpdates(this);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
