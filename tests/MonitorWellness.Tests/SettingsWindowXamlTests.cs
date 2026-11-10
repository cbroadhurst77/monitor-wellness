using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using RadioButton = System.Windows.Controls.RadioButton;
using TabControl = System.Windows.Controls.TabControl;
using TextBox = System.Windows.Controls.TextBox;

namespace MonitorWellness.Tests;

/// <summary>
/// The build validates the XAML's element and handler names, but nothing exercises it at run
/// time, so a markup mistake would only show when someone opened Settings. This parses the real
/// SettingsWindow.xaml with the XAML reader (after removing the code-behind class and event
/// handler attributes, which need the real window class) and checks the controls the schedule
/// source and wake-time features rely on. It deliberately does not construct the real window: that
/// needs live display controllers and would change the screen colours of whoever runs the tests.
/// </summary>
public class SettingsWindowXamlTests
{
    private static string XamlPath([CallerFilePath] string testFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", "..", "src", "MonitorWellness", "SettingsWindow.xaml"));

    /// <summary>
    /// Parses the window and runs the assertions on the same STA thread that created it (WPF
    /// objects can't be touched from another thread), re-throwing anything that went wrong.
    /// </summary>
    private static void WithSettingsWindow(Action<Window> assertions)
    {
        string xaml = File.ReadAllText(XamlPath());
        xaml = Regex.Replace(xaml, @"\sx:Class=""[^""]*""", "");
        xaml = Regex.Replace(
            xaml,
            @"\s(Click|Checked|Unchecked|TextChanged|ValueChanged|SelectionChanged|Loaded|Closed|Closing|PreviewKeyDown|KeyDown|LostFocus|GotFocus|"
            + @"MouseLeftButtonDown|PreviewMouseDown|MouseDown|DropDownClosed|SizeChanged|PreviewTextInput)=""[^""]*""",
            "");

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = (Window)XamlReader.Parse(xaml);
                assertions(window);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
            throw new Xunit.Sdk.XunitException("SettingsWindow.xaml check failed: " + failure);
    }

    private static T Find<T>(Window window, string name) where T : class
        => window.FindName(name) as T ?? throw new Xunit.Sdk.XunitException($"Control '{name}' ({typeof(T).Name}) was not found in SettingsWindow.xaml.");

    [Fact]
    public void SettingsXaml_Parses_AndHasTheScheduleSourceControls()
        => WithSettingsWindow(window =>
        {
            var sun = Find<RadioButton>(window, "ScheduleModeSunRadio");
            var fixedTimes = Find<RadioButton>(window, "ScheduleModeFixedRadio");
            var panel = Find<StackPanel>(window, "FixedTimesPanel");
            Find<TextBox>(window, "FixedSunriseBox");
            Find<TextBox>(window, "FixedSunsetBox");
            Find<TextBlock>(window, "FixedTimesWarning");

            Assert.Equal("ScheduleMode", sun.GroupName);
            Assert.Equal("ScheduleMode", fixedTimes.GroupName);
            Assert.True(sun.IsChecked);                            // the sun is the default
            Assert.Equal(Visibility.Collapsed, panel.Visibility);  // clock-time fields hidden until chosen
        });

    [Fact]
    public void SettingsXaml_HasTheWakeTimeBox_NextToBedtime()
        => WithSettingsWindow(window =>
        {
            Find<TextBox>(window, "BedtimeBox");
            Find<TextBox>(window, "WakeTimeBox");
            Find<TextBlock>(window, "BedtimeWarning");
        });

    [Fact]
    public void SettingsXaml_StillHasTheEarlierAdditions()
        => WithSettingsWindow(window =>
        {
            Find<StackPanel>(window, "LocationDefaultBanner");
            Find<Button>(window, "ConfirmLondonButton");
            Find<CheckBox>(window, "ExcludeFromCaptureCheckBox");
            Find<TabControl>(window, "SettingsTabs");
        });
}
