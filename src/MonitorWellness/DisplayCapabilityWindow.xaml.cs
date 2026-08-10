using System.Windows;
using MonitorWellness.Core;

namespace MonitorWellness;

/// <summary>Read-only, local explanation of monitor capabilities and chosen safety fallbacks.</summary>
public partial class DisplayCapabilityWindow : Window
{
    private readonly AppSettings _settings;

    public DisplayCapabilityWindow(AppSettings settings)
    {
        InitializeComponent();
        ThemeDetector.ApplyDarkThemeIfNeeded(this);
        ThemeDetector.EnableLiveThemeUpdates(this);
        Icon = AppIconSource.Default;
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        RefreshReport();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshReport();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(ReportText.Text);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            System.Windows.MessageBox.Show(this, "Windows couldn't access the clipboard. Try Copy report again.", "Monitor Wellness", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void RefreshReport() => ReportText.Text = DisplayCapabilityReporter.Create(_settings).ToPlainText();
}
