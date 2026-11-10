namespace MonitorWellness.Core;

/// <summary>
/// Builds the per-display scheduled brightness map. The device set is supplied by the caller
/// (overlay windows / active monitors), deliberately NOT derived from gamma controllers: a
/// display can have a working compositor overlay but no gamma-ramp device context, and it must
/// still receive scheduled dimming. Colour temperature is the only thing that depends on gamma.
/// </summary>
public static class ScheduleBrightnessTargets
{
    public static Dictionary<string, double> Build(
        IEnumerable<string> deviceNames,
        double globalBrightness,
        IReadOnlySet<string> excludedDevices,
        IReadOnlyDictionary<string, double> multipliers,
        IReadOnlySet<string> primaryDevices)
    {
        ArgumentNullException.ThrowIfNull(deviceNames);
        ArgumentNullException.ThrowIfNull(excludedDevices);
        ArgumentNullException.ThrowIfNull(multipliers);
        ArgumentNullException.ThrowIfNull(primaryDevices);

        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (string deviceName in deviceNames)
        {
            if (excludedDevices.Contains(deviceName))
            {
                result[deviceName] = 1.0;
                continue;
            }

            double multiplier = multipliers.TryGetValue(deviceName, out double m) ? m : 1.0;
            result[deviceName] = BrightnessSafety.CalculateEffectiveBrightness(
                globalBrightness,
                multiplier,
                primaryDevices.Contains(deviceName));
        }
        return result;
    }
}
