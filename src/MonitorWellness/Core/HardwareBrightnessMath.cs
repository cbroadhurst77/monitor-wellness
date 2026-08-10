namespace MonitorWellness.Core;

/// <summary>Pure conversions shared by future hardware-brightness backends.</summary>
public static class HardwareBrightnessMath
{
    public static uint ToNativeBrightness(double normalizedBrightness, uint minimum, uint maximum)
    {
        if (minimum > maximum)
            throw new ArgumentOutOfRangeException(nameof(minimum), "The monitor brightness range is invalid.");

        double clamped = Math.Clamp(normalizedBrightness, 0.0, 1.0);
        return (uint)Math.Round(minimum + (maximum - minimum) * clamped, MidpointRounding.AwayFromZero);
    }
}
