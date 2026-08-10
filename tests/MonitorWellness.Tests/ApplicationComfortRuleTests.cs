using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class ApplicationComfortRuleTests
{
    [Theory]
    [InlineData("Photoshop.exe", "photoshop")]
    [InlineData("code-insiders", "code-insiders")]
    public void ProcessName_IsNormalized(string value, string expected)
    {
        Assert.True(ApplicationComfortRules.TryNormalizeProcessName(value, out string normalized));

        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("C:\\Program Files\\Photoshop.exe")]
    [InlineData("photoshop.exe -argument")]
    public void ProcessName_RejectsPathsAndArguments(string value)
    {
        Assert.False(ApplicationComfortRules.TryNormalizeProcessName(value, out _));
    }

    [Fact]
    public void ForegroundRule_MatchesProcessNameCaseInsensitively()
    {
        var rule = new ApplicationComfortRule { ProcessName = "Photoshop", IsEnabled = true };

        ApplicationComfortRule? match = ApplicationComfortRules.FindForegroundRule(new[] { rule }, "PHOTOSHOP.exe");

        Assert.Same(rule, match);
    }

    [Fact]
    public void DisabledRule_DoesNotMatch()
    {
        var rule = new ApplicationComfortRule { ProcessName = "Photoshop", IsEnabled = false };

        Assert.Null(ApplicationComfortRules.FindForegroundRule(new[] { rule }, "Photoshop"));
    }
}
