using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
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
    private readonly GammaControllerManager _gammaManager;
    private readonly OverlayController _overlay;
    private readonly Action _onSaved;
    private readonly GeocodingService _geocoding = new();

    private readonly Dictionary<string, CheckBox> _excludeBoxes = new();
    private readonly Dictionary<string, TextBox> _multiplierBoxes = new();

    private uint _pendingHotkeyModifiers;
    private uint _pendingHotkeyKey;
    private bool _loaded;

    public SettingsWindow(AppSettings settings, GammaControllerManager gammaManager, OverlayController overlay, Action onSaved)
    {
        InitializeComponent();
        _settings = settings;
        _gammaManager = gammaManager;
        _overlay = overlay;
        _onSaved = onSaved;

        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        LatitudeBox.Text = _settings.Latitude.ToString(CultureInfo.InvariantCulture);
        LongitudeBox.Text = _settings.Longitude.ToString(CultureInfo.InvariantCulture);
        DayKelvinSlider.Value = _settings.DayKelvin;
        NightKelvinSlider.Value = _settings.NightKelvin;
        DayBrightnessSlider.Value = _settings.DayBrightness;
        NightBrightnessSlider.Value = _settings.NightBrightness;
        MigraineColorBox.Text = _settings.MigraineOverlayColorHex;
        MigraineOpacitySlider.Value = _settings.MigraineOverlayOpacity;
        MigraineContrastSlider.Value = _settings.MigraineContrastReduction;
        MigraineAutoRevertSlider.Value = _settings.MigraineAutoRevertMinutes;

        _pendingHotkeyModifiers = _settings.MigraineHotkeyModifiers;
        _pendingHotkeyKey = _settings.MigraineHotkeyKey;
        HotkeyBox.Text = FormatHotkey(_pendingHotkeyModifiers, _pendingHotkeyKey);

        BuildMonitorRows();

        LoadWorldMapImage();
        _loaded = true;
        UpdateMapMarker();
        UpdateSunTimesDisplay();
        UpdateSliderLabels();
        PreviewDay(); // something visible on screen the moment the window opens, matching whichever phase was last touched (day, as the default starting point)
    }

    private void LoadWorldMapImage()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MonitorWellness.Assets.worldmap.jpg")
            ?? throw new InvalidOperationException("Embedded world map resource not found.");

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad; // load fully now so the stream can be disposed
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();

        MapImage.Source = bitmap;
    }

    /// <summary>
    /// Standard equirectangular projection: longitude maps linearly across the full image
    /// width (-180 to +180), latitude linearly down the full height (+90 to -90). The world
    /// map asset is a true equirectangular projection (see ATTRIBUTIONS.md), so this holds
    /// regardless of the container's on-screen size — MapContainer's Width/Height are fixed
    /// in XAML specifically so this math doesn't need to handle letterboxing.
    /// </summary>
    private void UpdateMapMarker()
    {
        if (!double.TryParse(LatitudeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double lat))
            return;
        if (!double.TryParse(LongitudeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
            return;

        lat = Math.Clamp(lat, -90, 90);
        lon = Math.Clamp(lon, -180, 180);

        double x = (lon + 180.0) / 360.0 * MapContainer.Width;
        double y = (90.0 - lat) / 180.0 * MapContainer.Height;

        Canvas.SetLeft(MapMarker, x - MapMarker.Width / 2.0);
        Canvas.SetTop(MapMarker, y - MapMarker.Height / 2.0);
    }

    private void LatLongBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loaded) return;
        UpdateMapMarker();
        UpdateSunTimesDisplay();
    }

    private void UpdateSunTimesDisplay()
    {
        if (!double.TryParse(LatitudeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double lat)
            || !double.TryParse(LongitudeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
        {
            SunTimesText.Text = "";
            return;
        }

        var today = DateTime.UtcNow;
        var sunrise = SolarCalculator.FindSunriseUtc(today, lat, lon);
        var sunset = SolarCalculator.FindSunsetUtc(today, lat, lon);

        SunTimesText.Text = (sunrise, sunset) switch
        {
            (null, null) => "No sunrise or sunset today at this location (polar day or night).",
            (null, not null) => $"Today: no sunrise (polar day), sunset {sunset.Value.ToLocalTime():HH:mm}",
            (not null, null) => $"Today: sunrise {sunrise.Value.ToLocalTime():HH:mm}, no sunset (polar day)",
            _ => $"Today: sunrise {sunrise!.Value.ToLocalTime():HH:mm}, sunset {sunset!.Value.ToLocalTime():HH:mm} (local time) — this is what drives the schedule below."
        };
    }

    private void MapContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(MapContainer);

        double lon = pos.X / MapContainer.Width * 360.0 - 180.0;
        double lat = 90.0 - pos.Y / MapContainer.Height * 180.0;

        LatitudeBox.Text = Math.Clamp(lat, -90, 90).ToString("F4", CultureInfo.InvariantCulture);
        LongitudeBox.Text = Math.Clamp(lon, -180, 180).ToString("F4", CultureInfo.InvariantCulture);
        LocationSearchStatus.Text = "";
    }

    private async void FindLocationButton_Click(object sender, RoutedEventArgs e)
    {
        string query = LocationSearchBox.Text.Trim();
        if (query.Length == 0)
            return;

        FindLocationButton.IsEnabled = false;
        LocationSearchStatus.Text = "Searching...";

        var result = await _geocoding.SearchAsync(query);

        FindLocationButton.IsEnabled = true;

        if (result is null)
        {
            LocationSearchStatus.Text = $"Couldn't find \"{query}\" — try a different spelling, or a nearby larger town.";
            return;
        }

        LatitudeBox.Text = result.Latitude.ToString("F4", CultureInfo.InvariantCulture);
        LongitudeBox.Text = result.Longitude.ToString("F4", CultureInfo.InvariantCulture);
        LocationSearchStatus.Text = $"Found: {result.DisplayName}";
    }

    // --- Live preview -------------------------------------------------------------------
    // Dragging any Day/Night/migraine slider applies it directly to the real gamma
    // ramp/overlay right now, so the effect can be judged before Save commits anything.
    // App suspends the normal 30s schedule tick for as long as this window is open
    // (App._settingsPreviewActive) so the two don't fight over the same monitors — see
    // App.xaml.cs's OpenSettingsWindow/RunScheduleTick. Cancel needs no special revert
    // logic: closing this window (Save or Cancel) just lets the real schedule resume,
    // which naturally shows whatever is actually saved.

    private void DaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_loaded) return;
        UpdateSliderLabels();
        PreviewDay();
    }

    private void NightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_loaded) return;
        UpdateSliderLabels();
        PreviewNight();
    }

    private void MigrainePreview_Changed(object sender, EventArgs e)
    {
        if (!_loaded) return;
        UpdateSliderLabels();
        PreviewMigraine();
    }

    private void MigraineAutoRevertSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loaded) UpdateSliderLabels();
    }

    private void UpdateSliderLabels()
    {
        DayKelvinLabel.Text = $"{(int)DayKelvinSlider.Value}K";
        NightKelvinLabel.Text = $"{(int)NightKelvinSlider.Value}K";
        DayBrightnessLabel.Text = $"{DayBrightnessSlider.Value:P0}";
        NightBrightnessLabel.Text = $"{NightBrightnessSlider.Value:P0}";
        MigraineOpacityLabel.Text = $"{MigraineOpacitySlider.Value:P0}";
        MigraineContrastLabel.Text = MigraineContrastSlider.Value <= 0
            ? "None"
            : $"{MigraineContrastSlider.Value:P0}";
        MigraineAutoRevertLabel.Text = MigraineAutoRevertSlider.Value <= 0
            ? "Never (stays on until you turn it off)"
            : $"{(int)MigraineAutoRevertSlider.Value} minutes";

        bool dayUnsafe = !ColorTemperature.IsSafeForGammaRamp((int)DayKelvinSlider.Value);
        bool nightUnsafe = !ColorTemperature.IsSafeForGammaRamp((int)NightKelvinSlider.Value);
        if (dayUnsafe || nightUnsafe)
        {
            string which = dayUnsafe && nightUnsafe ? "Day and Night" : dayUnsafe ? "Day" : "Night";
            KelvinSafetyWarning.Text = $"{which} color temp is too warm for this hardware's gamma ramp — confirmed directly, values below roughly 3300K are silently rejected. The preview below won't reflect this until you pick a higher value.";
            KelvinSafetyWarning.Visibility = Visibility.Visible;
        }
        else
        {
            KelvinSafetyWarning.Visibility = Visibility.Collapsed;
        }
    }

    private void PreviewDay() => ApplySchedulePreview((int)DayKelvinSlider.Value, DayBrightnessSlider.Value);

    private void PreviewNight() => ApplySchedulePreview((int)NightKelvinSlider.Value, NightBrightnessSlider.Value);

    private void ApplySchedulePreview(int kelvin, double globalBrightness)
    {
        foreach (var controller in _gammaManager.Controllers)
            controller.ApplyColorTemperature(kelvin); // silently no-ops on this monitor if the value is unsafe — see KelvinSafetyWarning

        var brightnessByDevice = BuildBrightnessByDeviceForPreview(globalBrightness);
        _overlay.ApplyDim(brightnessByDevice, System.Windows.Media.Colors.Black);
    }

    private void PreviewMigraine()
    {
        System.Windows.Media.Color color;
        try
        {
            color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(MigraineColorBox.Text.Trim())!;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or InvalidOperationException)
        {
            return; // invalid hex mid-typing — just skip preview until it's valid again, Save will still validate properly
        }

        double opacity = MigraineOpacitySlider.Value;
        var byDevice = _overlay.DeviceNames.ToDictionary(d => d, _ => (color, opacity));
        _overlay.Apply(byDevice);

        foreach (var controller in _gammaManager.Controllers)
            controller.ApplyColorTemperatureWithContrast(_settings.NightKelvin, MigraineContrastSlider.Value);
    }

    /// <summary>
    /// Mirrors App.ComputeScheduleTarget's per-monitor multiplier logic using this window's
    /// own live (unsaved) exclude/multiplier controls, so the preview matches what Save would
    /// actually produce. Parse failures fall back to "no effect from this monitor's override"
    /// rather than throwing mid-drag.
    /// </summary>
    private Dictionary<string, double> BuildBrightnessByDeviceForPreview(double globalBrightness)
    {
        var result = new Dictionary<string, double>();
        foreach (var deviceName in _overlay.DeviceNames)
        {
            bool excluded = _excludeBoxes.TryGetValue(deviceName, out var box) && box.IsChecked == true;
            if (excluded)
            {
                result[deviceName] = 1.0;
                continue;
            }

            double multiplier = 1.0;
            if (_multiplierBoxes.TryGetValue(deviceName, out var multiplierBox))
                double.TryParse(multiplierBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out multiplier);

            double dimAmount = (1.0 - globalBrightness) * multiplier;
            result[deviceName] = Math.Clamp(1.0 - dimAmount, 0.0, 1.0);
        }
        return result;
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
        int dayKelvin = (int)DayKelvinSlider.Value;
        int nightKelvin = (int)NightKelvinSlider.Value;
        if (!ColorTemperature.IsSafeForGammaRamp(dayKelvin))
        {
            error = $"Day color temp of {dayKelvin}K is too warm for this hardware's gamma ramp — Windows will silently reject it. Confirmed directly on this hardware: values below roughly 3300K fail outright. Try a higher value.";
            return false;
        }
        if (!ColorTemperature.IsSafeForGammaRamp(nightKelvin))
        {
            error = $"Night color temp of {nightKelvin}K is too warm for this hardware's gamma ramp — Windows will silently reject it. Confirmed directly on this hardware: values below roughly 3300K fail outright. Try a higher value (3400K is the safe floor found during testing).";
            return false;
        }
        // Brightness/opacity sliders are range-locked to 0-1 in XAML (Minimum/Maximum), so
        // unlike the text boxes they replaced, there's nothing to validate here.
        double dayBrightness = DayBrightnessSlider.Value;
        double nightBrightness = NightBrightnessSlider.Value;
        double migraineOpacity = MigraineOpacitySlider.Value;

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
        _settings.MigraineContrastReduction = MigraineContrastSlider.Value;
        _settings.MigraineAutoRevertMinutes = (int)MigraineAutoRevertSlider.Value;
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
