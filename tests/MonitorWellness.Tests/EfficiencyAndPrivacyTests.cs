using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class EfficiencyAndPrivacyTests
{
    // ---- TimedCache ---------------------------------------------------------------------

    [Fact]
    public void TimedCache_ReusesTheValueUntilItExpires()
    {
        var cache = new TimedCache<int>(TimeSpan.FromSeconds(2));
        var t0 = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
        int calls = 0;
        int Create() => ++calls;

        Assert.Equal(1, cache.Get(Create, t0));
        Assert.Equal(1, cache.Get(Create, t0.AddMilliseconds(1999)));
        Assert.Equal(1, calls);

        Assert.Equal(2, cache.Get(Create, t0.AddSeconds(2)));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void TimedCache_Invalidate_ForcesARecompute()
    {
        var cache = new TimedCache<string>(TimeSpan.FromMinutes(5));
        var now = DateTime.UtcNow;
        int calls = 0;
        string Create() => "v" + (++calls);

        Assert.Equal("v1", cache.Get(Create, now));
        cache.Invalidate();

        Assert.Equal("v2", cache.Get(Create, now));
    }

    // ---- foreground polling -------------------------------------------------------------

    private static ApplicationComfortRule Rule(bool enabled)
        => new() { ProcessName = "photoshop", IsEnabled = enabled };

    [Fact]
    public void ForegroundPolling_IsSkipped_WhenThereIsNothingToWatchFor()
        => Assert.False(ApplicationRuleRefreshPolicy.ShouldPollForeground(false, Array.Empty<ApplicationComfortRule>(), false));

    [Fact]
    public void ForegroundPolling_IgnoresDisabledRules()
        => Assert.False(ApplicationRuleRefreshPolicy.ShouldPollForeground(false, new[] { Rule(false) }, false));

    [Theory]
    [InlineData(true, false, false)]   // full-screen guard on
    [InlineData(false, true, false)]   // an enabled rule exists
    [InlineData(false, false, true)]   // a rule's effect is still on screen and must be cleared
    public void ForegroundPolling_RunsWhenAnythingNeedsIt(bool guard, bool enabledRule, bool stateToClear)
        => Assert.True(ApplicationRuleRefreshPolicy.ShouldPollForeground(guard, enabledRule ? new[] { Rule(true) } : Array.Empty<ApplicationComfortRule>(), stateToClear));

    // ---- log redaction ------------------------------------------------------------------

    [Fact]
    public void Redact_RemovesTheProfileFolder_CaseInsensitively()
    {
        string text = @"SettingsStore: loaded settings from C:\Users\chris\AppData\Roaming\MonitorWellness\settings.json and c:\users\CHRIS\x";

        string redacted = LogRedaction.Redact(text, @"C:\Users\chris", "chris");

        Assert.DoesNotContain("chris", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"%USERPROFILE%\AppData\Roaming\MonitorWellness\settings.json", redacted);
    }

    [Fact]
    public void Redact_AlsoCoversAProfileOnAnotherDrive()
    {
        string redacted = LogRedaction.Redact(@"opened D:\Users\chris\Documents\x.txt", @"C:\Users\chris", "chris");

        Assert.Equal(@"opened %USERPROFILE%\Documents\x.txt", redacted);
    }

    [Fact]
    public void Redact_LeavesOrdinaryTextAndApplicationNamesAlone()
    {
        const string text = "Application comfort rule active for photoshop: restoring native display state.";

        Assert.Equal(text, LogRedaction.Redact(text, @"C:\Users\chris", "chris"));
        Assert.Equal("", LogRedaction.Redact("", @"C:\Users\chris", "chris"));
    }

    [Fact]
    public void Redact_DoesNotMangleAnotherUsersLongerName()
    {
        // "chris" must not be stripped out of "christine"'s folder.
        string redacted = LogRedaction.Redact(@"C:\Users\christine\x", @"C:\Users\chris", "chris");

        Assert.Equal(@"C:\Users\christine\x", redacted);
    }

    // ---- sun times for the person's own day ---------------------------------------------

    [Fact]
    public void SunTimes_ForAFarEastLocalDay_ComeFromThatLocalDay_NotTheUtcDate()
    {
        // Wellington, 10 November 2026, NZDT (UTC+13). Local midnight is 11:00 UTC the day before.
        const double lat = -41.2866, lon = 174.7756;
        var localDayStartUtc = new DateTime(2026, 11, 9, 11, 0, 0, DateTimeKind.Utc);

        DateTime? sunrise = SolarCalculator.FindSunriseInWindowUtc(localDayStartUtc, lat, lon);
        DateTime? sunset = SolarCalculator.FindSunsetInWindowUtc(localDayStartUtc, lat, lon);

        Assert.NotNull(sunrise);
        Assert.NotNull(sunset);
        Assert.InRange(sunrise!.Value, localDayStartUtc, localDayStartUtc.AddHours(24));
        Assert.InRange(sunset!.Value, localDayStartUtc, localDayStartUtc.AddHours(24));
        // Local sunrise is about 06:00, so it falls on 9 November in UTC...
        Assert.Equal(new DateTime(2026, 11, 9), sunrise.Value.Date);
        // ...which the old UTC-date lookup (which starts at 00:00 UTC on the 10th) could not return.
        DateTime? oldSunrise = SolarCalculator.FindSunriseUtc(new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc), lat, lon);
        Assert.NotEqual(sunrise, oldSunrise);
        Assert.True(sunrise < sunset);
    }

    [Fact]
    public void SunTimes_ByUtcDate_AreUnchanged()
    {
        var date = new DateTime(2026, 6, 21, 15, 30, 0, DateTimeKind.Utc);

        DateTime? viaDate = SolarCalculator.FindSunriseUtc(date, 51.5072, -0.1276);
        DateTime? viaWindow = SolarCalculator.FindSunriseInWindowUtc(date.Date, 51.5072, -0.1276);

        Assert.Equal(viaDate, viaWindow);
    }

    // ---- last result wording ------------------------------------------------------------

    [Theory]
    [InlineData(0, "0 (succeeded)")]
    [InlineData(267009, "267009 (the task is running now)")]
    [InlineData(267011, "267011 (it has not run yet)")]
    [InlineData(2147942402, "-2147024894 (0x80070002)")]
    public void LastResult_IsSpelledOutForTheCommonCodes(long raw, string expected)
        => Assert.Equal(expected, TaskSchedulerQuery.DescribeLastResult(unchecked((int)raw)));
}
