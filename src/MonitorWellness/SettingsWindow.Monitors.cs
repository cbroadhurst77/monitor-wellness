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

/// <summary>Per-monitor rows (exclude, colour-exclude, dim multiplier, Kelvin offset, hardware brightness). (part of the settings window; see SettingsWindow.xaml.cs.)</summary>
public partial class SettingsWindow
{
    private void BuildMonitorRows()
    {
        _excludeBoxes.Clear();
        _colorExcludeBoxes.Clear();
        _multiplierBoxes.Clear();
        _kelvinOffsetBoxes.Clear();
        _hardwareBrightnessBoxes.Clear();
        RefreshHardwareBrightnessReadiness();

        var activeMonitorsByDeviceName = MonitorEnumerator.GetActiveMonitors()
            .ToDictionary(monitor => monitor.DeviceName, StringComparer.OrdinalIgnoreCase);
        var rows = new List<UIElement>();
        foreach (var deviceName in _overlay.DeviceNames.OrderBy(d => d))
        {
            activeMonitorsByDeviceName.TryGetValue(deviceName, out MonitorInfo? monitor);
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock { Text = ShortDeviceName(deviceName), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 0);

            var excludeBox = new CheckBox
            {
                Content = "Exclude",
                IsChecked = _settings.ExcludedMonitors.Contains(deviceName),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "Skip this monitor entirely — no color or brightness adjustment.",
            };
            System.Windows.Automation.AutomationProperties.SetName(excludeBox, $"Exclude {ShortDeviceName(deviceName)}");
            Grid.SetColumn(excludeBox, 1);
            _excludeBoxes[deviceName] = excludeBox;

            var colorExcludeBox = new CheckBox
            {
                Content = "Color-accurate",
                IsChecked = _settings.ColorExcludedMonitors.Contains(deviceName),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "Keep this monitor's native color (e.g. a photo/video reference display) — it still dims with the schedule.",
            };
            System.Windows.Automation.AutomationProperties.SetName(colorExcludeBox, $"Color-accurate {ShortDeviceName(deviceName)}");
            Grid.SetColumn(colorExcludeBox, 2);
            _colorExcludeBoxes[deviceName] = colorExcludeBox;

            double multiplier = _settings.MonitorDimMultiplier.TryGetValue(deviceName, out var m) ? m : 1.0;
            var multiplierBox = new TextBox { Text = multiplier.ToString(CultureInfo.InvariantCulture), ToolTip = "Dim multiplier (1.0 = follow the global schedule exactly)." };
            multiplierBox.TextChanged += (_, _) =>
            {
                if (_loaded) QueuePreview("day");
            };
            System.Windows.Automation.AutomationProperties.SetName(multiplierBox, $"Dim multiplier for {ShortDeviceName(deviceName)}");
            Grid.SetColumn(multiplierBox, 3);
            _multiplierBoxes[deviceName] = multiplierBox;

            int kelvinOffset = _settings.MonitorKelvinOffset.TryGetValue(deviceName, out var k) ? k : 0;
            var kelvinOffsetBox = new TextBox { Text = kelvinOffset.ToString(CultureInfo.InvariantCulture), Margin = new Thickness(4, 0, 0, 0), ToolTip = "Kelvin offset for this monitor only — e.g. -300 if it reads warmer than the others at the same setting." };
            kelvinOffsetBox.TextChanged += (_, _) =>
            {
                if (_loaded) QueuePreview("day");
            };
            System.Windows.Automation.AutomationProperties.SetName(kelvinOffsetBox, $"Kelvin offset for {ShortDeviceName(deviceName)}");
            Grid.SetColumn(kelvinOffsetBox, 4);
            _kelvinOffsetBoxes[deviceName] = kelvinOffsetBox;

            bool hasStableHardwareIdentity = monitor is not null && HardwareBrightnessSafety.GetKey(monitor) is not null;
            string compatibilityReason = "";
            bool compatibilityFallback = monitor is not null
                && _settings.PreferOverlayOnlyOnCompatibilityDisplays
                && DisplayCompatibilityAdvisor.TryGetOverlayOnlyReason(monitor, out compatibilityReason);
            string quarantineReason = "";
            bool hardwareQuarantined = monitor is not null && HardwareBrightnessSafety.IsQuarantined(_settings, monitor, out quarantineReason);
            bool hardwareEnabled = monitor is not null && HardwareBrightnessSafety.IsApproved(_settings, monitor);
            var hardwareBrightnessBox = new CheckBox
            {
                Content = "HW",
                IsChecked = hardwareEnabled,
                IsEnabled = hardwareEnabled && !hardwareQuarantined && !compatibilityFallback,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
                ToolTip = compatibilityFallback
                    ? $"{compatibilityReason} Compatibility mode keeps this display overlay-only."
                    : hardwareQuarantined
                    ? quarantineReason
                    : hasStableHardwareIdentity
                        ? "Use physical DDC/CI brightness for this monitor. It becomes available only after this session's safe test succeeds."
                        : "This monitor does not expose a stable hardware identifier, so automatic DDC/CI brightness stays disabled for safety.",
            };
            System.Windows.Automation.AutomationProperties.SetName(hardwareBrightnessBox, $"Use hardware brightness for {ShortDeviceName(deviceName)}");
            Grid.SetColumn(hardwareBrightnessBox, 5);
            _hardwareBrightnessBoxes[deviceName] = hardwareBrightnessBox;

            var testHardwareBrightnessButton = new System.Windows.Controls.Button
            {
                Content = "Test HW",
                IsEnabled = !compatibilityFallback,
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(6, 0, 0, 0),
                ToolTip = compatibilityFallback
                    ? $"{compatibilityReason} Turn off overlay-only compatibility mode only if you have tested this display path."
                    : "Temporarily dims this monitor's physical backlight by a small amount, then restores it automatically. Does not enable hardware dimming.",
            };
            System.Windows.Automation.AutomationProperties.SetName(testHardwareBrightnessButton, $"Test hardware brightness for {ShortDeviceName(deviceName)}");
            testHardwareBrightnessButton.Click += (_, _) => TestHardwareBrightness(deviceName, monitor, hardwareBrightnessBox);
            Grid.SetColumn(testHardwareBrightnessButton, 6);

            row.Children.Add(label);
            row.Children.Add(excludeBox);
            row.Children.Add(colorExcludeBox);
            row.Children.Add(multiplierBox);
            row.Children.Add(kelvinOffsetBox);
            row.Children.Add(hardwareBrightnessBox);
            row.Children.Add(testHardwareBrightnessButton);
            rows.Add(row);
        }

        MonitorsList.ItemsSource = rows;
    }

    /// <summary>
    /// Detects DDC/CI capability without changing monitor state. Automatic hardware brightness
    /// remains unavailable until the user completes the explicit reversible per-monitor test.
    /// </summary>
    private void RefreshHardwareBrightnessReadiness()
    {
        HardwareBrightnessReadinessText.Text = "Checking whether your monitors support optional hardware brightness…";
        _ = Task.Run(DdcCiBrightnessProbe.GetCapabilities).ContinueWith(task => Dispatcher.Invoke(() =>
        {
            if (task.IsFaulted)
            {
                DebugLog.Write($"Hardware brightness capability probe failed: {task.Exception}");
                HardwareBrightnessReadinessText.Text = "Optional hardware brightness could not be checked. Monitor Wellness will continue using its overlay dimmer.";
                return;
            }

            var capabilities = task.Result;
            int supported = capabilities.Count(capability => capability.IsSupported);
            HardwareBrightnessReadinessText.Text = supported == 0
                ? "Optional hardware brightness: no compatible DDC/CI monitor was detected. The flicker-free overlay remains in use."
                : $"Optional hardware brightness: {supported} of {capabilities.Count} monitor(s) report DDC/CI support. Test a monitor first; after a successful test, tick HW and Save to opt in to scheduled physical brightness.";
        }));
    }

    private static string ShortDeviceName(string deviceName) => deviceName[(deviceName.LastIndexOf('\\') + 1)..];
}
