using MonitorWellness.Core;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace MonitorWellness.Tests;

sealed class FakeColorTemperatureTarget : IColorTemperatureTarget
{
    public List<(int Kelvin, double ContrastReduction)> Calls { get; } = new();
    public void ApplyToAll(int kelvin, double contrastReduction) => Calls.Add((kelvin, contrastReduction));
}

sealed class FakeOverlayTarget : IOverlayTarget
{
    public IReadOnlyCollection<string> DeviceNames { get; init; } = new[] { "\\\\.\\DISPLAY1", "\\\\.\\DISPLAY2" };
    public List<IReadOnlyDictionary<string, (Color Color, double Opacity)>> Calls { get; } = new();
    public void Apply(IReadOnlyDictionary<string, (Color Color, double Opacity)> byDevice) => Calls.Add(byDevice);
}

/// <summary>
/// MigraineModeController previously had zero test coverage despite owning pure fade/intensity
/// math just as testable as ScheduleCurve's — the only reason was no seam to substitute a fake
/// for its GammaControllerManager/OverlayController dependencies (see HardwareTargets.cs). These
/// tests cover what's synchronously testable without a running Dispatcher message loop:
/// activation's immediate hardware push, mild-intensity scaling, auto-revert arming, and state
/// transitions. FadeTick's tick-by-tick Lerp progression over its real 20-second duration isn't
/// covered here — that would need an injectable clock/timer, a larger change than this fix
/// attempts.
/// </summary>
public class MigraineModeControllerTests
{
    private static AppSettings CreateSettings() => new()
    {
        NightKelvin = 3400,
        MigraineOverlayColorHex = "#173620",
        MigraineOverlayOpacity = 0.72,
        MigraineContrastReduction = 0.15,
    };

    private static MigraineModeController CreateController(
        AppSettings settings,
        FakeColorTemperatureTarget colorTarget,
        FakeOverlayTarget overlayTarget,
        Func<bool>? isForegroundFullscreenLikely = null)
        => new(
            colorTarget,
            overlayTarget,
            settings,
            () => (6500, new Dictionary<string, double>(), System.Windows.Media.Colors.Black),
            isForegroundFullscreenLikely);

    [Fact]
    public void Activate_FullIntensity_PushesConfiguredColorTemperatureAndOverlayTint()
    {
        var settings = CreateSettings();
        var colorTarget = new FakeColorTemperatureTarget();
        var overlayTarget = new FakeOverlayTarget();
        var controller = CreateController(settings, colorTarget, overlayTarget);

        controller.Activate(mild: false);

        var call = Assert.Single(colorTarget.Calls);
        Assert.Equal(3400, call.Kelvin);
        Assert.Equal(0.15, call.ContrastReduction, precision: 5);

        var applied = Assert.Single(overlayTarget.Calls);
        Assert.Equal(overlayTarget.DeviceNames.Count, applied.Count);
        var expectedColor = (Color)ColorConverter.ConvertFromString("#173620")!;
        foreach (var deviceName in overlayTarget.DeviceNames)
        {
            Assert.Equal(0.72, applied[deviceName].Opacity, precision: 5);
            Assert.Equal(expectedColor, applied[deviceName].Color);
        }
    }

    [Fact]
    public void Activate_Mild_ScalesOpacityAndContrastByMildMultiplier()
    {
        var settings = CreateSettings();
        var colorTarget = new FakeColorTemperatureTarget();
        var overlayTarget = new FakeOverlayTarget();
        var controller = CreateController(settings, colorTarget, overlayTarget);

        controller.Activate(mild: true);

        var call = Assert.Single(colorTarget.Calls);
        Assert.Equal(0.15 * 0.6, call.ContrastReduction, precision: 5);

        var applied = Assert.Single(overlayTarget.Calls);
        foreach (var deviceName in overlayTarget.DeviceNames)
            Assert.Equal(0.72 * 0.6, applied[deviceName].Opacity, precision: 5);

        Assert.True(controller.IsMild);
    }

    [Fact]
    public void Activate_SetsIsActiveTrueAndClearsIsFadingOut()
    {
        var controller = CreateController(CreateSettings(), new FakeColorTemperatureTarget(), new FakeOverlayTarget());

        controller.Activate();

        Assert.True(controller.IsActive);
        Assert.False(controller.IsFadingOut);
        Assert.True(controller.SuspendsNormalSchedule);
    }

    [Fact]
    public void Toggle_UsesConfiguredDefaultResponsePlan()
    {
        var settings = CreateSettings();
        settings.DefaultMigraineResponsePlan = MigraineResponsePlans.Gentle;
        var controller = CreateController(settings, new FakeColorTemperatureTarget(), new FakeOverlayTarget());

        controller.Toggle();

        Assert.True(controller.IsActive);
        Assert.True(controller.IsMild);
    }

    [Fact]
    public void Activate_WithAutoRevertDisabled_LeavesAutoRevertAtUtcNull()
    {
        var settings = CreateSettings();
        settings.MigraineAutoRevertMinutes = 0;
        var controller = CreateController(settings, new FakeColorTemperatureTarget(), new FakeOverlayTarget());

        controller.Activate();

        Assert.Null(controller.AutoRevertAtUtc);
    }

    [Fact]
    public void Activate_WithAutoRevertMinutes_ArmsAutoRevertAtUtcApproximatelyNowPlusMinutes()
    {
        var settings = CreateSettings();
        settings.MigraineAutoRevertMinutes = 30;
        var controller = CreateController(settings, new FakeColorTemperatureTarget(), new FakeOverlayTarget());

        var before = DateTime.UtcNow;
        controller.Activate();
        var after = DateTime.UtcNow;

        Assert.NotNull(controller.AutoRevertAtUtc);
        Assert.InRange(controller.AutoRevertAtUtc!.Value, before.AddMinutes(30), after.AddMinutes(30));
    }

    [Fact]
    public void Deactivate_WhenNotActive_IsANoOp()
    {
        var colorTarget = new FakeColorTemperatureTarget();
        var overlayTarget = new FakeOverlayTarget();
        var controller = CreateController(CreateSettings(), colorTarget, overlayTarget);

        controller.Deactivate();

        Assert.False(controller.IsActive);
        Assert.False(controller.IsFadingOut);
        Assert.Empty(colorTarget.Calls);
        Assert.Empty(overlayTarget.Calls);
    }

    [Fact]
    public void Deactivate_WhenActive_StartsFadeImmediately()
    {
        var controller = CreateController(CreateSettings(), new FakeColorTemperatureTarget(), new FakeOverlayTarget());
        controller.Activate();

        controller.Deactivate();

        Assert.False(controller.IsActive);
        Assert.True(controller.IsFadingOut);
        Assert.True(controller.SuspendsNormalSchedule);
        Assert.Null(controller.AutoRevertAtUtc);
    }

    [Fact]
    public void Toggle_FromInactive_Activates()
    {
        var controller = CreateController(CreateSettings(), new FakeColorTemperatureTarget(), new FakeOverlayTarget());

        controller.Toggle();

        Assert.True(controller.IsActive);
    }

    [Fact]
    public void Toggle_FromActive_StartsDeactivate()
    {
        var controller = CreateController(CreateSettings(), new FakeColorTemperatureTarget(), new FakeOverlayTarget());
        controller.Activate();

        controller.Toggle();

        Assert.False(controller.IsActive);
        Assert.True(controller.IsFadingOut);
    }

    [Fact]
    public void StateChanged_FiresOnActivateAndDeactivate()
    {
        var controller = CreateController(CreateSettings(), new FakeColorTemperatureTarget(), new FakeOverlayTarget());
        int fireCount = 0;
        controller.StateChanged += () => fireCount++;

        controller.Activate();
        Assert.Equal(1, fireCount);

        controller.Deactivate();
        Assert.Equal(2, fireCount);
    }

    [Fact]
    public void PossibleFullscreenConflict_FiresWhenForegroundLooksFullscreen()
    {
        var controller = CreateController(
            CreateSettings(), new FakeColorTemperatureTarget(), new FakeOverlayTarget(),
            isForegroundFullscreenLikely: () => true);
        bool fired = false;
        controller.PossibleFullscreenConflict += () => fired = true;

        controller.Activate();

        Assert.True(fired);
    }

    [Fact]
    public void PossibleFullscreenConflict_DoesNotFireWhenNotFullscreen()
    {
        var controller = CreateController(
            CreateSettings(), new FakeColorTemperatureTarget(), new FakeOverlayTarget(),
            isForegroundFullscreenLikely: () => false);
        bool fired = false;
        controller.PossibleFullscreenConflict += () => fired = true;

        controller.Activate();

        Assert.False(fired);
    }
}
