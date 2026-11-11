#pragma warning disable CA1861 // small literal arrays in tests are clearer than shared static fields
using System.Reflection;
using System.Windows.Controls;
using MonitorWellness.Core;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using Color = System.Windows.Media.Color;

namespace MonitorWellness.Tests;

/// <summary>
/// The real Settings window, opened over fake displays. Until the display pieces became interfaces the
/// only way to check this window was to open it on the real screens (which tints them), so every change
/// to it was verified by hand once and then not again. The window is constructed but never shown: its
/// controls and its save validation can be read without that, and nothing appears on screen.
/// </summary>
public class SettingsWindowFakeTests
{
    private sealed class FakeGamma : IGammaController
    {
        public FakeGamma(string name) => DeviceName = name;
        public string DeviceName { get; }
        public int LastWin32Error => 0;
        public int ColourWrites { get; private set; }
        public bool ApplyColorTemperature(int kelvin, double brightnessAssist = 1.0) { ColourWrites++; return true; }
        public bool ApplyColorTemperatureWithContrast(int kelvin, double contrastReduction) { ColourWrites++; return true; }
        public bool RestoreBaseline() => true;
        public bool RestoreBaselineIfModified() => true;
    }

    private sealed class FakeGammaSource : IGammaSource
    {
        public List<FakeGamma> Fakes { get; } = new() { new FakeGamma(@"\\.\DISPLAY1"), new FakeGamma(@"\\.\DISPLAY2") };
        public IReadOnlyCollection<IGammaController> Controllers => Fakes;
        public void RestoreAllToBaseline() { }
    }

    private sealed class FakeOverlay : IPreviewOverlay
    {
        public IReadOnlyCollection<string> DeviceNames { get; } = new[] { @"\\.\DISPLAY1", @"\\.\DISPLAY2" };
        public int DimCalls { get; private set; }
        public int TintCalls { get; private set; }
        public void ApplyDim(IReadOnlyDictionary<string, double> brightnessByDevice, Color dimColor) => DimCalls++;
        public void Apply(IReadOnlyDictionary<string, (Color Color, double Opacity)> byDevice) => TintCalls++;
        public void Clear() { }
        public void IdentifyMonitors(TimeSpan duration) { }
    }

    private static void OnStaThread(Action<SettingsWindow, FakeGammaSource, FakeOverlay> body, AppSettings settings)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var gamma = new FakeGammaSource();
                var overlay = new FakeOverlay();
                var window = new SettingsWindow(settings, gamma, overlay, () => { }, () => "test");
                body(window, gamma, overlay);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw new InvalidOperationException("The window test failed on its UI thread.", failure);
    }

    private static T Find<T>(SettingsWindow window, string name) where T : class
        => (window.FindName(name) as T) ?? throw new InvalidOperationException($"No control named {name}.");

    private static AppSettings DraftOf(SettingsWindow window)
        => (AppSettings)typeof(SettingsWindow).GetField("_settings", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;

    private static bool Parse(SettingsWindow window, out string error)
    {
        var args = new object?[] { "" };
        bool ok = (bool)typeof(SettingsWindow).GetMethod("TryParseAll", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, args)!;
        error = (string)args[0]!;
        return ok;
    }

    private static AppSettings FullySetUp() => new()
    {
        BreakReminderEnabled = true, BreakReminderIntervalMinutes = 25,
        BreakReminderHoldDuringCalls = false, BreakReminderLimitToHours = true,
        BreakReminderFromLocal = "09:30", BreakReminderToLocal = "17:45",
        BreakReminderStyle = BreakReminderStyles.Notification, BreakReminderSound = true, BreakFocusSeconds = 45,
        StandUpReminderEnabled = true, StandUpIntervalMinutes = 90,
        BreakHistoryEnabled = true, FollowScheduleTheme = true,
    };

    [Fact]
    public void Opening_the_window_previews_only_on_the_fakes()
    {
        OnStaThread((window, gamma, overlay) =>
        {
            // The window shows a preview of the day colours the moment it opens; here that went to the fakes.
            Assert.True(gamma.Fakes.Sum(fake => fake.ColourWrites) > 0);
            Assert.True(overlay.DimCalls > 0);
        }, new AppSettings());
    }

    [Fact]
    public void Every_saved_reminder_option_is_shown_in_its_control()
    {
        OnStaThread((window, _, _) =>
        {
            Assert.True(Find<CheckBox>(window, "BreakReminderCheckBox").IsChecked);
            Assert.False(Find<CheckBox>(window, "BreakHoldDuringCallsCheckBox").IsChecked);
            Assert.True(Find<CheckBox>(window, "BreakLimitHoursCheckBox").IsChecked);
            Assert.Equal("09:30", Find<TextBox>(window, "BreakFromBox").Text);
            Assert.Equal("17:45", Find<TextBox>(window, "BreakToBox").Text);
            Assert.Equal(BreakReminderStyles.Notification, (Find<ComboBox>(window, "BreakStyleComboBox").SelectedItem as ComboBoxItem)?.Tag);
            Assert.True(Find<CheckBox>(window, "BreakSoundCheckBox").IsChecked);
            Assert.Equal(45, Find<Slider>(window, "BreakFocusSecondsSlider").Value);
            Assert.Equal("45 seconds", Find<TextBlock>(window, "BreakFocusSecondsLabel").Text);
            Assert.True(Find<CheckBox>(window, "StandUpEnabledCheckBox").IsChecked);
            Assert.Equal(90, Find<Slider>(window, "StandUpIntervalSlider").Value);
            Assert.Equal("90 minutes", Find<TextBlock>(window, "StandUpIntervalLabel").Text);
            Assert.True(Find<CheckBox>(window, "BreakHistoryCheckBox").IsChecked);
            Assert.True(Find<CheckBox>(window, "FollowThemeCheckBox").IsChecked);
        }, FullySetUp());
    }

    [Fact]
    public void Saving_reads_every_option_back_unchanged_and_the_result_is_valid()
    {
        OnStaThread((window, _, _) =>
        {
            Assert.True(Parse(window, out string error), error);

            AppSettings draft = DraftOf(window);
            Assert.False(draft.BreakReminderHoldDuringCalls);
            Assert.True(draft.BreakReminderLimitToHours);
            Assert.Equal("09:30", draft.BreakReminderFromLocal);
            Assert.Equal("17:45", draft.BreakReminderToLocal);
            Assert.Equal(BreakReminderStyles.Notification, draft.BreakReminderStyle);
            Assert.True(draft.BreakReminderSound);
            Assert.Equal(45, draft.BreakFocusSeconds);
            Assert.True(draft.StandUpReminderEnabled);
            Assert.Equal(90, draft.StandUpIntervalMinutes);
            Assert.True(draft.BreakHistoryEnabled);
            Assert.True(draft.FollowScheduleTheme);
            Assert.True(AppSettingsValidator.TryValidate(draft, out string why), why);
        }, FullySetUp());
    }

    [Fact]
    public void A_bad_reminder_time_is_refused_only_while_the_hours_option_is_on()
    {
        OnStaThread((window, _, _) =>
        {
            Find<TextBox>(window, "BreakFromBox").Text = "soon";

            Assert.False(Parse(window, out string error));
            Assert.Contains("HH:mm", error);

            Find<CheckBox>(window, "BreakLimitHoursCheckBox").IsChecked = false;
            Assert.True(Parse(window, out _));
            Assert.Equal("09:30", DraftOf(window).BreakReminderFromLocal); // the unreadable box did not overwrite the saved time
        }, FullySetUp());
    }

    [Fact]
    public void The_comfort_plan_to_start_each_day_with_is_shown_saved_and_can_be_none()
    {
        OnStaThread((window, _, _) =>
        {
            var box = Find<ComboBox>(window, "DefaultComfortPlanComboBox");
            Assert.Equal("Reading", (box.SelectedItem as ComboBoxItem)?.Tag);

            Assert.True(Parse(window, out string error), error);
            Assert.Equal("Reading", DraftOf(window).DefaultComfortPlan);

            box.SelectedIndex = 0; // "None"
            Assert.True(Parse(window, out _));
            Assert.Equal("", DraftOf(window).DefaultComfortPlan);
        }, new AppSettings { DefaultComfortPlan = "Reading" });
    }

    [Fact]
    public void Turning_reminders_off_greys_out_what_depends_on_them()
    {
        OnStaThread((window, _, _) =>
        {
            Find<CheckBox>(window, "BreakReminderCheckBox").IsChecked = false;

            Assert.False(Find<CheckBox>(window, "StandUpEnabledCheckBox").IsEnabled);
            Assert.False(Find<ComboBox>(window, "BreakStyleComboBox").IsEnabled);
            Assert.False(Find<Slider>(window, "BreakFocusSecondsSlider").IsEnabled);
            Assert.False(Find<Slider>(window, "StandUpIntervalSlider").IsEnabled);
        }, FullySetUp());
    }
}
