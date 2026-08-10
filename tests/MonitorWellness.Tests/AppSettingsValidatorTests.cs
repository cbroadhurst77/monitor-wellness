using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class AppSettingsValidatorTests
{
    [Fact]
    public void DefaultSettings_AreValid()
    {
        Assert.True(AppSettingsValidator.TryValidate(new AppSettings(), out string error), error);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(91)]
    public void InvalidLatitude_IsRejected(double latitude)
    {
        var settings = new AppSettings { Latitude = latitude };

        Assert.False(AppSettingsValidator.TryValidate(settings, out _));
    }

    [Fact]
    public void NullMonitorCollection_IsRejected()
    {
        var settings = new AppSettings { ExcludedMonitors = null! };

        Assert.False(AppSettingsValidator.TryValidate(settings, out _));
    }

    [Fact]
    public void NullHardwareBrightnessCollection_IsRejected()
    {
        var settings = new AppSettings { HardwareBrightnessEnabledMonitors = null! };

        Assert.False(AppSettingsValidator.TryValidate(settings, out _));
    }

    [Fact]
    public void NullHardwareBrightnessSafetyCollection_IsRejected()
    {
        var settings = new AppSettings { HardwareBrightnessSafetyByMonitor = null! };

        Assert.False(AppSettingsValidator.TryValidate(settings, out _));
    }

    [Fact]
    public void InvalidOverlayColor_IsRejected()
    {
        var settings = new AppSettings { DeepNightOverlayColorHex = "not-a-colour" };

        Assert.False(AppSettingsValidator.TryValidate(settings, out _));
    }

    [Fact]
    public void InvalidDefaultMigraineResponse_IsRejected()
    {
        var settings = new AppSettings { DefaultMigraineResponsePlan = "Unknown" };

        Assert.False(AppSettingsValidator.TryValidate(settings, out _));
    }

    [Fact]
    public void DuplicateApplicationComfortRules_AreRejected()
    {
        var settings = new AppSettings
        {
            ApplicationComfortRules = new List<ApplicationComfortRule>
            {
                new() { ProcessName = "photoshop" },
                new() { ProcessName = "Photoshop.exe" },
            },
        };

        Assert.False(AppSettingsValidator.TryValidate(settings, out _));
    }

    [Fact]
    public void ComfortPlanApplicationRule_RequiresKnownPlan()
    {
        var settings = new AppSettings
        {
            ApplicationComfortRules = new List<ApplicationComfortRule>
            {
                new()
                {
                    ProcessName = "winword",
                    Action = ApplicationComfortActions.ApplySensoryComfortPlan,
                    ComfortPlanName = "Unknown",
                },
            },
        };

        Assert.False(AppSettingsValidator.TryValidate(settings, out _));
    }

    [Fact]
    public void BrightnessBelowSafetyFloor_IsRejected()
    {
        var settings = new AppSettings { DayBrightness = AppSettingsValidator.MinimumSafeBrightness - 0.01 };

        Assert.False(AppSettingsValidator.TryValidate(settings, out string error));
        Assert.Contains("Brightness", error);
    }

    [Fact]
    public void Clone_IsDeepCopy()
    {
        var original = new AppSettings { ExcludedMonitors = new List<string> { @"\\.\DISPLAY1" } };

        var clone = original.Clone();
        clone.ExcludedMonitors.Add(@"\\.\DISPLAY2");

        Assert.Single(original.ExcludedMonitors);
        Assert.Equal(2, clone.ExcludedMonitors.Count);
    }

    [Fact]
    public void Clone_PreservesFullscreenPresentationGuardPreference()
    {
        var original = new AppSettings { RestoreNativeDisplayInFullscreen = true };

        AppSettings clone = original.Clone();

        Assert.True(clone.RestoreNativeDisplayInFullscreen);
    }

    [Fact]
    public void HardwareBrightnessOptIns_AreDeepCopied()
    {
        var original = new AppSettings { HardwareBrightnessEnabledMonitors = new List<string> { @"\\.\DISPLAY1" } };

        var clone = original.Clone();
        clone.HardwareBrightnessEnabledMonitors.Add(@"\\.\DISPLAY2");

        Assert.Single(original.HardwareBrightnessEnabledMonitors);
        Assert.Equal(2, clone.HardwareBrightnessEnabledMonitors.Count);
    }

    [Fact]
    public void HardwareBrightnessSafetyState_IsDeepCopied()
    {
        var original = new AppSettings
        {
            HardwareBrightnessSafetyByMonitor = new Dictionary<string, HardwareBrightnessSafetyState>
            {
                ["monitor:ACME"] = new HardwareBrightnessSafetyState { IsApproved = true },
            },
        };

        var clone = original.Clone();
        clone.HardwareBrightnessSafetyByMonitor["monitor:ACME"].IsApproved = false;

        Assert.True(original.HardwareBrightnessSafetyByMonitor["monitor:ACME"].IsApproved);
    }

    [Fact]
    public void CopyFrom_ReplacesValuesWithoutSharingCollections()
    {
        var target = new AppSettings();
        var source = new AppSettings { ExcludedMonitors = new List<string> { @"\\.\DISPLAY1" } };

        target.CopyFrom(source);
        source.ExcludedMonitors.Add(@"\\.\DISPLAY2");

        Assert.Single(target.ExcludedMonitors);
    }

    [Theory]
    [InlineData("Work", true)]
    [InlineData("CON", false)]
    [InlineData("..", false)]
    [InlineData("Name/with/slash", false)]
    public void ProfileNameValidation_RejectsUnsafeFileNames(string name, bool expected)
    {
        Assert.Equal(expected, ProfileStore.TryValidateName(name, out _));
    }
}
