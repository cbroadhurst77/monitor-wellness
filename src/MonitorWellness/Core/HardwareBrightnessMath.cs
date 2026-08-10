namespace MonitorWellness.Core;

/// <summary>Pure conversions shared by future hardware-brightness backends.</summary>
public static class HardwareBrightnessMath
{
    public const double MinimumSafeTestBrightness = 0.20;
    private const double TestBrightnessReduction = 0.15;

    public static uint ToNativeBrightness(double normalizedBrightness, uint minimum, uint maximum)
    {
        if (minimum > maximum)
            throw new ArgumentOutOfRangeException(nameof(minimum), "The monitor brightness range is invalid.");

        double clamped = Math.Clamp(normalizedBrightness, 0.0, 1.0);
        return (uint)Math.Round(minimum + (maximum - minimum) * clamped, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Creates a conservative, dimmer-only hardware test value. A monitor already at or near
    /// the safe floor is not tested because increasing its backlight could be an abrupt flash.
    /// </summary>
    public static bool TryGetSafeTestBrightness(double currentNormalizedBrightness, out double testNormalizedBrightness)
    {
        if (currentNormalizedBrightness <= MinimumSafeTestBrightness + TestBrightnessReduction)
        {
            testNormalizedBrightness = currentNormalizedBrightness;
            return false;
        }

        testNormalizedBrightness = currentNormalizedBrightness - TestBrightnessReduction;
        return true;
    }
}
