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
        _ = RefreshReportAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshReportAsync();

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

    private bool _refreshing;

    /// <summary>Building the report probes every monitor over DDC/CI (slow), so it runs off the UI thread and the window shows a placeholder meanwhile.</summary>
    private async Task RefreshReportAsync()
    {
        if (_refreshing)
            return;

        _refreshing = true;
        ReportText.Text = "Checking your displays…";
        try
        {
            ReportText.Text = await Task.Run(() => DisplayCapabilityReporter.Create(_settings).ToPlainText());
        }
        catch (Exception ex)
        {
            DebugLog.Write($"Display capability report failed: {ex}");
            ReportText.Text = "The capability report couldn't be built. Try Refresh, or export a diagnostic bundle from the tray menu.";
        }
        finally
        {
            _refreshing = false;
        }
    }
}
