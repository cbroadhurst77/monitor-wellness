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

/// <summary>Loading preferences into the controls, comfort plans and the cooler/warmer presets. (part of the settings window; see SettingsWindow.xaml.cs.)</summary>
public partial class SettingsWindow
{
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
        SelectMigraineResponsePlan(source.DefaultMigraineResponsePlan);
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

    private void PreviewBuiltInComfortPlanButton_Click(object sender, RoutedEventArgs e)
    {
        string? plan = (BuiltInComfortPlanComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        if (string.IsNullOrWhiteSpace(plan))
            return;

        AppSettings preview = _settings.Clone();
        if (!SensoryComfortPlans.Apply(plan, preview))
            return;

        // This only updates the settings-window draft controls. Save remains the explicit
        // commit boundary; Cancel abandons the preview and App reapplies the real schedule.
        LoadPreferencesFrom(preview);
        UpdateSliderLabels();
        PreviewDay();
    }

    private void SelectMigraineResponsePlan(string plan)
    {
        foreach (ComboBoxItem item in MigraineDefaultPlanComboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, plan, StringComparison.Ordinal))
            {
                MigraineDefaultPlanComboBox.SelectedItem = item;
                return;
            }
        }

        MigraineDefaultPlanComboBox.SelectedIndex = 1; // Strong is the safe backwards-compatible default.
    }

    private bool TryGetMigraineResponsePlan(out string plan, out string error)
    {
        plan = (MigraineDefaultPlanComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        error = MigraineResponsePlans.IsSupported(plan)
            ? ""
            : "Choose a valid default migraine response.";
        return string.IsNullOrEmpty(error);
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
}
