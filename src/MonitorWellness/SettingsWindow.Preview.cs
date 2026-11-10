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

/// <summary>Live preview while sliders move, the zero-brightness (blackout) safety, bedtime/deep-night/break-reminder controls and migraine preview. (part of the settings window; see SettingsWindow.xaml.cs.)</summary>
public partial class SettingsWindow
{
    private void DaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_loaded) return;
        if (HandleZeroBrightness((Slider)sender, e, "day")) return;
        UpdateSliderLabels();
        QueuePreview("day");
    }

    private void NightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_loaded) return;
        if (HandleZeroBrightness((Slider)sender, e, "night")) return;
        UpdateSliderLabels();
        QueuePreview("night");
    }

    /// <summary>
    /// Intercepts a zero-brightness live preview before an overlay can make the screen black.
    /// A deliberate preview is time-limited, and Escape/closing Settings restores a visible level.
    /// </summary>
    private bool HandleZeroBrightness(Slider slider, RoutedPropertyChangedEventArgs<double> e, string previewKind)
    {
        if (e.NewValue > 0)
        {
            if (ReferenceEquals(_blackoutSlider, slider))
                StopBlackoutSafetyTimer();
            return false;
        }

        if (_changingBrightnessForSafety || _allowTemporaryBlackout)
            return false;

        SetBrightnessForSafety(slider, Math.Max(e.OldValue, SafePreviewBrightness));
        PreviewSchedule(previewKind);

        var confirmation = new BlackoutPreviewDialog { Owner = this };
        confirmation.ShowDialog();
        if (!confirmation.ShouldPreviewBlackout)
            return true;

        _blackoutSlider = slider;
        _blackoutPreviewKind = previewKind;
        _allowTemporaryBlackout = true;
        slider.Value = 0;
        _allowTemporaryBlackout = false;
        StartBlackoutSafetyTimer();
        return true;
    }

    private void SetBrightnessForSafety(Slider slider, double brightness)
    {
        _changingBrightnessForSafety = true;
        slider.Value = Math.Clamp(brightness, AppSettingsValidator.MinimumSafeBrightness, slider.Maximum);
        _changingBrightnessForSafety = false;
    }

    private void StartBlackoutSafetyTimer()
    {
        _blackoutSafetyTimer?.Stop();
        _blackoutSafetyTimer = new DispatcherTimer { Interval = BlackoutPreviewDuration };
        _blackoutSafetyTimer.Tick += (_, _) => RestoreBlackoutPreview();
        _blackoutSafetyTimer.Start();
    }

    private void StopBlackoutSafetyTimer()
    {
        _blackoutSafetyTimer?.Stop();
        _blackoutSafetyTimer = null;
        _blackoutSlider = null;
        _blackoutPreviewKind = null;
    }

    private void RestoreBlackoutPreview()
    {
        Slider? slider = _blackoutSlider;
        string? previewKind = _blackoutPreviewKind;
        StopBlackoutSafetyTimer();
        if (slider is null || previewKind is null)
            return;

        try
        {
            SetBrightnessForSafety(slider, SafePreviewBrightness);
            PreviewSchedule(previewKind);
        }
        catch (Exception ex)
        {
            // This runs from the window's Closed handler (among other paths), registered
            // before App's own Closed handler that clears _settingsPreviewActive and resumes
            // the schedule. C# multicast delegates stop at the first handler that throws — an
            // exception here would silently skip App's cleanup and leave the normal schedule
            // suspended for the rest of the session, so this must never throw out.
            DebugLog.Write($"RestoreBlackoutPreview failed: {ex}");
        }
    }

    private void SettingsWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _blackoutSlider is not null)
        {
            RestoreBlackoutPreview();
            e.Handled = true;
        }
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
        WakeTimeBox.IsEnabled = BedtimeBox.IsEnabled;
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
        if (valid)
            valid = TryValidateWakeTime(out _, out error);
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

    private void PreviewSchedule(string previewKind)
    {
        if (previewKind == "day") PreviewDay();
        else PreviewNight();
    }

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
                controller.RestoreBaseline();
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
        var primaryMonitorNames = MonitorEnumerator.GetActiveMonitors()
            .Where(monitor => monitor.IsPrimary)
            .Select(monitor => monitor.DeviceName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var multipliers = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var deviceName in _overlay.DeviceNames)
        {
            if (_excludeBoxes.TryGetValue(deviceName, out var box) && box.IsChecked == true)
                excluded.Add(deviceName);

            double multiplier = 1.0;
            if (_multiplierBoxes.TryGetValue(deviceName, out var multiplierBox))
                double.TryParse(multiplierBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out multiplier);
            multipliers[deviceName] = multiplier;
        }

        // Same shared builder as App.ComputeScheduleTarget, so preview and the real schedule
        // cover the same overlay-backed device set regardless of gamma-controller availability.
        return ScheduleBrightnessTargets.Build(_overlay.DeviceNames, globalBrightness, excluded, multipliers, primaryMonitorNames);
    }
}
