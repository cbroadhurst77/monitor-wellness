using Color = System.Windows.Media.Color;

namespace MonitorWellness.Core;

/// <summary>
/// The "push a color temperature to every monitor" primitive MigraineModeController's
/// activate/fade logic depends on, extracted specifically so a test can substitute a fake
/// instead of needing a real GammaControllerManager (which owns live Win32 gamma-ramp device
/// contexts with no seam of their own to fake). GammaControllerManager is the only real
/// implementation — production code passes it through unchanged, since it already matches this
/// shape.
/// </summary>
public interface IColorTemperatureTarget
{
    void ApplyToAll(int kelvin, double contrastReduction);
}

/// <summary>
/// The "push a tint/opacity per monitor" primitive MigraineModeController's activate/fade logic
/// depends on, extracted specifically so a test can substitute a fake instead of needing a real
/// OverlayController (which owns live WPF windows). OverlayController is the only real
/// implementation — production code passes it through unchanged, since it already matches this
/// shape.
/// </summary>
public interface IOverlayTarget
{
    IReadOnlyCollection<string> DeviceNames { get; }
    void Apply(IReadOnlyDictionary<string, (Color Color, double Opacity)> byDevice);
}
