using System.IO;
using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class LocationStatusTests
{
    [Fact]
    public void FreshSettings_AreAnUnconfirmedDefault()
        => Assert.True(LocationStatus.IsUsingUnconfirmedDefault(new AppSettings()));

    [Fact]
    public void AnyOtherPlace_IsNotFlagged()
        => Assert.False(LocationStatus.IsUsingUnconfirmedDefault(new AppSettings { Latitude = 40.7128, Longitude = -74.0060 }));

    [Fact]
    public void ConfirmingLondon_ClearsTheFlag()
        => Assert.False(LocationStatus.IsUsingUnconfirmedDefault(new AppSettings { LocationConfirmed = true }));

    [Fact]
    public void CoordinatesTypedToFourDecimals_StillCountAsTheDefault()
    {
        Assert.True(LocationStatus.IsUsingUnconfirmedDefault(51.5072, -0.1276, confirmed: false));
        Assert.True(LocationStatus.IsUsingUnconfirmedDefault(51.5071, -0.1277, confirmed: false));
        Assert.False(LocationStatus.IsUsingUnconfirmedDefault(51.52, -0.1276, confirmed: false)); // ~2 km away: a deliberate choice
    }

    [Theory]
    [InlineData(false, false, 51.5072, -0.1276, false)] // still London, nobody said it's right
    [InlineData(false, true, 51.5072, -0.1276, true)]   // "London is correct"
    [InlineData(false, false, 48.8566, 2.3522, true)]   // chose Paris
    [InlineData(true, false, 51.5072, -0.1276, true)]   // stays confirmed
    public void ConfirmedAfterSave_FollowsTheRules(bool previously, bool thisSession, double lat, double lon, bool expected)
        => Assert.Equal(expected, LocationStatus.ConfirmedAfterSave(previously, thisSession, lat, lon));

    [Fact]
    public void LocationFlags_SurviveCloneAndExport()
    {
        var settings = new AppSettings { LocationConfirmed = true, LocationReminderShown = true };

        var clone = settings.Clone();

        Assert.True(clone.LocationConfirmed);
        Assert.True(clone.LocationReminderShown);
    }

    [Fact]
    public void AnOlderSettingsFile_WithoutTheFlags_LoadsAsNotConfirmed_SoOnlyDefaultCoordinatesAreFlagged()
    {
        string path = Path.Combine(Path.GetTempPath(), "mw-loc-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{ \"Latitude\": 35.6762, \"Longitude\": 139.6503 }");
            Assert.True(SettingsStore.TryImportFrom(path, out AppSettings tokyo, out _));
            Assert.False(LocationStatus.IsUsingUnconfirmedDefault(tokyo));

            File.WriteAllText(path, "{ \"DayKelvin\": 6500 }");
            Assert.True(SettingsStore.TryImportFrom(path, out AppSettings untouched, out _));
            Assert.True(LocationStatus.IsUsingUnconfirmedDefault(untouched));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
