using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class MigraineResponsePlansTests
{
    [Theory]
    [InlineData(MigraineResponsePlans.Gentle, true)]
    [InlineData(MigraineResponsePlans.Strong, false)]
    public void NamedPlans_MapToExpectedIntensity(string plan, bool isMild)
    {
        Assert.True(MigraineResponsePlans.IsSupported(plan));
        Assert.Equal(isMild, MigraineResponsePlans.IsMild(plan));
    }

    [Fact]
    public void UnknownPlan_IsNotSupported()
    {
        Assert.False(MigraineResponsePlans.IsSupported("MedicalTreatment"));
    }
}
