using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("https://github.com/cbroadhurst77/monitor-wellness/releases/tag/v0.2.2", true)]
    [InlineData("http://github.com/cbroadhurst77/monitor-wellness/releases/tag/v0.2.2", false)]
    [InlineData("https://example.com/cbroadhurst77/monitor-wellness/releases/tag/v0.2.2", false)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData("https://github.com/other/repository/releases/tag/v0.2.2", false)]
    public void TrustedReleaseUrl_OnlyAcceptsExpectedGitHubReleasePath(string candidate, bool expected)
    {
        Assert.Equal(expected, UpdateChecker.TryGetTrustedReleaseUrl(candidate, out _));
    }
}
