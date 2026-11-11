#pragma warning disable CA1861 // small literal arrays in tests are clearer than shared static fields
using System.Reflection;
using System.Text.Json;
using System.Windows.Controls;
using MonitorWellness.Core;
using CheckBox = System.Windows.Controls.CheckBox;
using Color = System.Windows.Media.Color;

namespace MonitorWellness.Tests;

/// <summary>
/// Importing a settings file must bring in every setting that an export writes, except the few that are
/// deliberately left out. Two settings (the app-aware comfort rules, and the overlay-only compatibility
/// preference) were silently dropped by an import for several releases because the import copies settings
/// one at a time and nothing checked that list against the settings class. This imports a file in which
/// EVERY setting has a non-default value, through the real window (over fake displays), and fails naming
/// any that did not arrive. Adding a new setting without handling it here and in the import fails this test.
/// </summary>
// Builds the real Settings window, which reads the shared data folder (history, profiles); serialised with the store tests so the two cannot collide.
[Collection("AppDataStores")]
public class ImportCoverageTests
{
    /// <summary>Settings an import deliberately leaves alone, and why. Anything not listed here must arrive.</summary>
    private static readonly Dictionary<string, string> IntentionallyNotImported = new()
    {
        ["SchemaVersion"] = "stamped by the app when it saves",
        ["HasCompletedOnboarding"] = "per-install state: a file from another PC must not skip or replay the welcome",
        ["LocationReminderShown"] = "per-install state: a one-time reminder",
        ["LastUpdateCheckUtc"] = "per-install state: when this PC last checked for updates",
        ["AutoStartEnabled"] = "managed by the tray's Start with Windows item and Task Scheduler, not a settings value to copy",
        ["HardwareBrightnessEnabledMonitors"] = "legacy display-name approvals, deliberately never honoured",
        ["HardwareBrightnessSafetyByMonitor"] = "hardware safety records: a file from another PC must never approve physical dimming here",
    };

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

    /// <summary>A valid settings object in which every importable setting differs from its default.</summary>
    private static AppSettings EverythingNonDefault()
    {
        var settings = new AppSettings
        {
            Latitude = 40.1, Longitude = -70.2, LocationConfirmed = true,
            DayKelvin = 5800, NightKelvin = 3700, DayBrightness = 0.9, NightBrightness = 0.6, DeepNightBrightness = 0.5,
            DeepNightOverlayColorHex = "#201008", MigraineOverlayColorHex = "#103020", MigraineOverlayOpacity = 0.5, MigraineContrastReduction = 0.1,
            DefaultMigraineResponsePlan = MigraineResponsePlans.Gentle,
            MigraineHotkeyModifiers = GlobalHotkey.MOD_CONTROL | GlobalHotkey.MOD_ALT, MigraineHotkeyKey = 0x4E,
            MigraineAutoRevertMinutes = 45, PlaySoundOnMigraineToggle = true,
            BedtimeLocal = "23:15", WakeTimeLocal = "06:45",
            ScheduleMode = ScheduleModes.FixedTimes, FixedSunriseLocal = "06:15", FixedSunsetLocal = "20:45",
            HistoryTrackingEnabled = true, PromptForMigraineRating = true, MatchAmbientLight = true,
            BreakReminderEnabled = true, BreakReminderIntervalMinutes = 25, BreakReminderHoldDuringCalls = false,
            BreakReminderLimitToHours = true, BreakReminderFromLocal = "09:30", BreakReminderToLocal = "17:45",
            BreakReminderStyle = BreakReminderStyles.Notification, BreakReminderSound = true, BreakFocusSeconds = 45,
            StandUpReminderEnabled = true, StandUpIntervalMinutes = 90, BreakHistoryEnabled = true,
            FollowScheduleTheme = true, DefaultComfortPlan = "Reading",
            RestoreNativeDisplayInFullscreen = true, PreferOverlayOnlyOnCompatibilityDisplays = false,
            CheckForUpdatesEnabled = true, UpdateNotifyPreReleases = false, ExcludeOverlayFromCapture = false,
        };
        settings.ExcludedMonitors.Add("monitor:EXCLUDED");
        settings.ColorExcludedMonitors.Add("monitor:COLOUR");
        settings.MonitorDimMultiplier["monitor:DIM"] = 0.7;
        settings.MonitorKelvinOffset["monitor:KELVIN"] = -200;
        settings.ApplicationComfortRules.Add(new ApplicationComfortRule { ProcessName = "vlc", Action = ApplicationComfortActions.RestoreNativeDisplay, IsEnabled = true });
        return settings;
    }

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

    private static (SettingsWindow Window, AppSettings Draft) ImportInto(AppSettings imported)
    {
        var window = new SettingsWindow(new AppSettings(), new FakeGammaSource(), new FakeOverlay(), () => { }, () => "test", probeCapabilities: () => Array.Empty<DdcCiBrightnessCapability>());
        typeof(SettingsWindow).GetMethod("ApplyImportedSettings", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, new object[] { imported });

        var args = new object?[] { "" };
        bool ok = (bool)typeof(SettingsWindow).GetMethod("TryParseAll", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, args)!;
        Assert.True(ok, (string)args[0]!);

        var draft = (AppSettings)typeof(SettingsWindow).GetField("_settings", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        return (window, draft);
    }

    [Fact]
    public void The_test_file_itself_is_valid_and_has_no_setting_left_at_its_default()
    {
        AppSettings imported = EverythingNonDefault();
        Assert.True(AppSettingsValidator.TryValidate(imported, out string why), why);

        var defaults = new AppSettings();
        var unchanged = typeof(AppSettings).GetProperties()
            .Where(p => !IntentionallyNotImported.ContainsKey(p.Name))
            .Where(p => JsonSerializer.Serialize(p.GetValue(imported)) == JsonSerializer.Serialize(p.GetValue(defaults)))
            .Select(p => p.Name)
            .ToList();

        Assert.True(unchanged.Count == 0,
            "These settings still have their default value in EverythingNonDefault(), so this test cannot tell whether an import carries them: "
            + string.Join(", ", unchanged) + ". Give each a non-default value (or, if an import should leave it alone, add it to IntentionallyNotImported with the reason).");
    }

    [Fact]
    public void Every_setting_an_export_writes_arrives_through_an_import_except_those_deliberately_left_out()
    {
        OnStaThread(() =>
        {
            AppSettings imported = EverythingNonDefault();
            var (_, draft) = ImportInto(imported);

            var missing = new List<string>();
            foreach (PropertyInfo property in typeof(AppSettings).GetProperties())
            {
                if (IntentionallyNotImported.ContainsKey(property.Name))
                    continue;

                object? want = property.GetValue(imported);
                object? got = property.GetValue(draft);
                bool arrived = want switch
                {
                    // The per-monitor settings also get an entry for each monitor really connected, so the imported entries must be present, not the whole thing equal.
                    Dictionary<string, double> wanted => got is Dictionary<string, double> have && wanted.All(pair => have.TryGetValue(pair.Key, out double v) && v == pair.Value),
                    Dictionary<string, int> wanted => got is Dictionary<string, int> have && wanted.All(pair => have.TryGetValue(pair.Key, out int v) && v == pair.Value),
                    List<string> wanted => got is List<string> have && wanted.All(have.Contains),
                    _ => JsonSerializer.Serialize(want) == JsonSerializer.Serialize(got),
                };
                if (!arrived)
                    missing.Add($"{property.Name} (imported {JsonSerializer.Serialize(want)}, window has {JsonSerializer.Serialize(got)})");
            }

            Assert.True(missing.Count == 0, "These settings were not imported: " + string.Join("; ", missing));
        });
    }

    [Fact]
    public void Imported_app_rules_and_the_compatibility_preference_show_in_the_window()
    {
        OnStaThread(() =>
        {
            var (window, _) = ImportInto(EverythingNonDefault());

            var rules = (StackPanel)window.FindName("ApplicationRulesList");
            Assert.Single(rules.Children);
            Assert.False(((CheckBox)window.FindName("CompatibilityOverlayOnlyCheckBox")).IsChecked);
        });
    }

    [Fact]
    public void The_settings_left_out_on_purpose_really_are_left_alone()
    {
        OnStaThread(() =>
        {
            var imported = EverythingNonDefault();
            imported.HasCompletedOnboarding = true;
            imported.AutoStartEnabled = true;
            imported.LastUpdateCheckUtc = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
            imported.HardwareBrightnessSafetyByMonitor["monitor:OTHERPC"] = new HardwareBrightnessSafetyState { IsApproved = true };

            var (_, draft) = ImportInto(imported);

            Assert.False(draft.HasCompletedOnboarding);
            Assert.False(draft.AutoStartEnabled);
            Assert.Null(draft.LastUpdateCheckUtc);
            Assert.DoesNotContain("monitor:OTHERPC", draft.HardwareBrightnessSafetyByMonitor.Keys); // never approve physical dimming from a file
        });
    }

    [Fact]
    public void Every_deliberate_exclusion_names_a_real_setting()
    {
        var names = typeof(AppSettings).GetProperties().Select(p => p.Name).ToHashSet();

        Assert.All(IntentionallyNotImported.Keys, key => Assert.Contains(key, names));
    }
}
