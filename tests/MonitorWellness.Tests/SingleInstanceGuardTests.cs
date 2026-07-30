using MonitorWellness.Core;

namespace MonitorWellness.Tests;

/// <summary>
/// Each test uses its own unique mutex name (via the testable constructor overload) so tests
/// never collide with each other, with any real running app, or with a stray leftover from a
/// previous test run under a shared CI machine.
/// </summary>
public class SingleInstanceGuardTests
{
    private static string UniqueName() => $"MonitorWellnessTest-{Guid.NewGuid():N}";

    [Fact]
    public void FirstGuard_IsPrimaryInstance()
    {
        string name = UniqueName();
        using var guard = new SingleInstanceGuard(name);

        Assert.True(guard.IsPrimaryInstance);
    }

    [Fact]
    public void SecondGuardWithSameName_IsNotPrimaryInstance()
    {
        string name = UniqueName();
        using var first = new SingleInstanceGuard(name);
        using var second = new SingleInstanceGuard(name);

        Assert.True(first.IsPrimaryInstance);
        Assert.False(second.IsPrimaryInstance);
    }

    [Fact]
    public void DifferentNames_BothBecomePrimary()
    {
        using var first = new SingleInstanceGuard(UniqueName());
        using var second = new SingleInstanceGuard(UniqueName());

        Assert.True(first.IsPrimaryInstance);
        Assert.True(second.IsPrimaryInstance);
    }

    [Fact]
    public void AfterDisposingFirst_ANewGuardWithTheSameNameCanBecomePrimary()
    {
        string name = UniqueName();
        var first = new SingleInstanceGuard(name);
        Assert.True(first.IsPrimaryInstance);
        first.Dispose();

        using var second = new SingleInstanceGuard(name);
        Assert.True(second.IsPrimaryInstance);
    }

    [Fact]
    public void DisposingANonPrimaryGuard_DoesNotThrow()
    {
        string name = UniqueName();
        using var first = new SingleInstanceGuard(name);
        var second = new SingleInstanceGuard(name);

        Assert.False(second.IsPrimaryInstance);
        var exception = Record.Exception(() => second.Dispose());
        Assert.Null(exception);
    }
}
