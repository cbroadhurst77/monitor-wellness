using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MonitorWellness.Core;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace MonitorWellness;

/// <summary>Identify monitors and the hardware-brightness test. (part of the settings window; see SettingsWindow.xaml.cs.)</summary>
public partial class SettingsWindow
{
    private void IdentifyButton_Click(object sender, RoutedEventArgs e) => _overlay.IdentifyMonitors(TimeSpan.FromSeconds(6));

    private bool _hardwareTestRunning;

    /// <summary>
    /// The user-requested reversible test of a monitor's physical backlight. Every call that talks to the
    /// monitor (opening it, dimming it, giving the brightness back) runs on a background thread: they are
    /// slow round trips that can hang on some monitors and docks, and must not freeze this window.
    /// </summary>
    private async Task TestHardwareBrightnessAsync(string deviceName, MonitorInfo? monitor, CheckBox hardwareBrightnessBox)
    {
        if (_hardwareTestRunning)
            return;

        if (System.Windows.MessageBox.Show(
                this,
                $"Monitor Wellness will temporarily reduce {ShortDeviceName(deviceName)}'s physical backlight by a small amount, then restore the exact previous value after at most 10 seconds. This does not enable hardware dimming for the schedule. Continue?",
                "Test hardware brightness",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information,
                MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        _hardwareTestRunning = true;
        DdcCiBrightnessTestSession? session = null;
        try
        {
            var (opened, openedSession, openError) = await Task.Run(() =>
            {
                bool ok = DdcCiBrightnessProbe.TryOpenTestSession(deviceName, out DdcCiBrightnessTestSession? s, out string e);
                return (ok && s is not null, s, e);
            });
            session = openedSession;
            if (!opened)
            {
                System.Windows.MessageBox.Show(this, $"Hardware brightness test isn't available: {openError}", "Monitor Wellness", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var (dimmed, dimError) = await Task.Run(() =>
            {
                bool ok = session!.TryDimForTest(out string e);
                return (ok, e);
            });
            if (!dimmed)
            {
                System.Windows.MessageBox.Show(this, $"Hardware brightness test couldn't start: {dimError}", "Monitor Wellness", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new HardwareBrightnessTestDialog { Owner = this };
            dialog.ShowDialog();

            // Give the brightness back before saying anything about the result.
            await Task.Run(() => session!.Dispose());
            session = null;

            if (dialog.Confirmed)
            {
                if (monitor is null || !HardwareBrightnessSafety.MarkVerified(_settings, monitor))
                {
                    System.Windows.MessageBox.Show(this, "The test restored correctly, but Windows did not provide a stable identity for this monitor. For safety, scheduled hardware brightness remains unavailable and the overlay will be used.", "Monitor Wellness", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                hardwareBrightnessBox.IsEnabled = true;
                hardwareBrightnessBox.IsChecked = true;
                hardwareBrightnessBox.ToolTip = "This monitor passed its reversible hardware-brightness test. Click Save to keep the approval.";
                System.Windows.MessageBox.Show(this, "Hardware brightness responded correctly. HW is now selected for this physically identified monitor; click Save to opt in to scheduled physical brightness.", "Monitor Wellness", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        finally
        {
            // Whatever happened, the monitor must not be left dimmed.
            if (session is not null)
                await Task.Run(() => session.Dispose());
            _hardwareTestRunning = false;
        }
    }
}
