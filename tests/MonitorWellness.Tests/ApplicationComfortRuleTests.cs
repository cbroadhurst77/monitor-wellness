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

    [Fact]
    public void RuleWithWindowTitle_MatchesOnlyItsNamedWorkspace()
    {
        var rule = new ApplicationComfortRule
        {
            ProcessName = "powerpnt",
            WindowTitleContains = "Quarterly review",
        };

        Assert.NotNull(ApplicationComfortRules.FindForegroundRule(new[] { rule }, "POWERPNT.EXE", "Quarterly Review - PowerPoint"));
        Assert.Null(ApplicationComfortRules.FindForegroundRule(new[] { rule }, "POWERPNT.EXE", "Team planning - PowerPoint"));
    }

    [Fact]
    public void TitleScopedRule_TakesPrecedenceOverProcessWideRule()
    {
        var generic = new ApplicationComfortRule { ProcessName = "teams" };
        var meeting = new ApplicationComfortRule { ProcessName = "teams", WindowTitleContains = "Quarterly review" };

        ApplicationComfortRule? matched = ApplicationComfortRules.FindForegroundRule(
            new[] { generic, meeting }, "teams.exe", "Quarterly review - Microsoft Teams");

        Assert.Same(meeting, matched);
        Assert.True(AppSettingsValidator.TryValidate(new AppSettings
        {
            ApplicationComfortRules = new List<ApplicationComfortRule> { generic, meeting },
        }, out _));
    }

    [Fact]
    public void ComfortPlanRule_IsMatchedAndValidated()
    {
        var rule = new ApplicationComfortRule
        {
            ProcessName = "winword",
            Action = ApplicationComfortActions.ApplySensoryComfortPlan,
            ComfortPlanName = SensoryComfortPlans.Reading,
        };

        Assert.Same(rule, ApplicationComfortRules.FindForegroundRule(new[] { rule }, "winword.exe"));
        Assert.True(AppSettingsValidator.TryValidate(new AppSettings
        {
            ApplicationComfortRules = new List<ApplicationComfortRule> { rule },
        }, out _));
    }
}
