using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class GeocodingServiceTests
{
    [Theory]
    [InlineData("51.5072", "-0.1276", "London", true)]
    [InlineData("91", "0", "Invalid latitude", false)]
    [InlineData("0", "181", "Invalid longitude", false)]
    [InlineData("NaN", "0", "Not finite", false)]
    [InlineData("0", "0", "", false)]
    public void TryCreateResult_ValidatesRemoteCoordinatesAndDisplayName(string latitude, string longitude, string displayName, bool expected)
    {
        bool success = GeocodingService.TryCreateResult(latitude, longitude, displayName, out var result);

        Assert.Equal(expected, success);
        Assert.Equal(expected, result is not null);
    }
}
