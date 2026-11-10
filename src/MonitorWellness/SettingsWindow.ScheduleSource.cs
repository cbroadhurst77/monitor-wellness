using System.Windows;
using System.Windows.Controls;
using MonitorWellness.Core;

namespace MonitorWellness;

/// <summary>What drives the day/night schedule (the sun at a location, or two fixed clock times) and the wake-up time next to bedtime. Part of the settings window; see SettingsWindow.xaml.cs.</summary>
public partial class SettingsWindow
{
    private void ScheduleMode_Changed(object sender, RoutedEventArgs e)
    {
        if (_loaded)
            UpdateScheduleModeUi();
    }

    private void FixedTimeBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loaded)
            UpdateFixedTimesWarning();
    }

    private void WakeTimeBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loaded)
            UpdateBedtimeWarning();
    }

    /// <summary>Puts a settings object's schedule source into the controls (used on load and after an import).</summary>
    private void LoadScheduleSourceFrom(AppSettings source)
    {
        bool fixedMode = string.Equals(source.ScheduleMode, ScheduleModes.FixedTimes, StringComparison.Ordinal);
        ScheduleModeFixedRadio.IsChecked = fixedMode;
        ScheduleModeSunRadio.IsChecked = !fixedMode;
        FixedSunriseBox.Text = source.FixedSunriseLocal;
        FixedSunsetBox.Text = source.FixedSunsetLocal;
    }

    /// <summary>Shows the clock-time fields only in fixed-time mode, and hides the "location not set" notice when no location is needed.</summary>
    private void UpdateScheduleModeUi()
    {
        FixedTimesPanel.Visibility = ScheduleModeFixedRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        UpdateFixedTimesWarning();
        UpdateLocationBanner();
    }

    private bool UpdateFixedTimesWarning()
    {
        string? error = ScheduleModeFixedRadio.IsChecked == true
            ? FixedTimeSchedule.Validate(FixedSunriseBox.Text, FixedSunsetBox.Text)
            : null;

        FixedTimesWarning.Text = error ?? "";
        FixedTimesWarning.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        return error is null;
    }

    /// <summary>
    /// Reads the schedule source. In sun mode the clock-time boxes are hidden, so whatever they hold
    /// is not the person's problem: junk there falls back to the defaults so the saved settings stay valid.
    /// </summary>
    private bool TryValidateFixedTimes(out string mode, out string sunrise, out string sunset, out string error)
    {
        bool fixedMode = ScheduleModeFixedRadio.IsChecked == true;
        mode = fixedMode ? ScheduleModes.FixedTimes : ScheduleModes.Sun;
        sunrise = FixedSunriseBox.Text.Trim();
        sunset = FixedSunsetBox.Text.Trim();

        string? message = FixedTimeSchedule.Validate(sunrise, sunset);
        if (!fixedMode)
        {
            if (message is not null)
            {
                sunrise = FixedTimeSchedule.DefaultSunrise;
                sunset = FixedTimeSchedule.DefaultSunset;
            }
            error = "";
            return true;
        }

        error = message ?? "";
        return message is null;
    }

    /// <summary>
    /// Reads the optional wake-up time. It only means something alongside a bedtime, so with bedtime
    /// off it is simply null. Blank means "not set" (the ten-hour default after bedtime).
    /// </summary>
    private bool TryValidateWakeTime(out string? wakeLocal, out string error)
    {
        wakeLocal = null;
        error = "";
        if (!TryValidateBedtime(out string? bedtimeLocal, out error))
            return false;
        if (bedtimeLocal is null)
            return true;

        string text = WakeTimeBox.Text.Trim();
        if (text.Length == 0)
            return true;

        if (!FixedTimeSchedule.TryParseTimeOfDay(text, out TimeSpan wake))
        {
            error = "Wake-up time must be in HH:mm format, e.g. 06:30, or left blank.";
            return false;
        }
        if (FixedTimeSchedule.TryParseTimeOfDay(bedtimeLocal, out TimeSpan bedtime) && wake == bedtime)
        {
            error = "Wake-up time must be different from bedtime.";
            return false;
        }

        wakeLocal = text;
        return true;
    }
}
