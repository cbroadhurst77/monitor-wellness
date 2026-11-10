using System.IO;
using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public sealed class AutoStartFallbackAndSettingsBackupTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mw-settings-tests-" + Guid.NewGuid().ToString("N"));

    public AutoStartFallbackAndSettingsBackupTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Theory]
    [InlineData(@"DESKTOP-1\chris", "chris", true)]
    [InlineData(@"CHRIS", "chris", true)]
    [InlineData(@"CORP\adminuser", "chris", false)]
    [InlineData("adminuser", "chris", false)]
    [InlineData(null, "chris", true)]   // unknown (e.g. localized schtasks output) is never treated as a mismatch
    [InlineData("", "chris", true)]
    [InlineData(@"CORP\chris", "", true)]
    public void RunsAsUser_OnlyReportsAPositivelyIdentifiedDifferentAccount(string? runAsUser, string current, bool expected)
        => Assert.Equal(expected, AutoStartManager.RunsAsUser(runAsUser, current));

    [Fact]
    public void BuildRunValue_QuotesThePathSoSpacesSurvive()
        => Assert.Equal("\"C:\\Program Files\\Monitor Wellness\\MonitorWellness.exe\"",
            AutoStartManager.BuildRunValue(@"C:\Program Files\Monitor Wellness\MonitorWellness.exe"));

    [Fact]
    public void ParseFields_ExposesTheRunAsUserLabel()
    {
        var fields = AutoStartManager.ParseFields("TaskName:   \\MonitorWellness\r\nRun As User:  DESKTOP\\chris\r\nStatus: Ready\r\n");

        Assert.Equal("DESKTOP\\chris", fields["Run As User"]);
    }

    [Fact]
    public void PreserveCopy_KeepsTheOriginalBytesUnderASiblingName()
    {
        string settings = Path.Combine(_directory, "settings.json");
        File.WriteAllText(settings, "{ \"DayKelvin\": 99999 }");

        string? kept = SettingsStore.PreserveCopy(settings, "invalid-test", pruneOldCopies: true);

        Assert.NotNull(kept);
        Assert.Equal("settings.invalid-test.json", Path.GetFileName(kept));
        Assert.Equal("{ \"DayKelvin\": 99999 }", File.ReadAllText(kept!));
        Assert.True(File.Exists(settings)); // the original is untouched; only a copy is made
    }

    [Fact]
    public void PreserveCopy_KeepsOnlyTheNewestFewUnreadableCopies()
    {
        string settings = Path.Combine(_directory, "settings.json");
        File.WriteAllText(settings, "bad");
        for (int i = 1; i <= 5; i++)
        {
            string? kept = SettingsStore.PreserveCopy(settings, $"invalid-2026110{i}-000000", pruneOldCopies: true);
            File.SetLastWriteTimeUtc(kept!, new DateTime(2026, 11, i, 0, 0, 0, DateTimeKind.Utc));
        }

        string[] remaining = Directory.GetFiles(_directory, "settings.invalid-*.json");

        Assert.Equal(3, remaining.Length);
        Assert.Contains(remaining, path => path.EndsWith("20261105-000000.json", StringComparison.Ordinal));
        Assert.DoesNotContain(remaining, path => path.EndsWith("20261101-000000.json", StringComparison.Ordinal));
    }

    [Fact]
    public void PreserveCopy_OfAMissingFile_ReturnsNullInsteadOfThrowing()
        => Assert.Null(SettingsStore.PreserveCopy(Path.Combine(_directory, "nope.json"), "invalid-x", pruneOldCopies: true));

    [Fact]
    public void NewSettings_ExcludeTheOverlayFromCaptureByDefault_AndRoundTripThroughCloneAndExport()
    {
        var settings = new AppSettings();
        Assert.True(settings.ExcludeOverlayFromCapture);
        Assert.Equal(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);

        settings.ExcludeOverlayFromCapture = false;
        Assert.False(settings.Clone().ExcludeOverlayFromCapture);

        string path = Path.Combine(_directory, "export.json");
        SettingsStore.ExportTo(settings, path);
        Assert.True(SettingsStore.TryImportFrom(path, out AppSettings imported, out _));
        Assert.False(imported.ExcludeOverlayFromCapture);
        Assert.Equal(AppSettings.CurrentSchemaVersion, imported.SchemaVersion);
    }

    [Fact]
    public void AnOlderSettingsFileWithoutTheNewFields_LoadsWithTheirDefaults()
    {
        string path = Path.Combine(_directory, "old.json");
        File.WriteAllText(path, "{ \"DayKelvin\": 6000 }");

        Assert.True(SettingsStore.TryImportFrom(path, out AppSettings imported, out _));

        Assert.Equal(6000, imported.DayKelvin);
        Assert.True(imported.ExcludeOverlayFromCapture);
    }
}
