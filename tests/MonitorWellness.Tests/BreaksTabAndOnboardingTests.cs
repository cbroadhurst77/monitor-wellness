#pragma warning disable CA1861 // small literal arrays in tests are clearer than shared static fields
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using MonitorWellness.Core;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Color = System.Windows.Media.Color;
using TabControl = System.Windows.Controls.TabControl;

namespace MonitorWellness.Tests;

public class OnboardingChoicesTests
{
    [Fact]
    public void Choosing_nothing_changes_nothing()
    {
        var settings = new AppSettings();

        Assert.False(OnboardingChoices.Apply(settings, wantsBreakReminders: false, defaultComfortPlan: ""));
        Assert.False(settings.BreakReminderEnabled);
        Assert.Equal("", settings.DefaultComfortPlan);
    }

    [Fact]
    public void A_ticked_box_and_a_chosen_plan_are_applied()
    {
        var settings = new AppSettings();

        Assert.True(OnboardingChoices.Apply(settings, true, "Reading"));

        Assert.True(settings.BreakReminderEnabled);
        Assert.Equal("Reading", settings.DefaultComfortPlan);
    }

    [Fact]
    public void Each_choice_stands_alone_and_an_unknown_plan_is_ignored()
    {
        var onlyReminders = new AppSettings();
        Assert.True(OnboardingChoices.Apply(onlyReminders, true, null));
        Assert.Equal("", onlyReminders.DefaultComfortPlan);

        var onlyPlan = new AppSettings();
        Assert.True(OnboardingChoices.Apply(onlyPlan, false, "Recovery"));
        Assert.False(onlyPlan.BreakReminderEnabled);

        var unknown = new AppSettings();
        Assert.False(OnboardingChoices.Apply(unknown, false, "Turbo"));
        Assert.Equal("", unknown.DefaultComfortPlan);
    }

    [Fact]
    public void Nothing_already_set_is_switched_off_or_overwritten_by_leaving_the_boxes_alone()
    {
        var settings = new AppSettings { BreakReminderEnabled = true, DefaultComfortPlan = "Balanced" };

        Assert.False(OnboardingChoices.Apply(settings, wantsBreakReminders: false, defaultComfortPlan: ""));

        Assert.True(settings.BreakReminderEnabled);
        Assert.Equal("Balanced", settings.DefaultComfortPlan);
    }

    [Fact]
    public void Choices_that_are_already_in_place_report_no_change()
    {
        var settings = new AppSettings { BreakReminderEnabled = true, DefaultComfortPlan = "Reading" };

        Assert.False(OnboardingChoices.Apply(settings, true, "Reading"));
    }
}

public class OnboardingWindowTests
{
    private static void OnStaThread(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw new InvalidOperationException("The window test failed on its UI thread.", failure);
    }

    private static void Click(OnboardingWindow window, string buttonName)
        => ((Button)window.FindName(buttonName)).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

    [Fact]
    public void The_welcome_has_a_breaks_and_comfort_step_before_the_last_one()
    {
        OnStaThread(() =>
        {
            var window = new OnboardingWindow(() => { });
            var progress = (TextBlock)window.FindName("StepProgressText");
            var title = (TextBlock)window.FindName("StepTitleText");

            Assert.Equal("Step 1 of 5", progress.Text);
            for (int i = 0; i < 3; i++)
                Click(window, "NextButton");

            Assert.Equal("Step 4 of 5", progress.Text);
            Assert.Equal("Breaks and comfort", title.Text);
            Assert.Equal(Visibility.Visible, ((StackPanel)window.FindName("StepBreaksPanel")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((StackPanel)window.FindName("Step4Panel")).Visibility);
            Assert.False(((CheckBox)window.FindName("OnboardingRemindersCheckBox")).IsChecked);          // opt-in: off until chosen
            Assert.Equal("", ((ComboBoxItem)((ComboBox)window.FindName("OnboardingPlanComboBox")).SelectedItem).Tag);

            Click(window, "NextButton");
            Assert.Equal("Step 5 of 5", progress.Text);
            Assert.Equal(Visibility.Visible, ((StackPanel)window.FindName("Step4Panel")).Visibility);
        });
    }

    [Fact]
    public void Skipping_the_welcome_without_choosing_applies_nothing_beyond_the_defaults()
    {
        OnStaThread(() =>
        {
            var received = new List<(bool Reminders, string? Plan)>();
            var window = new OnboardingWindow(() => { }, (reminders, plan) => received.Add((reminders, plan)));
            for (int i = 0; i < 4; i++)
                Click(window, "NextButton");

            Click(window, "SkipButton");

            Assert.Equal((false, ""), Assert.Single(received));
        });
    }

    [Fact]
    public void The_choices_arrive_once_and_before_settings_opens_from_the_set_location_button()
    {
        OnStaThread(() =>
        {
            var order = new List<string>();
            var window = new OnboardingWindow(() => order.Add("settings opened"), (reminders, plan) => order.Add($"choices {reminders} {plan}"));
            for (int i = 0; i < 3; i++)
                Click(window, "NextButton");
            ((CheckBox)window.FindName("OnboardingRemindersCheckBox")).IsChecked = true;
            var plans = (ComboBox)window.FindName("OnboardingPlanComboBox");
            plans.SelectedItem = plans.Items.OfType<ComboBoxItem>().Single(item => (string?)item.Tag == "Reading");
            Click(window, "NextButton");

            Click(window, "SetLocationButton");
            window.Close(); // closing afterwards must not deliver them a second time

            Assert.Equal(new[] { "choices True Reading", "settings opened" }, order);
        });
    }
}

// Builds the real Settings window, which reads the shared data folder (history, profiles); serialised with the store tests so the two cannot collide.
[Collection("AppDataStores")]
public class BreaksTabTests
{
    private sealed class FakeGamma : IGammaController
    {
        public string DeviceName => @"\\.\DISPLAY1";
        public int LastWin32Error => 0;
        public bool ApplyColorTemperature(int kelvin, double brightnessAssist = 1.0) => true;
        public bool ApplyColorTemperatureWithContrast(int kelvin, double contrastReduction) => true;
        public bool RestoreBaseline() => true;
        public bool RestoreBaselineIfModified() => true;
    }

    private sealed class FakeGammaSource : IGammaSource
    {
        public IReadOnlyCollection<IGammaController> Controllers { get; } = new[] { new FakeGamma() };
        public void RestoreAllToBaseline() { }
    }

    private sealed class FakeOverlay : IPreviewOverlay
    {
        public IReadOnlyCollection<string> DeviceNames { get; } = new[] { @"\\.\DISPLAY1" };
        public void ApplyDim(IReadOnlyDictionary<string, double> brightnessByDevice, Color dimColor) { }
        public void Apply(IReadOnlyDictionary<string, (Color Color, double Opacity)> byDevice) { }
        public void Clear() { }
        public void IdentifyMonitors(TimeSpan duration) { }
    }

    private static void WithWindow(Action<SettingsWindow> body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(new SettingsWindow(new AppSettings(), new FakeGammaSource(), new FakeOverlay(), () => { }, () => "test", probeCapabilities: () => Array.Empty<DdcCiBrightnessCapability>())); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw new InvalidOperationException("The window test failed on its UI thread.", failure);
    }

    private static string? TabOf(SettingsWindow window, string controlName)
    {
        DependencyObject? node = window.FindName(controlName) as DependencyObject;
        while (node is not null && node is not TabItem)
            node = LogicalTreeHelper.GetParent(node) ?? System.Windows.Media.VisualTreeHelper.GetParent(node);
        return (node as TabItem)?.Header as string;
    }

    [Fact]
    public void The_tabs_are_in_this_order()
    {
        WithWindow(window =>
        {
            var tabs = ((TabControl)window.FindName("SettingsTabs")).Items.OfType<TabItem>().Select(tab => (string)tab.Header).ToArray();

            Assert.Equal(new[] { "Schedule", "Migraine Mode", "Monitors", "Breaks", "Profiles & History" }, tabs);
        });
    }

    [Fact]
    public void Each_control_lives_on_the_tab_where_it_belongs()
    {
        var expected = new (string Control, string Tab)[]
        {
            ("BreakReminderCheckBox", "Breaks"), ("BreakHoldDuringCallsCheckBox", "Breaks"), ("BreakFocusSecondsSlider", "Breaks"),
            ("StandUpEnabledCheckBox", "Breaks"), ("BreakHistoryCheckBox", "Breaks"), ("ExportDailyHistoryButton", "Breaks"),
            ("FullscreenPresentationGuardCheckBox", "Monitors"), ("CompatibilityOverlayOnlyCheckBox", "Monitors"), ("ExcludeFromCaptureCheckBox", "Monitors"),
            ("HistoryTrackingCheckBox", "Profiles & History"), ("DefaultComfortPlanComboBox", "Profiles & History"),
            ("FollowThemeCheckBox", "Schedule"),
        };

        // One window for all of them: building the real window is the slow part.
        WithWindow(window =>
        {
            var wrong = expected.Where(e => TabOf(window, e.Control) != e.Tab).Select(e => $"{e.Control} is on '{TabOf(window, e.Control)}', not '{e.Tab}'").ToList();
            Assert.True(wrong.Count == 0, string.Join("; ", wrong));
        });
    }

    [Fact]
    public void A_notification_can_open_settings_on_the_breaks_tab_and_an_unknown_tab_is_ignored()
    {
        WithWindow(window =>
        {
            var tabs = (TabControl)window.FindName("SettingsTabs");

            window.SelectTab("Breaks");
            Assert.Equal("Breaks", (string)((TabItem)tabs.SelectedItem).Header);

            window.SelectTab("Nonexistent");
            Assert.Equal("Breaks", (string)((TabItem)tabs.SelectedItem).Header);
        });
    }
}

public class UserGuideMatchesBehaviourTests
{
    private static string RepoFile(string name, [CallerFilePath] string testFile = "")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", "..", name)));

    [Fact]
    public void The_user_guide_uses_the_current_tab_names_and_describes_the_remembered_comfort_plan()
    {
        string guide = RepoFile("USER_GUIDE.md");

        Assert.DoesNotContain("Monitors & Breaks", guide);
        Assert.Contains("Settings → Breaks", guide);
        Assert.Contains("remembered until\nmidnight", guide.Replace("\r\n", "\n"));
        Assert.Contains("Start each day with a", guide.Replace("\r\n", "\n"));
        Assert.Contains("I already took one", guide);
    }

    [Fact]
    public void The_privacy_policy_points_at_the_tab_that_holds_the_call_option()
        => Assert.Contains("Settings → Breaks", RepoFile("PRIVACY.md"));
}
