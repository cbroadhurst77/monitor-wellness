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

/// <summary>Export, import and reset of settings. (part of the settings window; see SettingsWindow.xaml.cs.)</summary>
public partial class SettingsWindow
{
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
        _settings.LocationConfirmed = imported.LocationConfirmed;
        _settings.ScheduleMode = imported.ScheduleMode;
        _settings.FixedSunriseLocal = imported.FixedSunriseLocal;
        _settings.FixedSunsetLocal = imported.FixedSunsetLocal;
        _settings.ExcludedMonitors = imported.ExcludedMonitors;
        _settings.ColorExcludedMonitors = imported.ColorExcludedMonitors;
        _settings.MonitorDimMultiplier = imported.MonitorDimMultiplier;
        _settings.MonitorKelvinOffset = imported.MonitorKelvinOffset;
        _settings.HistoryTrackingEnabled = imported.HistoryTrackingEnabled;
        _settings.BreakHistoryEnabled = imported.BreakHistoryEnabled;
        _settings.FollowScheduleTheme = imported.FollowScheduleTheme;
        _settings.PromptForMigraineRating = imported.PromptForMigraineRating;
        _settings.MatchAmbientLight = imported.MatchAmbientLight;
        _settings.BreakReminderEnabled = imported.BreakReminderEnabled;
        _settings.BreakReminderIntervalMinutes = imported.BreakReminderIntervalMinutes;
        _settings.BreakReminderHoldDuringCalls = imported.BreakReminderHoldDuringCalls;
        _settings.BreakReminderLimitToHours = imported.BreakReminderLimitToHours;
        _settings.BreakReminderFromLocal = imported.BreakReminderFromLocal;
        _settings.BreakReminderToLocal = imported.BreakReminderToLocal;
        _settings.BreakReminderStyle = imported.BreakReminderStyle;
        _settings.BreakReminderSound = imported.BreakReminderSound;
        _settings.BreakFocusSeconds = imported.BreakFocusSeconds;
        _settings.StandUpReminderEnabled = imported.StandUpReminderEnabled;
        _settings.StandUpIntervalMinutes = imported.StandUpIntervalMinutes;
        _settings.RestoreNativeDisplayInFullscreen = imported.RestoreNativeDisplayInFullscreen;
        _settings.CheckForUpdatesEnabled = imported.CheckForUpdatesEnabled;
        _settings.ExcludeOverlayFromCapture = imported.ExcludeOverlayFromCapture;
        _settings.UpdateNotifyPreReleases = imported.UpdateNotifyPreReleases;

        LatitudeBox.Text = _settings.Latitude.ToString(CultureInfo.InvariantCulture);
        LongitudeBox.Text = _settings.Longitude.ToString(CultureInfo.InvariantCulture);
        _locationConfirmed = _settings.LocationConfirmed;
        LoadScheduleSourceFrom(_settings);
        UpdateScheduleModeUi();
        LoadPreferencesFrom(imported);
        BuildMonitorRows();
        HistoryTrackingCheckBox.IsChecked = _settings.HistoryTrackingEnabled;
        BreakHistoryCheckBox.IsChecked = _settings.BreakHistoryEnabled;
        FollowThemeCheckBox.IsChecked = _settings.FollowScheduleTheme;
        PromptForRatingCheckBox.IsChecked = _settings.PromptForMigraineRating;
        MatchAmbientLightCheckBox.IsChecked = _settings.MatchAmbientLight;
        BreakReminderCheckBox.IsChecked = _settings.BreakReminderEnabled;
        BreakReminderIntervalSlider.Value = _settings.BreakReminderIntervalMinutes;
        LoadBreakExtras(_settings);
        FullscreenPresentationGuardCheckBox.IsChecked = _settings.RestoreNativeDisplayInFullscreen;
        CheckForUpdatesCheckBox.IsChecked = _settings.CheckForUpdatesEnabled;
        ExcludeFromCaptureCheckBox.IsChecked = _settings.ExcludeOverlayFromCapture;
        CheckForPreReleasesCheckBox.IsChecked = _settings.UpdateNotifyPreReleases;

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
}
