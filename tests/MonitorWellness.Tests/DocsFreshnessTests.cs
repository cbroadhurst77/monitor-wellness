using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace MonitorWellness.Tests;

/// <summary>
/// The README and the public landing page (GitHub Pages) had drifted: the README still said "v0.2.5 ... 219
/// tests", and the landing page listed six features and none of the break reminders, the remembered comfort
/// plan or the history, which are most of what the last several releases added. These keep them honest:
/// no stale hard-coded version numbers, and the current headline features named.
/// </summary>
public class DocsFreshnessTests
{
    private static string RepoFile(string relative, [CallerFilePath] string testFile = "")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", "..", relative)));

    private static string CurrentVersion()
        => Regex.Match(RepoFile("src/MonitorWellness/MonitorWellness.csproj"), @"<Version>([^<]+)</Version>").Groups[1].Value;

    /// <summary>Version numbers a document may mention: the current one, and the date-stamping licence line's v0.2.0.</summary>
    private static IEnumerable<string> StaleVersionsIn(string text)
    {
        string current = CurrentVersion();
        return Regex.Matches(text, @"\bv?(\d+\.\d+\.\d+)\b")
            .Select(match => match.Groups[1].Value)
            .Where(version => version != current && version != "0.2.0")
            .Distinct();
    }

    [Fact]
    public void The_readme_has_no_hard_coded_old_version_numbers_or_test_counts()
    {
        string readme = RepoFile("README.md");

        Assert.Empty(StaleVersionsIn(readme));
        Assert.DoesNotContain("219 automated tests", readme);
    }

    [Fact]
    public void The_landing_page_has_no_stale_version_numbers()
        => Assert.Empty(StaleVersionsIn(RepoFile("docs/index.html")));

    [Theory]
    [InlineData("Break reminders")]
    [InlineData("stand-up")]
    [InlineData("comfort plan, remembered")]
    [InlineData("Your own records, kept local")]
    [InlineData("light and dark themes")]
    [InlineData("microphone or camera")]
    [InlineData("nothing is ever sent")]
    public void The_landing_page_names_the_current_features_and_what_it_reads(string phrase)
        => Assert.Contains(phrase, RepoFile("docs/index.html"), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void The_landing_page_still_says_there_are_exactly_two_network_calls_and_lists_both()
    {
        string page = RepoFile("docs/index.html");

        Assert.Contains("exactly two network calls", page);
        Assert.Contains("Location search", page);
        Assert.Contains("Update check", page);
    }

    [Fact]
    public void Every_feature_card_has_a_heading_and_a_description()
    {
        string page = RepoFile("docs/index.html");
        var cards = Regex.Matches(page, @"<div class=""card"">\s*<h3>(.+?)</h3>\s*<p>(.+?)</p>", RegexOptions.Singleline);

        Assert.True(cards.Count >= 9, $"expected at least 9 feature cards, found {cards.Count}");
        Assert.All(cards, card => Assert.False(string.IsNullOrWhiteSpace(card.Groups[2].Value)));
    }
}
