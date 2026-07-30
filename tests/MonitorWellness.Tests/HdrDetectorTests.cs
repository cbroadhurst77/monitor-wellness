using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class HdrDetectorTests
{
    [Theory]
    [InlineData(0b0010u, true)]  // advancedColorEnabled bit set alone
    [InlineData(0b0011u, true)]  // supported + enabled both set
    [InlineData(0b1010u, true)]  // enabled + forceDisabled both set (real Windows wouldn't combine these, but the pure check only cares about bit 1)
    public void HasAdvancedColorEnabledFlag_TrueWhenBitSet(uint flags, bool expected)
    {
        Assert.Equal(expected, HdrDetector.HasAdvancedColorEnabledFlag(flags));
    }

    [Theory]
    [InlineData(0b0000u)]  // nothing set
    [InlineData(0b0001u)]  // only advancedColorSupported set, not enabled
    [InlineData(0b1000u)]  // only advancedColorForceDisabled set
    [InlineData(0b0100u)]  // only wideColorEnforced set
    public void HasAdvancedColorEnabledFlag_FalseWhenBitNotSet(uint flags)
    {
        Assert.False(HdrDetector.HasAdvancedColorEnabledFlag(flags));
    }
}
