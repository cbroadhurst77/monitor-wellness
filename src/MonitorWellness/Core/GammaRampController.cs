using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MonitorWellness.Core;

/// <summary>
/// Applies color temperature and brightness to a single monitor via the Win32 gamma ramp
/// API (SetDeviceGammaRamp) — the same underlying mechanism f.lux and Windows Night Light
/// use. One instance owns one device context for the lifetime of the app.
///
/// On creation it reads the display's current ramp as its <see cref="Baseline"/> (typically a
/// colour-calibration curve loaded by Windows) and every ramp it writes is derived from that
/// baseline, and restoring puts the baseline back. If the current ramp does not look like a
/// native one (see <see cref="GammaRampMath.IsPlausibleBaseline"/>) the linear identity ramp is
/// used instead, so a leftover warm/dim ramp is never adopted as "normal".
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

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool GetDeviceGammaRamp(IntPtr hdc, ref RAMP lpRamp);

    public string DeviceName { get; }

    /// <summary>The Win32 error from the most recent rejected write (after the retry), or 0. For diagnostics only.</summary>
    public int LastWin32Error { get; private set; }

    /// <summary>The ramp this display had when the app took control of it; what "native" means for restore.</summary>
    public GammaRamp Baseline { get; }

    /// <summary>True if <see cref="Baseline"/> was read from the display, false if it fell back to the linear identity ramp.</summary>
    public bool BaselineWasCaptured { get; }

    private IntPtr _hdc;
    private bool _disposed;

    // What the last real write asked for, what the display held right after it, and when. Lets a
    // repeat of the same request skip the (about 10 ms) driver write when nothing has changed.
    private GammaRamp? _lastRequested;

    // True while the display holds a ramp we wrote that is not the baseline. Separate from
    // _lastRequested, which is cleared by a context refresh or a failed write although the display
    // still holds our colour, so it cannot say whether there is anything to undo.
    private bool _rampModified;
    private GammaRamp? _lastReadBack;
    private long _lastWriteTicks;

    /// <summary>Real driver writes performed (skipped repeats don't count). For diagnostics and tests.</summary>
    public int WriteCount { get; private set; }

    public GammaRampController(string deviceName)
    {
        DeviceName = deviceName;
        _hdc = CreateDC("DISPLAY", deviceName, null, IntPtr.Zero);
        if (_hdc == IntPtr.Zero)
            throw new InvalidOperationException($"Could not create a device context for '{deviceName}'.");

        GammaRamp? captured = TryReadRamp(_hdc);
        if (GammaRampMath.IsPlausibleBaseline(captured))
        {
            Baseline = captured!;
            BaselineWasCaptured = true;
        }
        else
        {
            Baseline = GammaRamp.Identity();
            DebugLog.Write(captured is null
                ? $"GammaRampController: couldn't read the current ramp for {deviceName}; using the linear identity ramp as its baseline."
                : $"GammaRampController: the current ramp for {deviceName} doesn't look native; using the linear identity ramp as its baseline.");
        }
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
    /// The threshold itself lives on ColorTemperature (shared with the settings window's
    /// input validation — confirmed necessary in practice when a user entered 2500K, which
    /// silently failed on this hardware; see IMPLEMENTATION.md).
    /// </summary>
    private const double MinSafeChannelFactor = ColorTemperature.MinSafeChannelFactor;

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

        return Write(GammaRampMath.Scale(Baseline, (rFactor, gFactor, bFactor), clampedBrightness));
    }

    /// <summary>
    /// Sets this monitor's gamma ramp to the given color temperature with contrast reduction
    /// (raising the black floor toward the white ceiling by contrastReduction, 0.0-1.0) —
    /// used by migraine mode. Deliberately does not accept a brightnessAssist parameter:
    /// confirmed on real hardware (tools/GammaCheck) that a raised floor alone is safe up to
    /// at least 0.30 even at this app's warmest safe Kelvin, but combining it with a scaled-
    /// down ceiling (brightness assist) reintroduces the old rejection failure. Keeping this
    /// method contrast-only enforces that separation at the API level rather than relying on
    /// callers to remember not to combine them.
    /// </summary>
    public bool ApplyColorTemperatureWithContrast(int kelvin, double contrastReduction)
    {
        var (rFactor, gFactor, bFactor) = ColorTemperature.KelvinToRgbFactors(kelvin);
        return Write(GammaRampMath.ScaleWithContrast(Baseline, (rFactor, gFactor, bFactor), contrastReduction));
    }

    /// <summary>
    /// Replaces the device context with a fresh one. A context can go stale across a display-mode
    /// or layout change (the rejected writes in real logs came seconds after such changes), so the
    /// manager refreshes every context when the layout changes. Keeps the old one if a fresh one
    /// can't be made.
    /// </summary>
    public void RefreshDeviceContext()
    {
        if (_disposed)
            return;

        IntPtr fresh = CreateDC("DISPLAY", DeviceName, null, IntPtr.Zero);
        if (fresh == IntPtr.Zero)
            return;

        _lastRequested = null;   // a new context: write for real next time
        IntPtr old = _hdc;
        _hdc = fresh;
        if (old != IntPtr.Zero)
            DeleteDC(old);
    }

    /// <summary>Restores the ramp this monitor had when the app took control of it (its calibration curve, or linear if none was detected).</summary>
    public bool RestoreBaseline()
    {
        bool ok = Write(Baseline);
        _lastRequested = null;   // restored means hands off: the quick reset check must not put our colour back
        return ok;
    }

    /// <summary>
    /// Restores the baseline only if this controller has written a ramp since it last restored
    /// (migraine mode writes ramps too, outside the schedule). Safe to call every tick for
    /// displays that are meant to stay native: it costs nothing when there is nothing to undo.
    /// </summary>
    public bool RestoreBaselineIfModified() => !_rampModified || RestoreBaseline();

    /// <summary>
    /// A cheap check (one ramp read, about 1.5 ms) for another program having reset this display's
    /// colour since we last set it. If so, writes our last requested ramp again and returns true.
    /// Does nothing before the first apply or after a restore.
    /// </summary>
    public bool RepairIfReset()
    {
        if (_disposed || _lastRequested is null || _lastReadBack is null)
            return false;

        GammaRamp? current = TryReadRamp(_hdc);
        if (current is null || GammaRampMath.AreEqual(current, _lastReadBack))
            return false;

        if (!WriteForReal(_lastRequested))
            return false;

        _lastReadBack = TryReadRamp(_hdc);
        _lastWriteTicks = Stopwatch.GetTimestamp();
        return true;
    }

    /// <summary>Writes a ramp to a display by device name without owning a controller; used by crash recovery.</summary>
    public static bool TryWriteRamp(string deviceName, GammaRamp ramp)
    {
        if (!ramp.HasValidShape)
            return false;

        IntPtr hdc = CreateDC("DISPLAY", deviceName, null, IntPtr.Zero);
        if (hdc == IntPtr.Zero)
            return false;
        try
        {
            return SetRamp(hdc, ramp);
        }
        finally
        {
            DeleteDC(hdc);
        }
    }

    private bool Write(GammaRamp ramp)
    {
        if (_lastRequested is not null && _lastReadBack is not null
            && GammaRampMath.CanSkipWrite(ramp, _lastRequested, _lastReadBack, TryReadRamp(_hdc), Stopwatch.GetElapsedTime(_lastWriteTicks)))
        {
            LastWin32Error = 0;
            return true;
        }

        _lastRequested = null;
        if (!WriteForReal(ramp))
            return false;

        _rampModified = !ReferenceEquals(ramp, Baseline);
        _lastRequested = ramp;
        _lastReadBack = TryReadRamp(_hdc);   // null if unreadable: then nothing is ever skipped
        _lastWriteTicks = Stopwatch.GetTimestamp();
        return true;
    }

    private bool WriteForReal(GammaRamp ramp)
    {
        WriteCount++;
        if (SetRamp(_hdc, ramp))
        {
            LastWin32Error = 0;
            return true;
        }

        int firstError = Marshal.GetLastWin32Error();

        // A device context can go stale after a desktop, session or display-mode change, and a
        // write through it fails even for perfectly safe values (the logs showed 5,000-5,400K
        // being "rejected"). Create a fresh one and try once more before reporting a failure.
        IntPtr fresh = CreateDC("DISPLAY", DeviceName, null, IntPtr.Zero);
        if (fresh != IntPtr.Zero)
        {
            if (SetRamp(fresh, ramp))
            {
                DeleteDC(_hdc);
                _hdc = fresh;
                LastWin32Error = 0;
                DebugLog.Write($"GammaRampController: write to {DeviceName} failed (Win32 error {firstError}) and succeeded after re-creating its device context.");
                return true;
            }
            DeleteDC(fresh);
        }

        LastWin32Error = firstError;
        return false;
    }

    private static bool SetRamp(IntPtr hdc, GammaRamp ramp)
    {
        var native = new RAMP { Red = ramp.Red, Green = ramp.Green, Blue = ramp.Blue };
        return SetDeviceGammaRamp(hdc, ref native);
    }

    private static GammaRamp? TryReadRamp(IntPtr hdc)
    {
        var native = new RAMP { Red = new ushort[256], Green = new ushort[256], Blue = new ushort[256] };
        return GetDeviceGammaRamp(hdc, ref native)
            ? new GammaRamp(native.Red, native.Green, native.Blue)
            : null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hdc != IntPtr.Zero)
            DeleteDC(_hdc);
    }
}
