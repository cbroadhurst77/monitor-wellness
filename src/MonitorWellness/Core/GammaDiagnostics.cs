namespace MonitorWellness.Core;

/// <summary>
/// Plain-language reasons for a rejected gamma-ramp write. The log used to say every rejection
/// was "likely below this hardware's safe floor", but the rejected values in real logs were
/// 5,400K and 5,000K, which are nowhere near the floor: those were transient failures (a stale
/// device context after a desktop or session change). Saying which one it was makes the log
/// useful instead of misleading.
/// </summary>
public static class GammaDiagnostics
{
    /// <summary>Describes why a write at <paramref name="kelvin"/> was rejected. Pure and testable.</summary>
    public static string DescribeRejection(int kelvin, int win32Error)
    {
        string code = win32Error == 0 ? "no Win32 error reported" : $"Win32 error {win32Error}";
        return ColorTemperature.IsSafeForGammaRamp(kelvin)
            ? $"{kelvin}K is inside the safe range, so this looks like a transient display or session state ({code}); it will be retried on the next tick"
            : $"{kelvin}K is below the safe range for gamma ramps ({code}); choose a cooler value";
    }
}
