using System.Runtime.InteropServices;

namespace MonitorWellness.Core;

/// <summary>
/// Applies color temperature and brightness to a single monitor via the Win32 gamma ramp
/// API (SetDeviceGammaRamp) — the same underlying mechanism f.lux and Windows Night Light
/// use. One instance owns one device context for the lifetime of the app.
/// </summary>
public sealed class GammaRampController : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RAMP
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public ushort[] Red;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public ushort[] Green;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public ushort[] Blue;
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDC(string lpszDriver, string lpszDevice, string? lpszOutput, IntPtr lpInitData);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool SetDeviceGammaRamp(IntPtr hdc, ref RAMP lpRamp);

    public string DeviceName { get; }

    private readonly IntPtr _hdc;
    private bool _disposed;

    public GammaRampController(string deviceName)
    {
        DeviceName = deviceName;
        _hdc = CreateDC("DISPLAY", deviceName, null, IntPtr.Zero);
        if (_hdc == IntPtr.Zero)
            throw new InvalidOperationException($"Could not create a device context for '{deviceName}'.");
    }

    /// <summary>
    /// Windows rejects SetDeviceGammaRamp calls where any channel's ramp deviates from the
    /// identity ramp by more than a factor of 2 (i.e. the effective per-channel multiplier
    /// must stay above ~0.5). Confirmed empirically on real hardware: 3000K alone fails
    /// (blue factor ~0.43), 3400K alone passes (blue factor ~0.53), and combinations that
    /// pass individually can still fail together (3400K + 85% brightness fails, even though
    /// 3400K@100% and 6500K@85% each succeed alone). This means gamma ramp can only be used
    /// for modest brightness assist on top of color temperature — real, visible dimming has
    /// to come from the overlay window layer (Week 2+), not from scaling this ramp further.
    /// </summary>
    private const double MinSafeChannelFactor = 0.55; // small margin above the observed ~0.5 cutoff

    /// <summary>
    /// Sets this monitor's gamma ramp to the given color temperature, with an optional small
    /// brightness assist (0.0-1.0). The assist is silently clamped so the combined per-channel
    /// factor never crosses the driver's rejection threshold — see MinSafeChannelFactor. Do not
    /// rely on this for real dimming; use the overlay window for that.
    /// Returns false if the driver rejected the call outright (e.g. a virtual/remote display
    /// that doesn't support gamma ramps at all) — callers should not treat that as fatal.
    /// </summary>
    public bool ApplyColorTemperature(int kelvin, double brightnessAssist = 1.0)
    {
        var (rFactor, gFactor, bFactor) = ColorTemperature.KelvinToRgbFactors(kelvin);

        // Dimming below this floor risks the whole call being rejected, so requests below
        // it get pushed UP to the floor rather than passed through as-is. If the color
        // temperature alone already puts a channel below the safety margin (e.g. very warm
        // Kelvin values), the floor exceeds 1.0 and no brightness assist can save the call —
        // that's expected: gamma ramp cannot carry both extreme warmth and dimming together,
        // by design (real dimming belongs to the overlay window).
        double minColorFactor = Math.Min(rFactor, Math.Min(gFactor, bFactor));
        double minSafeBrightness = minColorFactor > 0.0 ? MinSafeChannelFactor / minColorFactor : 1.0;
        double clampedBrightness = Math.Clamp(brightnessAssist, Math.Min(1.0, minSafeBrightness), 1.0);

        var ramp = new RAMP { Red = new ushort[256], Green = new ushort[256], Blue = new ushort[256] };

        for (int i = 0; i < 256; i++)
        {
            double baseValue = i * 257.0; // identity ramp is 0-65535 in steps of 257
            ramp.Red[i] = ClampToUShort(baseValue * rFactor * clampedBrightness);
            ramp.Green[i] = ClampToUShort(baseValue * gFactor * clampedBrightness);
            ramp.Blue[i] = ClampToUShort(baseValue * bFactor * clampedBrightness);
        }

        return SetDeviceGammaRamp(_hdc, ref ramp);
    }

    /// <summary>Restores the monitor's default (identity, 6500K, full brightness) gamma ramp.</summary>
    public bool ResetToIdentity()
    {
        var ramp = new RAMP { Red = new ushort[256], Green = new ushort[256], Blue = new ushort[256] };

        for (int i = 0; i < 256; i++)
        {
            ushort v = ClampToUShort(i * 257.0);
            ramp.Red[i] = v;
            ramp.Green[i] = v;
            ramp.Blue[i] = v;
        }

        return SetDeviceGammaRamp(_hdc, ref ramp);
    }

    private static ushort ClampToUShort(double value) => (ushort)Math.Clamp(value, 0.0, 65535.0);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hdc != IntPtr.Zero)
            DeleteDC(_hdc);
    }
}
