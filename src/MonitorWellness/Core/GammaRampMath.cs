namespace MonitorWellness.Core;

/// <summary>A 3 x 256 gamma ramp, the shape Win32 Get/SetDeviceGammaRamp uses.</summary>
public sealed record GammaRamp(ushort[] Red, ushort[] Green, ushort[] Blue)
{
    public const int Size = 256;

    public bool HasValidShape => Red?.Length == Size && Green?.Length == Size && Blue?.Length == Size;

    /// <summary>The linear ramp (0-65535 in steps of 257): what an uncalibrated display uses.</summary>
    public static GammaRamp Identity()
    {
        var ramp = new GammaRamp(new ushort[Size], new ushort[Size], new ushort[Size]);
        for (int i = 0; i < Size; i++)
        {
            ushort v = (ushort)(i * 257);
            ramp.Red[i] = v;
            ramp.Green[i] = v;
            ramp.Blue[i] = v;
        }
        return ramp;
    }
}

/// <summary>
/// Pure ramp arithmetic, kept free of Win32 so it can be tested. Every ramp this app writes is
/// derived from the display's baseline (whatever was loaded when the app started, typically a
/// colour-calibration curve) instead of from a hard-coded linear ramp, so enabling the schedule
/// no longer discards a calibrated display's correction curve.
/// </summary>
public static class GammaRampMath
{
    /// <summary>How long a display may go without a real write even when nothing seems to have changed.</summary>
    public static readonly TimeSpan MaxTimeWithoutWrite = TimeSpan.FromMinutes(10);

    /// <summary>True when both ramps hold exactly the same values.</summary>
    public static bool AreEqual(GammaRamp? a, GammaRamp? b)
        => a is not null && b is not null
           && a.Red.AsSpan().SequenceEqual(b.Red)
           && a.Green.AsSpan().SequenceEqual(b.Green)
           && a.Blue.AsSpan().SequenceEqual(b.Blue);

    /// <summary>
    /// Whether a write can be skipped: the same ramp was asked for last time, the display still holds
    /// exactly what it held right after that write (so nothing reset it), and the last real write
    /// isn't old. Drivers may round a ramp, so "current" is compared with the ramp read back after
    /// our own write, not with the one requested.
    /// </summary>
    public static bool CanSkipWrite(GammaRamp requested, GammaRamp? lastRequested, GammaRamp? lastReadBack, GammaRamp? current, TimeSpan sinceLastWrite)
        => sinceLastWrite < MaxTimeWithoutWrite
           && AreEqual(requested, lastRequested)
           && lastReadBack is not null
           && AreEqual(current, lastReadBack);

    /// <summary>
    /// Guards against adopting a ramp that is itself a leftover of this app (or another tool)
    /// as the "native" baseline: a warm, dimmed or contrast-raised ramp fails these checks and
    /// the linear identity ramp is used instead. Real calibration curves stay close to linear.
    /// </summary>
    public static bool IsPlausibleBaseline(GammaRamp? ramp)
    {
        if (ramp is null || !ramp.HasValidShape)
            return false;

        return IsPlausibleChannel(ramp.Red) && IsPlausibleChannel(ramp.Green) && IsPlausibleChannel(ramp.Blue);
    }

    private static bool IsPlausibleChannel(ushort[] channel)
    {
        if (channel[0] > 6_000)
            return false; // raised black floor (e.g. migraine contrast reduction)
        if (channel[255] < 58_000)
            return false; // scaled-down white point (warm tint or brightness assist)
        if (channel[128] is < 22_000 or > 44_000)
            return false; // identity is ~32,896
        for (int i = 1; i < channel.Length; i++)
        {
            if (channel[i] + 257 < channel[i - 1])
                return false; // must be (near) monotonic
        }
        return true;
    }

    /// <summary>Baseline scaled per channel by the colour-temperature factors and an overall brightness assist.</summary>
    public static GammaRamp Scale(GammaRamp baseline, (double R, double G, double B) factors, double brightness)
    {
        var ramp = new GammaRamp(new ushort[GammaRamp.Size], new ushort[GammaRamp.Size], new ushort[GammaRamp.Size]);
        for (int i = 0; i < GammaRamp.Size; i++)
        {
            ramp.Red[i] = ClampToUShort(baseline.Red[i] * factors.R * brightness);
            ramp.Green[i] = ClampToUShort(baseline.Green[i] * factors.G * brightness);
            ramp.Blue[i] = ClampToUShort(baseline.Blue[i] * factors.B * brightness);
        }
        return ramp;
    }

    /// <summary>
    /// Baseline with contrast compression (raised black floor) applied to the input position
    /// before the baseline lookup, then scaled by the colour-temperature factors. With the
    /// identity baseline this equals the previous behaviour exactly.
    /// </summary>
    public static GammaRamp ScaleWithContrast(GammaRamp baseline, (double R, double G, double B) factors, double contrastReduction)
    {
        double reduction = Math.Clamp(contrastReduction, 0.0, 1.0);
        var ramp = new GammaRamp(new ushort[GammaRamp.Size], new ushort[GammaRamp.Size], new ushort[GammaRamp.Size]);
        for (int i = 0; i < GammaRamp.Size; i++)
        {
            double position = ColorTemperature.ApplyContrastCompression(i / 255.0, reduction) * 255.0;
            ramp.Red[i] = ClampToUShort(Interpolate(baseline.Red, position) * factors.R);
            ramp.Green[i] = ClampToUShort(Interpolate(baseline.Green, position) * factors.G);
            ramp.Blue[i] = ClampToUShort(Interpolate(baseline.Blue, position) * factors.B);
        }
        return ramp;
    }

    private static double Interpolate(ushort[] channel, double position)
    {
        double clamped = Math.Clamp(position, 0.0, channel.Length - 1);
        int lower = (int)Math.Floor(clamped);
        int upper = Math.Min(lower + 1, channel.Length - 1);
        double fraction = clamped - lower;
        return channel[lower] + (channel[upper] - channel[lower]) * fraction;
    }

    private static ushort ClampToUShort(double value) => (ushort)Math.Clamp(value, 0.0, 65535.0);
}
