using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class HardwareBrightnessMathTests
{
    [Theory]
    [InlineData(0.0, 10u, 90u, 10u)]
    [InlineData(0.5, 10u, 90u, 50u)]
    [InlineData(1.0, 10u, 90u, 90u)]
    [InlineData(-1.0, 10u, 90u, 10u)]
    [InlineData(2.0, 10u, 90u, 90u)]
    public void ToNativeBrightness_MapsAndClampsNormalizedValues(double normalized, uint minimum, uint maximum, uint expected)
    {
        Assert.Equal(expected, HardwareBrightnessMath.ToNativeBrightness(normalized, minimum, maximum));
    }

    [Fact]
    public void ToNativeBrightness_RejectsInvalidRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HardwareBrightnessMath.ToNativeBrightness(0.5, 90, 10));
    }
}
