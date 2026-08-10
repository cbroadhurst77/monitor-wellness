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
    public void InvalidOverlayColor_IsRejected()
    {
        var settings = new AppSettings { DeepNightOverlayColorHex = "not-a-colour" };

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
