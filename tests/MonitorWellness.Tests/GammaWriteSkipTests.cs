using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class GammaWriteSkipTests
{
    private static GammaRamp Ramp(ushort bump = 0)
    {
        GammaRamp r = GammaRamp.Identity();
        r.Blue[100] += bump;
        return r;
    }

    private static readonly TimeSpan Recent = TimeSpan.FromSeconds(30);

    [Fact]
    public void AreEqual_ComparesEveryChannel()
    {
        Assert.True(GammaRampMath.AreEqual(Ramp(), Ramp()));
        Assert.False(GammaRampMath.AreEqual(Ramp(), Ramp(1)));
        Assert.False(GammaRampMath.AreEqual(Ramp(), null));
        Assert.False(GammaRampMath.AreEqual(null, null));
    }

    [Fact]
    public void SameRequestAndUntouchedDisplay_SkipsTheWrite()
        => Assert.True(GammaRampMath.CanSkipWrite(Ramp(1), Ramp(1), Ramp(2), Ramp(2), Recent));

    [Fact]
    public void DifferentRequest_Writes()
        => Assert.False(GammaRampMath.CanSkipWrite(Ramp(1), Ramp(2), Ramp(2), Ramp(2), Recent));

    [Fact]
    public void DisplayWasResetByAnotherProgram_Writes()
        => Assert.False(GammaRampMath.CanSkipWrite(Ramp(1), Ramp(1), Ramp(2), Ramp(), Recent));

    [Fact]
    public void UnreadableDisplayOrNoPriorWrite_Writes()
    {
        Assert.False(GammaRampMath.CanSkipWrite(Ramp(1), Ramp(1), Ramp(2), null, Recent));
        Assert.False(GammaRampMath.CanSkipWrite(Ramp(1), null, Ramp(2), Ramp(2), Recent));
        Assert.False(GammaRampMath.CanSkipWrite(Ramp(1), Ramp(1), null, Ramp(2), Recent));
    }

    [Fact]
    public void AnOldWrite_IsRefreshedEvenIfNothingLooksChanged()
        => Assert.False(GammaRampMath.CanSkipWrite(Ramp(1), Ramp(1), Ramp(2), Ramp(2), GammaRampMath.MaxTimeWithoutWrite));
}
