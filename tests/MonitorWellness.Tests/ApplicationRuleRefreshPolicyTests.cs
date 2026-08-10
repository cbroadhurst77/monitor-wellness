using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class ApplicationRuleRefreshPolicyTests
{
    [Fact]
    public void UnmatchedWindowChange_DoesNotReapplyDisplayState()
    {
        Assert.False(ApplicationRuleRefreshPolicy.ShouldRefresh(null, nativeDisplayRuleWasActive: false, comfortPlanRuleWasActive: false));
    }

    [Fact]
    public void EnteringOrLeavingAConfiguredRule_RefreshesDisplayState()
    {
        var matchingRule = new ApplicationComfortRule { ProcessName = "winword" };

        Assert.True(ApplicationRuleRefreshPolicy.ShouldRefresh(matchingRule, nativeDisplayRuleWasActive: false, comfortPlanRuleWasActive: false));
        Assert.True(ApplicationRuleRefreshPolicy.ShouldRefresh(null, nativeDisplayRuleWasActive: true, comfortPlanRuleWasActive: false));
        Assert.True(ApplicationRuleRefreshPolicy.ShouldRefresh(null, nativeDisplayRuleWasActive: false, comfortPlanRuleWasActive: true));
    }
}
