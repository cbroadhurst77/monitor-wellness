using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MonitorWellness.Core;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace MonitorWellness;

/// <summary>
/// Minimal but functional settings editor. Mutates the live AppSettings instance in place on
/// Save (the same object App holds), so there's no separate "apply" step to keep in sync —
/// App's next schedule tick just reads the updated values. onSaved is called after
/// persisting, so App can rebuild the hotkey and reapply the schedule immediately rather than
/// waiting for the next 30s tick or app restart.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly OverlayController _overlay;
    private readonly Action _onSaved;

    private readonly Dictionary<string, CheckBox> _excludeBoxes = new();
    private readonly Dictionary<string, TextBox> _multiplierBoxes = new();

    private uint _pendingHotkeyModifiers;
    private uint _pendingHotkeyKey;

    public SettingsWindow(AppSettings settings, OverlayController overlay, Action onSaved)
    {
        InitializeComponent();
        _settings = settings;
        _overlay = overlay;
        _onSaved = onSaved;

        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        LatitudeBox.Text = _settings.Latitude.ToString(CultureInfo.InvariantCulture);
        LongitudeBox.Text = _settings.Longitude.ToString(CultureInfo.InvariantCulture);
        DayKelvinBox.Text = _settings.DayKelvin.ToString(CultureInfo.InvariantCulture);
        NightKelvinBox.Text = _settings.NightKelvin.ToString(CultureInfo.InvariantCulture);
        DayBrightnessBox.Text = _settings.DayBrightness.ToString(CultureInfo.InvariantCulture);
        NightBrightnessBox.Text = _settings.NightBrightness.ToString(CultureInfo.InvariantCulture);
        MigraineColorBox.Text = _settings.MigraineOverlayColorHex;
        MigraineOpacityBox.Text = _settings.MigraineOverlayOpacity.ToString(CultureInfo.InvariantCulture);

        _pendingHotkeyModifiers = _settings.MigraineHotkeyModifiers;
        _pendingHotkeyKey = _settings.MigraineHotkeyKey;
        HotkeyBox.Text = FormatHotkey(_pendingHotkeyModifiers, _pendingHotkeyKey);

        BuildMonitorRows();
    }

    private void BuildMonitorRows()
    {
        _excludeBoxes.Clear();
        _multiplierBoxes.Clear();

        var rows = new List<UIElement>();
        foreach (var deviceName in _overlay.DeviceNames.OrderBy(d => d))
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

            var label = new TextBlock { Text = ShortDeviceName(deviceName), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 0);

            var excludeBox = new CheckBox
            {
                Content = "Exclude",
                IsChecked = _settings.ExcludedMonitors.Contains(deviceName),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            Grid.SetColumn(excludeBox, 1);
            _excludeBoxes[deviceName] = excludeBox;

            double multiplier = _settings.MonitorDimMultiplier.TryGetValue(deviceName, out var m) ? m : 1.0;
            var multiplierBox = new TextBox { Text = multiplier.ToString(CultureInfo.InvariantCulture) };
            Grid.SetColumn(multiplierBox, 2);
            _multiplierBoxes[deviceName] = multiplierBox;

            row.Children.Add(label);
            row.Children.Add(excludeBox);
            row.Children.Add(multiplierBox);
            rows.Add(row);
        }

        MonitorsList.ItemsSource = rows;
    }

    private static string ShortDeviceName(string deviceName) => deviceName[(deviceName.LastIndexOf('\\') + 1)..];

    private void IdentifyButton_Click(object sender, RoutedEventArgs e) => _overlay.IdentifyMonitors(TimeSpan.FromSeconds(6));

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Ignore a bare modifier keypress — wait for a real key while modifiers are held.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        uint modifiers = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= GlobalHotkey.MOD_CONTROL;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= GlobalHotkey.MOD_ALT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= GlobalHotkey.MOD_SHIFT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= GlobalHotkey.MOD_WIN;

        if (modifiers == 0)
        {
            HotkeyBox.Text = "Include at least one of Ctrl/Alt/Shift, then press a key";
            return;
        }

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        _pendingHotkeyModifiers = modifiers;
        _pendingHotkeyKey = vk;
        HotkeyBox.Text = FormatHotkey(modifiers, vk);
    }

    private static string FormatHotkey(uint modifiers, uint vk)
    {
        var parts = new List<string>();
        if ((modifiers & GlobalHotkey.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & GlobalHotkey.MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & GlobalHotkey.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((modifiers & GlobalHotkey.MOD_WIN) != 0) parts.Add("Win");

        Key key = KeyInterop.KeyFromVirtualKey((int)vk);
        parts.Add(key.ToString());

        return string.Join("+", parts);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseAll(out string error))
        {
            System.Windows.MessageBox.Show(this, error, "Monitor Wellness", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SettingsStore.Save(_settings);
        _onSaved();
        Close();
    }

    private bool TryParseAll(out string error)
    {
        error = "";

        if (!double.TryParse(LatitudeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double lat) || lat < -90 || lat > 90)
        {
            error = "Latitude must be a number between -90 and 90.";
            return false;
        }
        if (!double.TryParse(LongitudeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double lon) || lon < -180 || lon > 180)
        {
            error = "Longitude must be a number between -180 and 180.";
            return false;
        }
        if (!int.TryParse(DayKelvinBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dayKelvin))
        {
            error = "Day color temp must be a whole number.";
            return false;
        }
        if (!int.TryParse(NightKelvinBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int nightKelvin))
        {
            error = "Night color temp must be a whole number.";
            return false;
        }
        if (!double.TryParse(DayBrightnessBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double dayBrightness) || dayBrightness < 0 || dayBrightness > 1)
        {
            error = "Day brightness must be between 0 and 1.";
            return false;
        }
        if (!double.TryParse(NightBrightnessBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double nightBrightness) || nightBrightness < 0 || nightBrightness > 1)
        {
            error = "Night brightness must be between 0 and 1.";
            return false;
        }
        if (!double.TryParse(MigraineOpacityBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double migraineOpacity) || migraineOpacity < 0 || migraineOpacity > 1)
        {
            error = "Migraine overlay opacity must be between 0 and 1.";
            return false;
        }

        string migraineColorHex = MigraineColorBox.Text.Trim();
        try
        {
            _ = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(migraineColorHex)!;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or InvalidOperationException)
        {
            error = "Migraine overlay color must be a valid hex color, e.g. #321408.";
            return false;
        }

        var multipliers = new Dictionary<string, double>();
        foreach (var (deviceName, box) in _multiplierBoxes)
        {
            if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double multiplier) || multiplier < 0)
            {
                error = $"Dim multiplier for {ShortDeviceName(deviceName)} must be a number >= 0.";
                return false;
            }
            multipliers[deviceName] = multiplier;
        }

        // All parsed successfully — commit to the live settings object.
        _settings.Latitude = lat;
        _settings.Longitude = lon;
        _settings.DayKelvin = dayKelvin;
        _settings.NightKelvin = nightKelvin;
        _settings.DayBrightness = dayBrightness;
        _settings.NightBrightness = nightBrightness;
        _settings.MigraineOverlayColorHex = migraineColorHex;
        _settings.MigraineOverlayOpacity = migraineOpacity;
        _settings.MigraineHotkeyModifiers = _pendingHotkeyModifiers;
        _settings.MigraineHotkeyKey = _pendingHotkeyKey;
        _settings.MonitorDimMultiplier = multipliers;
        _settings.ExcludedMonitors = _excludeBoxes
            .Where(kv => kv.Value.IsChecked == true)
            .Select(kv => kv.Key)
            .ToList();

        return true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
