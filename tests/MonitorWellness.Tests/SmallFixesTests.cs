using System.IO;
using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class SmallFixesTests
{
    // ---- update check ---------------------------------------------------------------------

    private const string Releases = """
        [
          { "tag_name": "v0.2.8", "prerelease": true,  "draft": false, "html_url": "https://github.com/cbroadhurst77/monitor-wellness/releases/tag/v0.2.8" },
          { "tag_name": "v0.2.7", "prerelease": true,  "draft": false, "html_url": "https://github.com/cbroadhurst77/monitor-wellness/releases/tag/v0.2.7" },
          { "tag_name": "v0.2.2", "prerelease": false, "draft": false, "html_url": "https://github.com/cbroadhurst77/monitor-wellness/releases/tag/v0.2.2" },
          { "tag_name": "v0.2.1", "prerelease": false, "draft": false, "html_url": "https://github.com/cbroadhurst77/monitor-wellness/releases/tag/v0.2.1" }
        ]
        """;

    [Fact]
    public void SelectLatest_FindsANewerPreRelease_WhenPreReleasesAreIncluded()
    {
        // The real situation: someone on 0.2.2 and every newer release is a pre-release.
        UpdateInfo? update = UpdateChecker.SelectLatest(Releases, new Version(0, 2, 2, 0), includePreReleases: true);

        Assert.NotNull(update);
        Assert.Equal(new Version(0, 2, 8), update!.Version);
        Assert.Equal("v0.2.8", update.TagName);
        Assert.True(update.IsPreRelease);
        Assert.EndsWith("/releases/tag/v0.2.8", update.ReleaseUrl.AbsoluteUri);
    }

    [Fact]
    public void SelectLatest_IgnoresPreReleases_WhenTheyAreExcluded()
    {
        Assert.Null(UpdateChecker.SelectLatest(Releases, new Version(0, 2, 2, 0), includePreReleases: false)); // already on the newest full release
        Assert.Equal(new Version(0, 2, 2), UpdateChecker.SelectLatest(Releases, new Version(0, 2, 1, 0), includePreReleases: false)!.Version);
    }

    [Fact]
    public void SelectLatest_SaysNothing_WhenAlreadyUpToDate()
    {
        Assert.Null(UpdateChecker.SelectLatest(Releases, new Version(0, 2, 8, 0), includePreReleases: true)); // "0.2.8" vs 0.2.8.0 is not newer
        Assert.Null(UpdateChecker.SelectLatest(Releases, new Version(0, 3, 0, 0), includePreReleases: true));
    }

    [Fact]
    public void SelectLatest_PicksTheHighestVersion_NotTheFirstListed()
    {
        const string unordered = """
            [ { "tag_name": "v0.2.6", "prerelease": false }, { "tag_name": "v0.3.1", "prerelease": false }, { "tag_name": "v0.2.9", "prerelease": false } ]
            """;

        Assert.Equal(new Version(0, 3, 1), UpdateChecker.SelectLatest(unordered, new Version(0, 2, 0, 0), true)!.Version);
    }

    [Fact]
    public void SelectLatest_SkipsDrafts_BadTags_AndUntrustedLinks()
    {
        const string json = """
            [
              { "tag_name": "v9.9.9", "prerelease": false, "draft": true },
              { "tag_name": "nightly",  "prerelease": false },
              { "tag_name": "v0.4.0", "prerelease": false, "html_url": "https://evil.example.com/cbroadhurst77/monitor-wellness/releases/tag/v0.4.0" }
            ]
            """;

        UpdateInfo? update = UpdateChecker.SelectLatest(json, new Version(0, 2, 8, 0), true);

        Assert.Equal(new Version(0, 4, 0), update!.Version);                              // the draft and the non-version tag were skipped
        Assert.StartsWith("https://github.com/cbroadhurst77/monitor-wellness", update.ReleaseUrl.AbsoluteUri); // an untrusted link is replaced by the project's own
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{ \"tag_name\": \"v1.0.0\" }")]   // an object, not a list
    [InlineData("[ 5, \"x\", null ]")]
    public void SelectLatest_ToleratesMalformedResponses(string json)
        => Assert.Null(UpdateChecker.SelectLatest(json, new Version(0, 2, 8, 0), true));

    [Theory]
    [InlineData("v0.2.8", "0.2.8")]
    [InlineData("V1.0.0", "1.0.0")]
    [InlineData("0.3.0", "0.3.0")]
    [InlineData("v0.3.0-beta.1", "0.3.0")]
    [InlineData("v0.3.0+build5", "0.3.0")]
    public void TagsAreReadAsVersions(string tag, string expected)
    {
        Assert.True(UpdateChecker.TryParseTag(tag, out Version version));
        Assert.Equal(Version.Parse(expected), version);
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("")]
    [InlineData("v")]
    public void NonVersionTagsAreRejected(string tag)
        => Assert.False(UpdateChecker.TryParseTag(tag, out _));

    [Fact]
    public void PreReleaseUpdates_AreOnByDefault_AndRoundTripThroughSettings()
    {
        var settings = new AppSettings();
        Assert.True(settings.UpdateNotifyPreReleases);

        settings.UpdateNotifyPreReleases = false;
        Assert.False(settings.Clone().UpdateNotifyPreReleases);
        var target = new AppSettings();
        target.CopyFrom(settings);
        Assert.False(target.UpdateNotifyPreReleases);
    }

    // ---- schtasks by full path --------------------------------------------------------------

    [Fact]
    public void Schtasks_IsLaunchedFromSystem32_NotFoundBySearch()
    {
        string path = AutoStartManager.SchtasksPath;

        Assert.True(Path.IsPathFullyQualified(path));
        Assert.Equal(Environment.SystemDirectory, Path.GetDirectoryName(path));
        Assert.Equal("schtasks.exe", Path.GetFileName(path));
        Assert.True(File.Exists(path), "the real schtasks.exe should exist at that path");
    }

    // ---- hotkey rebind ----------------------------------------------------------------------

    private const uint CtrlAltShift = 0x0002 | 0x0001 | 0x0004;

    [Fact]
    public void Hotkey_UnchangedOnlyWhenTheSameShortcutIsAlreadyWorking()
    {
        Assert.True(HotkeyRebindPolicy.IsUnchanged(true, CtrlAltShift, 0x4D, CtrlAltShift, 0x4D));
        Assert.False(HotkeyRebindPolicy.IsUnchanged(false, CtrlAltShift, 0x4D, CtrlAltShift, 0x4D)); // it never registered: try again
        Assert.False(HotkeyRebindPolicy.IsUnchanged(true, CtrlAltShift, 0x4D, CtrlAltShift, 0x4E));
        Assert.False(HotkeyRebindPolicy.IsUnchanged(true, CtrlAltShift, 0x4D, 0x0002, 0x4D));
    }

    [Theory]
    [InlineData(true, true, HotkeyRebindChoice.UseNew)]
    [InlineData(false, true, HotkeyRebindChoice.UseNew)]
    [InlineData(true, false, HotkeyRebindChoice.KeepPreviousBecauseNewFailed)]          // the bug: used to lose BOTH
    [InlineData(false, false, HotkeyRebindChoice.UseNewEvenThoughItFailed)]             // nothing to lose; the person is told
    public void Hotkey_NeverGivesUpAWorkingShortcutForOneThatFailed(bool hadWorking, bool newRegistered, HotkeyRebindChoice expected)
        => Assert.Equal(expected, HotkeyRebindPolicy.ChooseAfterAttempt(hadWorking, newRegistered));
}
