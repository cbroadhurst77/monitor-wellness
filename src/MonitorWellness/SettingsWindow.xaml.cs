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
using TextBox = System.Windows.Controls.TextBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

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
    private readonly Func<string> _getStatusText;
    private readonly GeocodingService _geocoding = new();

    private static readonly TimeSpan StatusRefreshInterval = TimeSpan.FromSeconds(2);
    private readonly DispatcherTimer _statusRefreshTimer;

    // Throttles the live slider preview to roughly 10 updates/second so a fast drag across a
    // wide Kelvin/brightness/opacity range can't write rapid, un-throttled changes straight to
    // the real screen — the one motion-safety exception in an otherwise flicker-safe app. See
    // MonitorWellness_UX_Accessibility_Audit.html §2.2/§2.5/§6 (P0). The timer only runs while
    // a preview is actually pending and stops itself once flushed, so idle time costs nothing.
    private static readonly TimeSpan PreviewThrottleInterval = TimeSpan.FromMilliseconds(100);
    private readonly HashSet<string> _pendingPreviews = new();
    private DispatcherTimer? _previewThrottleTimer;

    private readonly Dictionary<string, CheckBox> _excludeBoxes = new();
    private readonly Dictionary<string, CheckBox> _colorExcludeBoxes = new();
    private readonly Dictionary<string, TextBox> _multiplierBoxes = new();
    private readonly Dictionary<string, TextBox> _kelvinOffsetBoxes = new();

    private uint _pendingHotkeyModifiers;
    private uint _pendingHotkeyKey;
    private bool _loaded;

    /// <summary>
    /// Pairs each slider with an adjacent editable text field, added so a value can be typed
    /// or pasted directly (e.g. to match a specific Kelvin value from elsewhere) rather than
    /// only ever dragged — Week 7's slider-only redesign was good for live preview but removed
    /// exact entry entirely; this restores it alongside the sliders instead of instead of them.
    /// See TECHNICAL_UX_REVIEW.md §2.3.
    /// </summary>
    private readonly List<(TextBox Input, Slider Slider, bool IsPercentage)> _numericInputs = new();

    public SettingsWindow(AppSettings settings, GammaControllerManager gammaManager, OverlayController overlay, Action onSaved, Func<string> getStatusText)
    {
        InitializeComponent();
        ThemeDetector.ApplyDarkThemeIfNeeded(this);
        ThemeDetector.EnableLiveThemeUpdates(this);
        Icon = AppIconSource.Default;
        _settings = settings;
        _gammaManager = gammaManager;
        _overlay = overlay;
        _onSaved = onSaved;
        _getStatusText = getStatusText;

        _numericInputs.Add((DayKelvinInput, DayKelvinSlider, false));
        _numericInputs.Add((NightKelvinInput, NightKelvinSlider, false));
        _numericInputs.Add((DayBrightnessInput, DayBrightnessSlider, true));
        _numericInputs.Add((NightBrightnessInput, NightBrightnessSlider, true));
        _numericInputs.Add((MigraineOpacityInput, MigraineOpacitySlider, true));
        _numericInputs.Add((MigraineContrastInput, MigraineContrastSlider, true));
        _numericInputs.Add((DeepNightBrightnessInput, DeepNightBrightnessSlider, true));

        LoadFromSettings();

        // Refreshed on a short timer (not just once at load) so the status line stays accurate
        // for as long as this window is left open — e.g. a Migraine Mode auto-revert countdown
        // ticking down, or a schedule pause expiring, while Settings sits open in the background.
        CurrentStatusText.Text = _getStatusText();
        _statusRefreshTimer = new DispatcherTimer { Interval = StatusRefreshInterval };
        _statusRefreshTimer.Tick += (_, _) => CurrentStatusText.Text = _getStatusText();
        _statusRefreshTimer.Start();
        Closed += (_, _) =>
        {
            _statusRefreshTimer.Stop();
            _previewThrottleTimer?.Stop();
        };
    }

    /// <summary>Commits on losing focus — covers clicking away or Tabbing to the next field.</summary>
    private void NumericInput_LostFocus(object sender, RoutedEventArgs e) => CommitNumericInput((TextBox)sender);

    /// <summary>Commits on Enter too, without waiting for focus to move elsewhere.</summary>
    private void NumericInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CommitNumericInput((TextBox)sender);
        Keyboard.ClearFocus();
        e.Handled = true;
    }

    private void CommitNumericInput(TextBox box)
    {
        int index = _numericInputs.FindIndex(x => x.Input == box);
        if (index < 0) return;
        var entry = _numericInputs[index];

        if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double typed))
        {
            RefreshNumericInputText(entry); // invalid text — revert to whatever the slider actually holds
            return;
        }

        double sliderValue = entry.IsPercentage ? typed / 100.0 : typed;
        // Setting Slider.Value fires its existing ValueChanged handler (DaySlider_ValueChanged /
        // NightSlider_ValueChanged / MigrainePreview_Changed), which already previews and calls
        // UpdateSliderLabels() — no separate preview call needed here.
        entry.Slider.Value = Math.Clamp(sliderValue, entry.Slider.Minimum, entry.Slider.Maximum);
    }

    private static void RefreshNumericInputText((TextBox Input, Slider Slider, bool IsPercentage) entry)
    {
        if (entry.Input.IsKeyboardFocused) return; // don't clobber what the user is mid-typing
        entry.Input.Text = entry.IsPercentage
            ? $"{Math.Round(entry.Slider.Value * 100)}"
            : $"{(int)entry.Slider.Value}";
    }

    private void LoadFromSettings()
    {
        LatitudeBox.Text = _settings.Latitude.ToString(CultureInfo.InvariantCulture);
        LongitudeBox.Text = _settings.Longitude.ToString(CultureInfo.InvariantCulture);
        LoadPreferencesFrom(_settings);

        BuildMonitorRows();
        RefreshProfilesComboBox();

        LoadWorldMapImage();
        HistoryTrackingCheckBox.IsChecked = _settings.HistoryTrackingEnabled;
        MatchAmbientLightCheckBox.IsChecked = _settings.MatchAmbientLight;
        BreakReminderCheckBox.IsChecked = _settings.BreakReminderEnabled;
        BreakReminderIntervalSlider.Value = _settings.BreakReminderIntervalMinutes;
        // HistoryTrackingCheckBox's own Checked/Unchecked handler (fired by setting its
        // IsChecked above) already set PromptForRatingCheckBox.IsEnabled to match — this just
        // loads the actual saved value into it.
        PromptForRatingCheckBox.IsChecked = _settings.PromptForMigraineRating;
        AmbientLightAvailabilityText.Text = AmbientLightSensor.IsAvailable
            ? "An ambient light sensor was found on this device."
            : "No ambient light sensor was found on this device — most desktops don't have one (it's mostly a laptop/tablet feature), so this option will have no effect here even if turned on.";
        CheckForUpdatesCheckBox.IsChecked = _settings.CheckForUpdatesEnabled;
        var runningVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        CurrentVersionText.Text = runningVersion is not null ? $"You're running version {runningVersion.ToString(3)}." : "";
        _loaded = true;
        UpdateMapMarker();
        UpdateSunTimesDisplay();
        UpdateSliderLabels();
        RefreshHistorySummary();
        PreviewDay(); // something visible on screen the moment the window opens, matching whichever phase was last touched (day, as the default starting point)
    }

    /// <summary>
    /// Live-preview only — like every other control in this window, the actual setting isn't
    /// committed until Save (see TryParseAll), so Cancel genuinely leaves it untouched. This
    /// previously saved immediately on toggle, bypassing Cancel entirely — a real bug caught on
    /// re-review, not by any earlier hand-testing, since a toggle-then-Cancel sequence is easy
    /// to not think to try. RefreshHistorySummary reads the checkbox's own IsChecked state
    /// directly (not _settings), so the live summary preview still works with no settings
    /// mutation needed here.
    /// </summary>
    private void HistoryTrackingCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        PromptForRatingCheckBox.IsEnabled = HistoryTrackingCheckBox.IsChecked == true;
        if (HistoryTrackingCheckBox.IsChecked != true)
            PromptForRatingCheckBox.IsChecked = false; // the rating prompt only makes sense alongside history tracking — see MigraineModeController.Deactivate

        if (!_loaded) return;
        RefreshHistorySummary();
    }

    private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            this,
            "This will permanently delete your saved history. Are you sure?",
            "Monitor Wellness",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        HistoryStore.Clear();
        RefreshHistorySummary();
    }

    /// <summary>
    /// The aggregate summary above answers "how often and how well, on average" — this hands
    /// over the same raw HistoryEvent records HistoryStore.Load() already returns, one row per
    /// event, so a user can cross-reference activation timing against anything else they track
    /// (e.g. a personal migraine diary) rather than being limited to the on-screen averages.
    /// </summary>
    private void ExportHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            FileName = "MonitorWellness-history.csv",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            Title = "Export Monitor Wellness History",
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var events = HistoryStore.Load();
            var lines = new List<string> { "TimestampUtc,EventType,Mild,DurationSeconds,Rating" };
            lines.AddRange(events.Select(evt => string.Join(",",
                evt.TimestampUtc.ToString("O", CultureInfo.InvariantCulture),
                evt.EventType,
                evt.Mild?.ToString() ?? "",
                evt.DurationSeconds?.ToString(CultureInfo.InvariantCulture) ?? "",
                evt.Rating?.ToString(CultureInfo.InvariantCulture) ?? "")));
            File.WriteAllLines(dialog.FileName, lines);
            System.Windows.MessageBox.Show(this, $"History exported to {dialog.FileName}.", "Monitor Wellness", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Windows.MessageBox.Show(this, $"Couldn't export history: {ex.Message}", "Monitor Wellness", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshHistorySummary()
    {
        if (HistoryTrackingCheckBox.IsChecked != true)
        {
            HistorySummaryText.Text = "History tracking is off.";
            return;
        }

        var summary = HistorySummarizer.Summarize(HistoryStore.Load(), DateTime.UtcNow);
        HistorySummaryText.Text = summary.TotalActivations == 0
            ? "No Migraine Mode activations recorded yet."
            : $"{summary.TotalActivations} total activation(s) ({summary.FullCount} full, {summary.MildCount} mild) — " +
              $"{summary.ActivationsLast7Days} in the last 7 days, {summary.ActivationsLast30Days} in the last 30." +
              (summary.AverageDurationMinutes is double avg ? $" Average duration: {avg:F0} min." : "") +
              (summary.PauseCount > 0 ? $" Schedule paused {summary.PauseCount} time(s)." : "") +
              (summary.AverageRating is double avgRating ? $" Average helpfulness: {avgRating:F1}/5 ({summary.RatingCount} rating(s))." : "");
    }

    /// <summary>
    /// Populates the color/brightness/migraine controls from any AppSettings-shaped source —
    /// used both to load the real saved settings and by the Reset button (with a fresh
    /// `new AppSettings()`). Deliberately excludes Latitude/Longitude/ExcludedMonitors/
    /// MonitorDimMultiplier — those are personal location and hardware setup, not
    /// "preferences" in the sense Reset is meant to cover.
    /// </summary>
    private void LoadPreferencesFrom(AppSettings source)
    {
        DayKelvinSlider.Value = source.DayKelvin;
        NightKelvinSlider.Value = source.NightKelvin;
        DayBrightnessSlider.Value = source.DayBrightness;
        NightBrightnessSlider.Value = source.NightBrightness;
        MigraineColorBox.Text = source.MigraineOverlayColorHex;
        MigraineOpacitySlider.Value = source.MigraineOverlayOpacity;
        MigraineContrastSlider.Value = source.MigraineContrastReduction;
        MigraineAutoRevertSlider.Value = source.MigraineAutoRevertMinutes;
        PlaySoundCheckBox.IsChecked = source.PlaySoundOnMigraineToggle;

        BedtimeEnabledCheckBox.IsChecked = !string.IsNullOrWhiteSpace(source.BedtimeLocal);
        BedtimeBox.Text = source.BedtimeLocal ?? "22:30";
        BedtimeBox.IsEnabled = BedtimeEnabledCheckBox.IsChecked == true;

        _pendingHotkeyModifiers = source.MigraineHotkeyModifiers;
        _pendingHotkeyKey = source.MigraineHotkeyKey;
        HotkeyBox.Text = FormatHotkey(_pendingHotkeyModifiers, _pendingHotkeyKey);

        DeepNightBrightnessSlider.Value = source.DeepNightBrightness;
        DeepNightColorBox.Text = source.DeepNightOverlayColorHex;
    }

    /// <summary>
    /// Two experiential starting points for the Day/Night schedule as a whole, mirroring the
    /// existing Migraine Gentle/Strong preset pattern — see TECHNICAL_UX_REVIEW.md §4.3, which
    /// only ever closed this gap for migraine intensity, not for the schedule most people will
    /// tune first. Night is set first so Day (previewed last, and the phase shown by default
    /// when this window opens) is what's left on screen after clicking either button.
    /// </summary>
    private void TryCoolerBrighterPreset_Click(object sender, RoutedEventArgs e) => ApplyDayNightPreset(6500, 1.0, 4200, 0.9);
    private void TryWarmerDimmerPreset_Click(object sender, RoutedEventArgs e) => ApplyDayNightPreset(4500, 0.7, 3400, 0.7);

    private void ApplyDayNightPreset(int dayKelvin, double dayBrightness, int nightKelvin, double nightBrightness)
    {
        // Setting .Value fires the sliders' existing ValueChanged handlers, which already
        // preview live and refresh labels/numeric inputs — no separate preview call needed.
        NightKelvinSlider.Value = nightKelvin;
        NightBrightnessSlider.Value = nightBrightness;
        DayKelvinSlider.Value = dayKelvin;
        DayBrightnessSlider.Value = dayBrightness;
    }

    private void ExportSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            FileName = "MonitorWellness-settings.json",
            Filter = "Monitor Wellness settings (*.json)|*.json|All files (*.*)|*.*",
            Title = "Export Monitor Wellness Settings",
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            // Exports the last-Saved settings, not whatever's mid-edit in this window's
            // controls right now — consistent with every other control here, nothing is
            // "real" until Save. If the user has unsaved changes they want in the export,
            // the dialog text (below, in XAML) says so.
            SettingsStore.ExportTo(_settings, dialog.FileName);
            System.Windows.MessageBox.Show(this, $"Settings exported to {dialog.FileName}.", "Monitor Wellness", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Windows.MessageBox.Show(this, $"Couldn't export settings: {ex.Message}", "Monitor Wellness", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ImportSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Monitor Wellness settings (*.json)|*.json|All files (*.*)|*.*",
            Title = "Import Monitor Wellness Settings",
        };
        if (dialog.ShowDialog(this) != true)
            return;

        if (!SettingsStore.TryImportFrom(dialog.FileName, out var imported, out string error))
        {
            System.Windows.MessageBox.Show(this, error, "Monitor Wellness", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ApplyImportedSettings(imported);
        System.Windows.MessageBox.Show(this, "Settings imported. Review the values below, then click Save to keep them.", "Monitor Wellness", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// Populates every control in this window (location, monitor rows, and the Reset/Profile
    /// "preferences" subset) from an imported AppSettings — the superset LoadFromSettings
    /// already applies at construction time, repeated here for an arbitrary source instead of
    /// the real saved settings. Mutates the working _settings copy in place first so
    /// BuildMonitorRows() (which reads _settings directly) sees the imported values — nothing
    /// reaches disk until Save, same as every other control in this window.
    /// </summary>
    private void ApplyImportedSettings(AppSettings imported)
    {
        _settings.Latitude = imported.Latitude;
        _settings.Longitude = imported.Longitude;
        _settings.ExcludedMonitors = imported.ExcludedMonitors;
        _settings.ColorExcludedMonitors = imported.ColorExcludedMonitors;
        _settings.MonitorDimMultiplier = imported.MonitorDimMultiplier;
        _settings.MonitorKelvinOffset = imported.MonitorKelvinOffset;
        _settings.HistoryTrackingEnabled = imported.HistoryTrackingEnabled;
        _settings.PromptForMigraineRating = imported.PromptForMigraineRating;
        _settings.MatchAmbientLight = imported.MatchAmbientLight;
        _settings.BreakReminderEnabled = imported.BreakReminderEnabled;
        _settings.BreakReminderIntervalMinutes = imported.BreakReminderIntervalMinutes;
        _settings.CheckForUpdatesEnabled = imported.CheckForUpdatesEnabled;

        LatitudeBox.Text = _settings.Latitude.ToString(CultureInfo.InvariantCulture);
        LongitudeBox.Text = _settings.Longitude.ToString(CultureInfo.InvariantCulture);
        LoadPreferencesFrom(imported);
        BuildMonitorRows();
        HistoryTrackingCheckBox.IsChecked = _settings.HistoryTrackingEnabled;
        PromptForRatingCheckBox.IsChecked = _settings.PromptForMigraineRating;
        MatchAmbientLightCheckBox.IsChecked = _settings.MatchAmbientLight;
        BreakReminderCheckBox.IsChecked = _settings.BreakReminderEnabled;
        BreakReminderIntervalSlider.Value = _settings.BreakReminderIntervalMinutes;
        CheckForUpdatesCheckBox.IsChecked = _settings.CheckForUpdatesEnabled;

        UpdateMapMarker();
        UpdateSunTimesDisplay();
        UpdateSliderLabels();
        RefreshHistorySummary();
        PreviewDay();
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            this,
            "This resets your color, brightness, and Migraine Mode settings back to the defaults. Your location and monitor setup stay the same. Continue?",
            "Monitor Wellness",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        LoadPreferencesFrom(new AppSettings());
        UpdateSliderLabels();
        PreviewDay();
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

        if (result is null)
        {
            LocationSearchStatus.Text = $"Couldn't find \"{query}\" — try a different spelling, or a nearby larger town.";
        }
        else
        {
            LatitudeBox.Text = result.Latitude.ToString("F4", CultureInfo.InvariantCulture);
            LongitudeBox.Text = result.Longitude.ToString("F4", CultureInfo.InvariantCulture);
            LocationSearchStatus.Text = $"Found: {result.DisplayName}";
        }

        // Nominatim's usage policy asks for roughly no more than 1 request/second. This is a
        // manual, one-off search box, not a batch process, so the real-world risk is low --
        // but keeping the button disabled a beat past the request itself closes the gap
        // between "compliant in practice" and "compliant by design" for this app's only
        // network call.
        await Task.Delay(TimeSpan.FromSeconds(1));
        FindLocationButton.IsEnabled = true;
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
        QueuePreview("day");
    }

    private void NightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_loaded) return;
        UpdateSliderLabels();
        QueuePreview("night");
    }

    private void MigrainePreview_Changed(object sender, EventArgs e)
    {
        if (!_loaded) return;
        UpdateSliderLabels();
        QueuePreview("migraine");
    }

    /// <summary>
    /// Queues a preview kind to be applied on the next throttle tick rather than immediately —
    /// starts the shared throttle timer on first use and lets it stop itself once nothing is
    /// pending, so a fast slider drag can only ever write to the real screen at the throttled
    /// rate (see PreviewThrottleInterval), while a single slow adjustment still applies within
    /// one tick. Reading the sliders' live .Value inside Preview*() (rather than snapshotting
    /// here) means the last value before the user stops dragging is always what gets applied,
    /// even if several ValueChanged events were coalesced into one flush.
    /// </summary>
    private void QueuePreview(string kind)
    {
        _pendingPreviews.Add(kind);
        if (_previewThrottleTimer is not null)
            return;

        // DispatcherPriority.Render, not the DispatcherTimer default (Background) -- confirmed
        // that a Background-priority timer never got a turn to fire for as long as the mouse
        // kept dragging a slider, since the continuous stream of Input/Render/Normal-priority
        // drag events monopolized the dispatcher queue ahead of it. The whole point of this
        // timer is to update the on-screen preview WHILE dragging, so it needs a priority that
        // can actually interleave with that drag traffic instead of only ever running once the
        // drag stops and the queue finally idles down to Background.
        _previewThrottleTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = PreviewThrottleInterval };
        _previewThrottleTimer.Tick += (_, _) => FlushPendingPreviews();
        _previewThrottleTimer.Start();
    }

    private void FlushPendingPreviews()
    {
        if (_pendingPreviews.Count == 0)
        {
            _previewThrottleTimer?.Stop();
            _previewThrottleTimer = null;
            return;
        }

        if (_pendingPreviews.Contains("day")) PreviewDay();
        if (_pendingPreviews.Contains("night")) PreviewNight();
        if (_pendingPreviews.Contains("migraine")) PreviewMigraine();
        _pendingPreviews.Clear();
    }

    private void MigraineAutoRevertSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loaded) UpdateSliderLabels();
    }

    /// <summary>
    /// No live preview here (unlike Day/Night/migraine) — deep night only kicks in once solar
    /// elevation drops past nautical twilight or the bedtime clock, a state this window can't
    /// easily fake a preview for on demand.
    /// </summary>
    private void DeepNightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loaded) UpdateSliderLabels();
    }

    private void BedtimeEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        BedtimeBox.IsEnabled = BedtimeEnabledCheckBox.IsChecked == true;
        if (_loaded) UpdateBedtimeWarning();
    }

    private void BedtimeBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loaded) UpdateBedtimeWarning();
    }

    private void DeepNightColorBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loaded) UpdateHexColorWarning(DeepNightColorBox, DeepNightColorWarning, "Deep-night overlay color");
    }

    /// <summary>
    /// Replaces the previous blocking Save-time MessageBox for hex-color/bedtime validation
    /// with inline warning text next to the field itself — the same pattern already used
    /// successfully for KelvinSafetyWarning above, just applied to the other two fields that
    /// used to only ever surface their errors as a modal dialog at Save time. See
    /// MonitorWellness_UX_Accessibility_Audit.html §2.6/§6/§7.2 (P1). Returns whether the
    /// field is currently valid, so TryParseAll can gate Save on it without needing a second,
    /// separate validation pass.
    /// </summary>
    private static bool UpdateHexColorWarning(TextBox box, TextBlock warning, string fieldLabel)
    {
        bool valid = TryValidateHexColor(box, fieldLabel, out _, out string error);
        warning.Text = valid ? "" : error;
        warning.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
        return valid;
    }

    private bool UpdateBedtimeWarning()
    {
        bool valid = TryValidateBedtime(out _, out string error);
        BedtimeWarning.Text = valid ? "" : error;
        BedtimeWarning.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
        return valid;
    }

    private void BreakReminderCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        BreakReminderIntervalSlider.IsEnabled = BreakReminderCheckBox.IsChecked == true;
    }

    private void BreakReminderIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Fires once during InitializeComponent itself -- setting Minimum="5" in XAML coerces
        // the slider's not-yet-explicitly-set Value up from its 0 default, raising this event
        // before BreakReminderIntervalLabel (declared later in the same XAML file) has been
        // connected to its field yet. Guarding on _loaded (like every other slider handler in
        // this file) isn't right here specifically, because this is the only place that ever
        // sets this label's text -- _loaded doesn't flip true until after LoadFromSettings
        // already assigns BreakReminderIntervalSlider.Value from the real saved setting, and
        // skipping that assignment's event would leave the label blank until first dragged.
        if (BreakReminderIntervalLabel is null) return;
        BreakReminderIntervalLabel.Text = $"{(int)BreakReminderIntervalSlider.Value} minutes";
    }

    private void UpdateSliderLabels()
    {
        foreach (var entry in _numericInputs)
            RefreshNumericInputText(entry);

        MigraineAutoRevertLabel.Text = MigraineAutoRevertSlider.Value <= 0
            ? "Never (stays on until you turn it off)"
            : $"{(int)MigraineAutoRevertSlider.Value} minutes";

        bool dayUnsafe = !ColorTemperature.IsSafeForGammaRamp((int)DayKelvinSlider.Value);
        bool nightUnsafe = !ColorTemperature.IsSafeForGammaRamp((int)NightKelvinSlider.Value);
        if (dayUnsafe || nightUnsafe)
        {
            string which = dayUnsafe && nightUnsafe ? "Day and Night color temps are" : dayUnsafe ? "Day color temp is" : "Night color temp is";
            KelvinSafetyWarning.Text = $"That {which} a little too warm for this screen to show smoothly. Try a slightly cooler setting — most screens are comfortable from about 3,300K up.";
            KelvinSafetyWarning.Visibility = Visibility.Visible;
        }
        else
        {
            KelvinSafetyWarning.Visibility = Visibility.Collapsed;
        }

        UpdateHexColorWarning(MigraineColorBox, MigraineColorWarning, "Migraine overlay color");
        UpdateHexColorWarning(DeepNightColorBox, DeepNightColorWarning, "Deep-night overlay color");
        UpdateBedtimeWarning();
    }

    private void PreviewDay() => ApplySchedulePreview((int)DayKelvinSlider.Value, DayBrightnessSlider.Value);

    private void PreviewNight() => ApplySchedulePreview((int)NightKelvinSlider.Value, NightBrightnessSlider.Value);

    private void ApplySchedulePreview(int kelvin, double globalBrightness)
    {
        foreach (var controller in _gammaManager.Controllers)
        {
            // Found on re-review: this previously checked _colorExcludeBoxes but never
            // _excludeBoxes, so checking "Exclude" and then dragging a slider still changed
            // that monitor's gamma ramp during preview — and since App.RunScheduleTick's real
            // path just skips excluded monitors (no reset), a monitor excluded mid-preview
            // could stay stuck at whatever color the preview last applied for the rest of the
            // session. Matches RunScheduleTick's real-path semantics: skip entirely, don't
            // touch this monitor's color at all (unlike Color-accurate exclude, which actively
            // resets to native).
            bool excluded = _excludeBoxes.TryGetValue(controller.DeviceName, out var excludeBox) && excludeBox.IsChecked == true;
            if (excluded)
                continue;

            bool colorExcluded = _colorExcludeBoxes.TryGetValue(controller.DeviceName, out var box) && box.IsChecked == true;
            if (colorExcluded)
            {
                controller.ResetToIdentity();
                continue;
            }

            int offset = 0;
            if (_kelvinOffsetBoxes.TryGetValue(controller.DeviceName, out var offsetBox))
                int.TryParse(offsetBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out offset);
            controller.ApplyColorTemperature(kelvin + offset); // silently no-ops on this monitor if the value is unsafe — see KelvinSafetyWarning
        }

        var brightnessByDevice = BuildBrightnessByDeviceForPreview(globalBrightness);
        _overlay.ApplyDim(brightnessByDevice, System.Windows.Media.Colors.Black);
    }

    /// <summary>
    /// Two experiential starting points for the migraine tint's intensity, so a user can pick
    /// "whichever feels more comfortable" rather than needing to interpret a raw opacity/
    /// contrast percentage cold — see TECHNICAL_UX_REVIEW.md §4.3. Deliberately not a third
    /// "correct" value: color/hue is already fixed by the evidence-backed default, only
    /// intensity varies here, and either preset is just a starting point for the sliders below.
    /// </summary>
    private const double GentlePresetOpacity = 0.5, GentlePresetContrast = 0.08;
    private const double StrongPresetOpacity = 0.85, StrongPresetContrast = 0.22;

    private void TryGentlePreset_Click(object sender, RoutedEventArgs e) => ApplyMigrainePreset(GentlePresetOpacity, GentlePresetContrast);
    private void TryStrongPreset_Click(object sender, RoutedEventArgs e) => ApplyMigrainePreset(StrongPresetOpacity, StrongPresetContrast);

    private void ApplyMigrainePreset(double opacity, double contrast)
    {
        // Setting .Value fires the sliders' existing ValueChanged handlers (MigrainePreview_
        // Changed), which already preview live on the real screen and refresh the numeric
        // inputs/labels — no separate preview call needed here.
        MigraineOpacitySlider.Value = Math.Clamp(opacity, MigraineOpacitySlider.Minimum, MigraineOpacitySlider.Maximum);
        MigraineContrastSlider.Value = Math.Clamp(contrast, MigraineContrastSlider.Minimum, MigraineContrastSlider.Maximum);
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
        _colorExcludeBoxes.Clear();
        _multiplierBoxes.Clear();
        _kelvinOffsetBoxes.Clear();

        var rows = new List<UIElement>();
        foreach (var deviceName in _overlay.DeviceNames.OrderBy(d => d))
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });

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
            System.Windows.Automation.AutomationProperties.SetName(multiplierBox, $"Dim multiplier for {ShortDeviceName(deviceName)}");
            Grid.SetColumn(multiplierBox, 3);
            _multiplierBoxes[deviceName] = multiplierBox;

            int kelvinOffset = _settings.MonitorKelvinOffset.TryGetValue(deviceName, out var k) ? k : 0;
            var kelvinOffsetBox = new TextBox { Text = kelvinOffset.ToString(CultureInfo.InvariantCulture), Margin = new Thickness(4, 0, 0, 0), ToolTip = "Kelvin offset for this monitor only — e.g. -300 if it reads warmer than the others at the same setting." };
            System.Windows.Automation.AutomationProperties.SetName(kelvinOffsetBox, $"Kelvin offset for {ShortDeviceName(deviceName)}");
            Grid.SetColumn(kelvinOffsetBox, 4);
            _kelvinOffsetBoxes[deviceName] = kelvinOffsetBox;

            row.Children.Add(label);
            row.Children.Add(excludeBox);
            row.Children.Add(colorExcludeBox);
            row.Children.Add(multiplierBox);
            row.Children.Add(kelvinOffsetBox);
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

    private static bool TryValidateHexColor(TextBox box, string fieldLabel, out string colorHex, out string error)
    {
        colorHex = box.Text.Trim();
        error = "";
        try
        {
            _ = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex)!;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or InvalidOperationException)
        {
            error = $"{fieldLabel} must be a valid hex color, e.g. #321408.";
            return false;
        }
    }

    private bool TryValidateMigraineColorHex(out string colorHex, out string error) =>
        TryValidateHexColor(MigraineColorBox, "Migraine overlay color", out colorHex, out error);

    private bool TryValidateDeepNightColorHex(out string colorHex, out string error) =>
        TryValidateHexColor(DeepNightColorBox, "Deep-night overlay color", out colorHex, out error);

    private bool TryValidateBedtime(out string? bedtimeLocal, out string error)
    {
        bedtimeLocal = null;
        error = "";
        if (BedtimeEnabledCheckBox.IsChecked != true)
            return true;

        if (!TimeSpan.TryParse(BedtimeBox.Text.Trim(), CultureInfo.InvariantCulture, out var parsed) || parsed < TimeSpan.Zero || parsed >= TimeSpan.FromDays(1))
        {
            error = "Bedtime must be in HH:mm format, e.g. 22:30.";
            return false;
        }

        bedtimeLocal = BedtimeBox.Text.Trim();
        return true;
    }

    /// <summary>
    /// Captures just the Day/Night/migraine/bedtime controls into a fresh AppSettings, for
    /// saving as a named profile — the same "preferences" subset LoadPreferencesFrom reads
    /// back out of one, deliberately excluding location and per-monitor setup. Does not
    /// touch/validate the location or monitor-row controls at all, unlike TryParseAll.
    /// </summary>
    private bool TryBuildPreferencesSnapshot(out AppSettings snapshot, out string error)
    {
        snapshot = new AppSettings();

        int dayKelvin = (int)DayKelvinSlider.Value;
        int nightKelvin = (int)NightKelvinSlider.Value;
        if (!ColorTemperature.IsSafeForGammaRamp(dayKelvin) || !ColorTemperature.IsSafeForGammaRamp(nightKelvin))
        {
            error = "Fix the Day/Night color temp warning above before saving a profile.";
            return false;
        }

        if (!TryValidateMigraineColorHex(out string migraineColorHex, out error))
            return false;

        if (!TryValidateDeepNightColorHex(out string deepNightColorHex, out error))
            return false;

        if (!TryValidateBedtime(out string? bedtimeLocal, out error))
            return false;

        snapshot.DayKelvin = dayKelvin;
        snapshot.NightKelvin = nightKelvin;
        snapshot.DayBrightness = DayBrightnessSlider.Value;
        snapshot.NightBrightness = NightBrightnessSlider.Value;
        snapshot.DeepNightBrightness = DeepNightBrightnessSlider.Value;
        snapshot.DeepNightOverlayColorHex = deepNightColorHex;
        snapshot.MigraineOverlayColorHex = migraineColorHex;
        snapshot.MigraineOverlayOpacity = MigraineOpacitySlider.Value;
        snapshot.MigraineContrastReduction = MigraineContrastSlider.Value;
        snapshot.MigraineAutoRevertMinutes = (int)MigraineAutoRevertSlider.Value;
        snapshot.PlaySoundOnMigraineToggle = PlaySoundCheckBox.IsChecked == true;
        snapshot.BedtimeLocal = bedtimeLocal;
        snapshot.MigraineHotkeyModifiers = _pendingHotkeyModifiers;
        snapshot.MigraineHotkeyKey = _pendingHotkeyKey;
        error = "";
        return true;
    }

    private void RefreshProfilesComboBox(string? selectName = null)
    {
        var names = ProfileStore.ListNames();
        ProfilesComboBox.ItemsSource = names;
        if (selectName is not null && names.Contains(selectName))
            ProfilesComboBox.SelectedItem = selectName;
        else if (names.Count > 0)
            ProfilesComboBox.SelectedIndex = 0;
    }

    private void LoadProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesComboBox.SelectedItem is not string name)
            return;

        var profile = ProfileStore.Load(name);
        if (profile is null)
        {
            System.Windows.MessageBox.Show(this, $"Couldn't load profile \"{name}\".", "Monitor Wellness", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        LoadPreferencesFrom(profile);
        UpdateSliderLabels();
        PreviewDay();
    }

    private void SaveProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildPreferencesSnapshot(out var snapshot, out string error))
        {
            System.Windows.MessageBox.Show(this, error, "Monitor Wellness", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new ProfileNameDialog(ProfilesComboBox.SelectedItem as string ?? "") { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ProfileStore.Save(dialog.ProfileName, snapshot);
        RefreshProfilesComboBox(dialog.ProfileName);
    }

    private void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesComboBox.SelectedItem is not string name)
            return;

        var result = System.Windows.MessageBox.Show(this, $"Delete profile \"{name}\"?", "Monitor Wellness", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        ProfileStore.Delete(name);
        RefreshProfilesComboBox();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseAll(out string error))
        {
            // An empty error means the reason is already showing as inline warning text next
            // to the offending field (hex color / bedtime) — nothing further to say in a dialog.
            if (!string.IsNullOrEmpty(error))
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
            error = $"{dayKelvin}K is a bit too warm for this screen. Please choose 3,400K or higher, then try saving again.";
            return false;
        }
        if (!ColorTemperature.IsSafeForGammaRamp(nightKelvin))
        {
            error = $"{nightKelvin}K is a bit too warm for this screen. Please choose 3,400K or higher, then try saving again.";
            return false;
        }
        // Brightness/opacity sliders are range-locked to 0-1 in XAML (Minimum/Maximum), so
        // unlike the text boxes they replaced, there's nothing to validate here.
        double dayBrightness = DayBrightnessSlider.Value;
        double nightBrightness = NightBrightnessSlider.Value;
        double migraineOpacity = MigraineOpacitySlider.Value;

        // Hex-color/bedtime problems surface as inline warning text next to the field itself
        // (UpdateSliderLabels keeps these current on every keystroke/slider move already) —
        // no blocking MessageBox needed here, just decline to save. See UpdateHexColorWarning/
        // UpdateBedtimeWarning and MonitorWellness_UX_Accessibility_Audit.html §2.6/§6 (P1).
        bool migraineHexValid = UpdateHexColorWarning(MigraineColorBox, MigraineColorWarning, "Migraine overlay color");
        bool deepNightHexValid = UpdateHexColorWarning(DeepNightColorBox, DeepNightColorWarning, "Deep-night overlay color");
        bool bedtimeValid = UpdateBedtimeWarning();
        if (!migraineHexValid || !deepNightHexValid || !bedtimeValid)
            return false;

        TryValidateMigraineColorHex(out string migraineColorHex, out _);
        TryValidateDeepNightColorHex(out string deepNightColorHex, out _);
        TryValidateBedtime(out string? bedtimeLocal, out _);

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

        var kelvinOffsets = new Dictionary<string, int>();
        foreach (var (deviceName, box) in _kelvinOffsetBoxes)
        {
            if (!int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int offset))
            {
                error = $"Kelvin offset for {ShortDeviceName(deviceName)} must be a whole number.";
                return false;
            }
            if (offset != 0)
                kelvinOffsets[deviceName] = offset;
        }

        // All parsed successfully — commit to the live settings object.
        _settings.Latitude = lat;
        _settings.Longitude = lon;
        _settings.DayKelvin = dayKelvin;
        _settings.NightKelvin = nightKelvin;
        _settings.DayBrightness = dayBrightness;
        _settings.NightBrightness = nightBrightness;
        _settings.DeepNightBrightness = DeepNightBrightnessSlider.Value;
        _settings.DeepNightOverlayColorHex = deepNightColorHex;
        _settings.MigraineOverlayColorHex = migraineColorHex;
        _settings.MigraineOverlayOpacity = migraineOpacity;
        _settings.MigraineContrastReduction = MigraineContrastSlider.Value;
        _settings.MigraineAutoRevertMinutes = (int)MigraineAutoRevertSlider.Value;
        _settings.PlaySoundOnMigraineToggle = PlaySoundCheckBox.IsChecked == true;
        _settings.BedtimeLocal = bedtimeLocal;
        _settings.MigraineHotkeyModifiers = _pendingHotkeyModifiers;
        _settings.MigraineHotkeyKey = _pendingHotkeyKey;
        _settings.MonitorDimMultiplier = multipliers;
        _settings.MonitorKelvinOffset = kelvinOffsets;
        _settings.HistoryTrackingEnabled = HistoryTrackingCheckBox.IsChecked == true;
        _settings.PromptForMigraineRating = PromptForRatingCheckBox.IsChecked == true;
        _settings.MatchAmbientLight = MatchAmbientLightCheckBox.IsChecked == true;
        _settings.BreakReminderEnabled = BreakReminderCheckBox.IsChecked == true;
        _settings.BreakReminderIntervalMinutes = (int)BreakReminderIntervalSlider.Value;
        _settings.CheckForUpdatesEnabled = CheckForUpdatesCheckBox.IsChecked == true;
        _settings.ExcludedMonitors = _excludeBoxes
            .Where(kv => kv.Value.IsChecked == true)
            .Select(kv => kv.Key)
            .ToList();
        _settings.ColorExcludedMonitors = _colorExcludeBoxes
            .Where(kv => kv.Value.IsChecked == true)
            .Select(kv => kv.Key)
            .ToList();

        return true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
