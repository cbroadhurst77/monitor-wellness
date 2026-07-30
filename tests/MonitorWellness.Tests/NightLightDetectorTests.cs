using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class NightLightDetectorTests
{
    [Theory]
    [InlineData("flux")]
    [InlineData("Flux")]
    [InlineData("FLUX")]
    [InlineData("flux.exe")]
    [InlineData("Flux.EXE")]
    [InlineData("  flux  ")]
    public void MatchesKnownConflictingProcessName_TrueForFluxVariants(string processName)
    {
        Assert.True(NightLightDetector.MatchesKnownConflictingProcessName(processName));
    }

    [Theory]
    [InlineData("explorer")]
    [InlineData("MonitorWellness")]
    [InlineData("fluxion")] // must not substring-match — "flux" is not a prefix match target
    [InlineData("")]
    [InlineData("notepad.exe")]
    public void MatchesKnownConflictingProcessName_FalseForUnrelatedNames(string processName)
    {
        Assert.False(NightLightDetector.MatchesKnownConflictingProcessName(processName));
    }
}
