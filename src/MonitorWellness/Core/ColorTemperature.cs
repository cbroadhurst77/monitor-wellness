namespace MonitorWellness.Core;

/// <summary>
/// Converts a color temperature in Kelvin to RGB multiplier factors (0.0-1.0 per channel,
/// relative to a neutral white point) using the Tanner Helland blackbody approximation.
/// This is the same approach used by most open-source night-light tools (f.lux, redshift).
/// </summary>
public static class ColorTemperature
{
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
