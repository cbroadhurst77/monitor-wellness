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

/// <summary>
/// Minimal but functional settings editor. Edits an isolated AppSettings draft and commits it
/// to the live instance App holds only on Save, so Cancel and Import remain non-destructive.
/// onSaved is called after persisting, so App can rebuild the hotkey and reapply the schedule
/// immediately rather than waiting for the next 30s tick or app restart.
/// </summary>
public partial class SettingsWindow : Window
{
    // The window edits this isolated draft. It is copied to _targetSettings only after every
    // field has passed validation and the user explicitly presses Save.
    private readonly AppSettings _settings;
    private readonly AppSettings _targetSettings;
    private readonly GammaControllerManager _gammaManager;
    private readonly OverlayController _overlay;
    private readonly Action _onSaved;
    private readonly Func<string> _getStatusText;

    private static readonly TimeSpan StatusRefreshInterval = TimeSpan.FromSeconds(2);
    private readonly DispatcherTimer _statusRefreshTimer;

    // Throttles the live slider preview to roughly 10 updates/second so a fast drag across a
    // wide Kelvin/brightness/opacity range can't write rapid, un-throttled changes straight to
    // the real screen — the one motion-safety exception in an otherwise flicker-safe app. See
    // MonitorWellness_UX_Accessibility_Audit.html §2.2/§2.5/§6 (P0). The timer only runs while
    // a preview is actually pending and stops itself once flushed, so idle time costs nothing.
    private static readonly TimeSpan PreviewThrottleInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan BlackoutPreviewDuration = TimeSpan.FromSeconds(15);
    private const double SafePreviewBrightness = 0.20;
    private readonly HashSet<string> _pendingPreviews = new();
    private DispatcherTimer? _previewThrottleTimer;
    private DispatcherTimer? _blackoutSafetyTimer;
    private Slider? _blackoutSlider;
    private string? _blackoutPreviewKind;
    private bool _changingBrightnessForSafety;
    private bool _allowTemporaryBlackout;

    private readonly Dictionary<string, CheckBox> _excludeBoxes = new();
    private readonly Dictionary<string, CheckBox> _colorExcludeBoxes = new();
    private readonly Dictionary<string, TextBox> _multiplierBoxes = new();
    private readonly Dictionary<string, TextBox> _kelvinOffsetBoxes = new();
    private readonly Dictionary<string, CheckBox> _hardwareBrightnessBoxes = new();
    private readonly List<ApplicationRuleRow> _applicationRuleRows = new();

    private uint _pendingHotkeyModifiers;
    private uint _pendingHotkeyKey;
    private bool _loaded;
    private bool _locationConfirmed;          // mirrors AppSettings.LocationConfirmed, plus an in-window "London is correct"
    private bool _londonConfirmedThisSession;

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
        _targetSettings = settings;
        _settings = settings.Clone();
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
            RestoreBlackoutPreview();
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
        BuildApplicationRuleRows();
        RefreshProfilesComboBox();

        LoadWorldMapImage();
        HistoryTrackingCheckBox.IsChecked = _settings.HistoryTrackingEnabled;
        MatchAmbientLightCheckBox.IsChecked = _settings.MatchAmbientLight;
        BreakReminderCheckBox.IsChecked = _settings.BreakReminderEnabled;
        BreakReminderIntervalSlider.Value = _settings.BreakReminderIntervalMinutes;
        FullscreenPresentationGuardCheckBox.IsChecked = _settings.RestoreNativeDisplayInFullscreen;
        CompatibilityOverlayOnlyCheckBox.IsChecked = _settings.PreferOverlayOnlyOnCompatibilityDisplays;
        // HistoryTrackingCheckBox's own Checked/Unchecked handler (fired by setting its
        // IsChecked above) already set PromptForRatingCheckBox.IsEnabled to match — this just
        // loads the actual saved value into it.
        PromptForRatingCheckBox.IsChecked = _settings.PromptForMigraineRating;
        AmbientLightAvailabilityText.Text = AmbientLightSensor.IsAvailable
            ? "An ambient light sensor was found on this device."
            : "No ambient light sensor was found on this device — most desktops don't have one (it's mostly a laptop/tablet feature), so this option will have no effect here even if turned on.";
        CheckForUpdatesCheckBox.IsChecked = _settings.CheckForUpdatesEnabled;
        ExcludeFromCaptureCheckBox.IsChecked = _settings.ExcludeOverlayFromCapture;
        CheckForPreReleasesCheckBox.IsChecked = _settings.UpdateNotifyPreReleases;
        var runningVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        CurrentVersionText.Text = runningVersion is not null ? $"You're running version {runningVersion.ToString(3)}." : "";
        LoadScheduleSourceFrom(_settings);
        _loaded = true;
        _locationConfirmed = _settings.LocationConfirmed;
        UpdateMapMarker();
        UpdateSunTimesDisplay();
        UpdateLocationBanner();
        UpdateScheduleModeUi();
        UpdateSliderLabels();
        RefreshHistorySummary();
        PreviewDay(); // something visible on screen the moment the window opens, matching whichever phase was last touched (day, as the default starting point)
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

        try
        {
            SettingsStore.Save(_settings);
            _targetSettings.CopyFrom(_settings);
            _onSaved();
            Close();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            System.Windows.MessageBox.Show(this, $"Couldn't save settings: {ex.Message}", "Monitor Wellness", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool TryParseAll(out string error)
    {
        error = "";

        if (!double.TryParse(LatitudeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double lat) || !double.IsFinite(lat) || lat < -90 || lat > 90)
        {
            error = "Latitude must be a number between -90 and 90.";
            return false;
        }
        if (!double.TryParse(LongitudeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double lon) || !double.IsFinite(lon) || lon < -180 || lon > 180)
        {
            error = "Longitude must be a number between -180 and 180.";
            return false;
        }
        if (!TryValidateFixedTimes(out string scheduleMode, out string fixedSunrise, out string fixedSunset, out error))
            return false;
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
        // A 0% value can be previewed briefly after explicit confirmation, but cannot be
        // saved: otherwise the next scheduled tick could leave the display black with no
        // usable visual recovery path. The same floor applies to deep night.
        double dayBrightness = DayBrightnessSlider.Value;
        double nightBrightness = NightBrightnessSlider.Value;
        double deepNightBrightness = DeepNightBrightnessSlider.Value;
        if (dayBrightness < AppSettingsValidator.MinimumSafeBrightness
            || nightBrightness < AppSettingsValidator.MinimumSafeBrightness
            || deepNightBrightness < AppSettingsValidator.MinimumSafeBrightness)
        {
            error = $"Brightness settings must be at least {AppSettingsValidator.MinimumSafeBrightness:P0}. You can preview 0% temporarily, but it cannot be saved.";
            return false;
        }
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
        TryValidateWakeTime(out string? wakeLocal, out _);
        if (!TryGetMigraineResponsePlan(out string defaultMigraineResponsePlan, out error))
            return false;

        var multipliers = new Dictionary<string, double>();
        foreach (var (deviceName, box) in _multiplierBoxes)
        {
            if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double multiplier)
                || !double.IsFinite(multiplier) || multiplier < 0 || multiplier > 5)
            {
                error = $"Dim multiplier for {ShortDeviceName(deviceName)} must be a finite number between 0 and 5.";
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

        var applicationRules = new List<ApplicationComfortRule>();
        var configuredRuleContexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ApplicationRuleRow row in _applicationRuleRows)
        {
            if (!ApplicationComfortRules.TryNormalizeProcessName(row.ProcessNameBox.Text, out string processName))
            {
                error = "Application rules need an executable name only, e.g. photoshop.exe or vlc.";
                return false;
            }
            string? windowTitleContains = string.IsNullOrWhiteSpace(row.WindowTitleBox.Text) ? null : row.WindowTitleBox.Text.Trim();
            if (!configuredRuleContexts.Add($"{processName}\u001f{windowTitleContains ?? ""}"))
            {
                error = $"There is already an application rule for {processName} with the same window-title condition.";
                return false;
            }

            applicationRules.Add(new ApplicationComfortRule
            {
                ProcessName = processName,
                WindowTitleContains = windowTitleContains,
                Action = (row.ActionBox.SelectedItem as ComboBoxItem)?.Tag as string
                    ?? ApplicationComfortActions.RestoreNativeDisplay,
                ComfortPlanName = string.Equals((row.ActionBox.SelectedItem as ComboBoxItem)?.Tag as string,
                    ApplicationComfortActions.ApplySensoryComfortPlan, StringComparison.Ordinal)
                    ? (row.ComfortPlanBox.SelectedItem as ComboBoxItem)?.Tag as string
                    : null,
                IsEnabled = row.EnabledBox.IsChecked == true,
            });
        }

        // All parsed successfully — commit to the live settings object.
        _settings.Latitude = lat;
        _settings.Longitude = lon;
        _settings.LocationConfirmed = LocationStatus.ConfirmedAfterSave(_settings.LocationConfirmed, _londonConfirmedThisSession, lat, lon);
        _settings.ScheduleMode = scheduleMode;
        _settings.FixedSunriseLocal = fixedSunrise;
        _settings.FixedSunsetLocal = fixedSunset;
        _settings.DayKelvin = dayKelvin;
        _settings.NightKelvin = nightKelvin;
        _settings.DayBrightness = dayBrightness;
        _settings.NightBrightness = nightBrightness;
        _settings.DeepNightBrightness = deepNightBrightness;
        _settings.DeepNightOverlayColorHex = deepNightColorHex;
        _settings.MigraineOverlayColorHex = migraineColorHex;
        _settings.MigraineOverlayOpacity = migraineOpacity;
        _settings.MigraineContrastReduction = MigraineContrastSlider.Value;
        _settings.DefaultMigraineResponsePlan = defaultMigraineResponsePlan;
        _settings.MigraineAutoRevertMinutes = (int)MigraineAutoRevertSlider.Value;
        _settings.PlaySoundOnMigraineToggle = PlaySoundCheckBox.IsChecked == true;
        _settings.BedtimeLocal = bedtimeLocal;
        _settings.WakeTimeLocal = wakeLocal;
        _settings.MigraineHotkeyModifiers = _pendingHotkeyModifiers;
        _settings.MigraineHotkeyKey = _pendingHotkeyKey;
        _settings.MonitorDimMultiplier = multipliers;
        _settings.MonitorKelvinOffset = kelvinOffsets;
        _settings.ApplicationComfortRules = applicationRules;
        var activeMonitorsByDeviceName = MonitorEnumerator.GetActiveMonitors()
            .ToDictionary(monitor => monitor.DeviceName, StringComparer.OrdinalIgnoreCase);
        foreach (var (deviceName, hardwareBox) in _hardwareBrightnessBoxes)
        {
            if (activeMonitorsByDeviceName.TryGetValue(deviceName, out MonitorInfo? monitor)
                && HardwareBrightnessSafety.GetKey(monitor) is string key
                && _settings.HardwareBrightnessSafetyByMonitor.TryGetValue(key, out HardwareBrightnessSafetyState? state)
                && !state.IsQuarantined)
            {
                state.IsApproved = hardwareBox.IsChecked == true;
            }
        }

        // Legacy device-name approvals are unsafe once that display has been seen by this
        // version. New hardware approvals are persisted only against physical identities.
        _settings.HardwareBrightnessEnabledMonitors.RemoveAll(_hardwareBrightnessBoxes.ContainsKey);
        _settings.HistoryTrackingEnabled = HistoryTrackingCheckBox.IsChecked == true;
        _settings.PromptForMigraineRating = PromptForRatingCheckBox.IsChecked == true;
        _settings.MatchAmbientLight = MatchAmbientLightCheckBox.IsChecked == true;
        _settings.BreakReminderEnabled = BreakReminderCheckBox.IsChecked == true;
        _settings.BreakReminderIntervalMinutes = (int)BreakReminderIntervalSlider.Value;
        _settings.RestoreNativeDisplayInFullscreen = FullscreenPresentationGuardCheckBox.IsChecked == true;
        _settings.PreferOverlayOnlyOnCompatibilityDisplays = CompatibilityOverlayOnlyCheckBox.IsChecked == true;
        _settings.CheckForUpdatesEnabled = CheckForUpdatesCheckBox.IsChecked == true;
        _settings.ExcludeOverlayFromCapture = ExcludeFromCaptureCheckBox.IsChecked == true;
        _settings.UpdateNotifyPreReleases = CheckForPreReleasesCheckBox.IsChecked == true;
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
