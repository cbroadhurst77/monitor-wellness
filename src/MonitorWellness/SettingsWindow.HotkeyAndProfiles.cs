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

/// <summary>Hotkey capture, input validation and saved profiles. (part of the settings window; see SettingsWindow.xaml.cs.)</summary>
public partial class SettingsWindow
{
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
        if (!TryGetMigraineResponsePlan(out string defaultMigraineResponsePlan, out error))
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
        snapshot.DefaultMigraineResponsePlan = defaultMigraineResponsePlan;
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

        try
        {
            ProfileStore.Save(dialog.ProfileName, snapshot);
            RefreshProfilesComboBox(dialog.ProfileName);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            System.Windows.MessageBox.Show(this, $"Couldn't save profile: {ex.Message}", "Monitor Wellness", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesComboBox.SelectedItem is not string name)
            return;

        var result = System.Windows.MessageBox.Show(this, $"Delete profile \"{name}\"?", "Monitor Wellness", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        if (!ProfileStore.Delete(name))
        {
            System.Windows.MessageBox.Show(this, $"Couldn't delete profile \"{name}\" — it may be open elsewhere or read-only.", "Monitor Wellness", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        RefreshProfilesComboBox();
    }
}
