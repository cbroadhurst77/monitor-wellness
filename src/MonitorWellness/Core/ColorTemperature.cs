namespace MonitorWellness.Core;

/// <summary>
/// Converts a color temperature in Kelvin to RGB multiplier factors (0.0-1.0 per channel,
/// relative to a neutral white point) using the Tanner Helland blackbody approximation.
/// This is the same approach used by most open-source night-light tools (f.lux, redshift).
/// </summary>
public static class ColorTemperature
{
    /// <summary>
    /// Windows rejects SetDeviceGammaRamp calls once a channel's ramp deviates from identity
    /// by more than roughly a factor of 2 (empirically confirmed on real hardware — see the
    /// Week 1 finding in IMPLEMENTATION.md). Confirmed data points on this hardware: 6500K@50%
    /// (combined factor exactly 0.50) fails; 3400K alone (factor 0.5301) passes; 3000K alone
    /// (factor 0.4310) fails. 0.52 sits strictly between the confirmed-fail 0.50 exact boundary
    /// and 3000K's 0.4310, while still comfortably including 3400K — this app's own NightKelvin
    /// default. A prior value of 0.55 was too conservative: it excluded 3400K itself, which
    /// would have made the settings window's Kelvin validation (added this session) reject the
    /// app's own working default — caught by an automated test, not by hand-testing, which is
    /// exactly the kind of regression these tests exist to catch.
    /// </summary>
    public const double MinSafeChannelFactor = 0.52;

    /// <summary>True if this Kelvin value's minimum RGB channel factor stays above the gamma ramp safety margin on its own (i.e. at full brightness, no additional dimming assist).</summary>
    public static bool IsSafeForGammaRamp(int kelvin)
    {
        var (r, g, b) = KelvinToRgbFactors(kelvin);
        return Math.Min(r, Math.Min(g, b)) >= MinSafeChannelFactor;
    }

    public static (double R, double G, double B) KelvinToRgbFactors(int kelvin)
    {
        double temp = Math.Clamp(kelvin, 1000, 40000) / 100.0;

        double r;
        if (temp <= 66)
        {
            r = 255;
        }
        else
        {
            r = 329.698727446 * Math.Pow(temp - 60, -0.1332047592);
        }

        double g;
        if (temp <= 66)
        {
            g = 99.4708025861 * Math.Log(temp) - 161.1195681661;
        }
        else
        {
            g = 288.1221695283 * Math.Pow(temp - 60, -0.0755148492);
        }

        double b;
        if (temp >= 66)
        {
            b = 255;
        }
        else if (temp <= 19)
        {
            b = 0;
        }
        else
        {
            b = 138.5177312231 * Math.Log(temp - 10) - 305.0447927307;
        }

        r = Math.Clamp(r, 0, 255);
        g = Math.Clamp(g, 0, 255);
        b = Math.Clamp(b, 0, 255);

        return (r / 255.0, g / 255.0, b / 255.0);
    }
}
