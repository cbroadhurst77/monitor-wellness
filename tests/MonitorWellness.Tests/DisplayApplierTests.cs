#pragma warning disable CA1861 // small literal arrays in tests are clearer than shared static fields
using MonitorWellness.Core;
using Color = System.Windows.Media.Color;

namespace MonitorWellness.Tests;

/// <summary>
/// What reaches each display. DisplayApplier owns the rules for colour, brightness and the overlay
/// (exclusions, colour-accurate monitors, HDR and remote displays, hardware brightness and its
/// quarantine), and until its collaborators became interfaces none of it could be tested, which is how
/// a tinted excluded monitor and an HDR display stuck on a migraine tint got past review.
/// Shares a collection with the store tests because a hardware failure saves the settings.
/// </summary>
[Collection("AppDataStores")]
public class DisplayApplierTests
{
    private sealed class FakeGamma : IGammaController
    {
        public FakeGamma(string deviceName) => DeviceName = deviceName;
        public string DeviceName { get; }
        public int LastWin32Error => 0;
        public bool ApplyResult { get; set; } = true;
        public bool Modified { get; set; }
        public List<int> Applied { get; } = new();
        public int Restores { get; private set; }

        public bool ApplyColorTemperature(int kelvin, double brightnessAssist = 1.0)
        {
            Applied.Add(kelvin);
            if (ApplyResult)
                Modified = true;
            return ApplyResult;
        }

        public bool ApplyColorTemperatureWithContrast(int kelvin, double contrastReduction) => ApplyColorTemperature(kelvin);

        public bool RestoreBaseline()
        {
            Restores++;
            Modified = false;
            return true;
        }

        public bool RestoreBaselineIfModified() => !Modified || RestoreBaseline();
    }

    private sealed class FakeGammaSource : IGammaSource
    {
        public List<FakeGamma> Fakes { get; } = new();
        public IReadOnlyCollection<IGammaController> Controllers => Fakes;
        public int RestoreAllCalls { get; private set; }
        public void RestoreAllToBaseline() => RestoreAllCalls++;
    }

    private sealed class FakeOverlay : IDimOverlay
    {
        public Dictionary<string, double>? LastDim { get; private set; }
        public Color LastColor { get; private set; }
        public int Clears { get; private set; }
        public void ApplyDim(IReadOnlyDictionary<string, double> brightnessByDevice, Color dimColor)
        {
            LastDim = new Dictionary<string, double>(brightnessByDevice, StringComparer.OrdinalIgnoreCase);
            LastColor = dimColor;
        }
        public void Clear() => Clears++;
    }

    private sealed class FakeHardware : IHardwareBrightness
    {
        public event Action<HardwareBrightnessApplicationResult>? ResultChanged;
        public IReadOnlySet<string> AppliedDevices { get; set; } = new HashSet<string>();
        public List<string>? LastApproved { get; private set; }
        public Dictionary<string, double>? LastTargets { get; private set; }
        public int RestoreAllCalls { get; private set; }
        public void RequestApply(IReadOnlyCollection<string> approvedDevices, IReadOnlyDictionary<string, double> brightnessByDevice)
        {
            LastApproved = approvedDevices.ToList();
            LastTargets = new Dictionary<string, double>(brightnessByDevice, StringComparer.OrdinalIgnoreCase);
        }
        public bool RestoreAll(TimeSpan timeout) { RestoreAllCalls++; return true; }
        public void Raise(HardwareBrightnessApplicationResult result) => ResultChanged?.Invoke(result);
    }

    private sealed class FakeMonitors : IMonitorSource
    {
        public List<MonitorInfo> Active { get; set; } = new();
        public IReadOnlyList<MonitorInfo> GetActiveMonitors() => Active;
    }

    private static readonly MonitorInfo MonitorA = new(@"\\.\DISPLAY1", "Display A", true, @"\\?\DISPLAY#AAA0001#1&1&0&UID1#{guid}");
    private static readonly MonitorInfo MonitorB = new(@"\\.\DISPLAY2", "Display B", false, @"\\?\DISPLAY#BBB0002#1&2&0&UID2#{guid}");

    private sealed class Rig
    {
        public AppSettings Settings { get; } = new();
        public FakeGammaSource Gamma { get; } = new();
        public FakeOverlay Overlay { get; } = new();
        public FakeHardware Hardware { get; } = new();
        public FakeMonitors Monitors { get; } = new();
        public DisplayApplier Applier { get; }
        public List<(string Device, string Reason, bool IsHdr)> Fallbacks { get; } = new();
        public int HardwareChanges { get; private set; }

        public Rig(params MonitorInfo[] monitors)
        {
            Monitors.Active = monitors.ToList();
            foreach (MonitorInfo monitor in monitors)
                Gamma.Fakes.Add(new FakeGamma(monitor.DeviceName));
            Applier = new DisplayApplier(Settings, Gamma, Overlay, Hardware, Monitors);
            Applier.CompatibilityFallbackStarted += (device, reason, isHdr) => Fallbacks.Add((device, reason, isHdr));
            Applier.HardwareBrightnessChanged += () => HardwareChanges++;
        }

        public FakeGamma For(string deviceName) => Gamma.Fakes.Single(fake => fake.DeviceName == deviceName);

        public void Apply(int kelvin = 5000, double dim = 0.8)
            => Applier.Apply(kelvin, Monitors.Active.ToDictionary(m => m.DeviceName, _ => dim), Color.FromRgb(0, 0, 0));
    }

    [Fact]
    public void Every_normal_display_gets_the_schedule_colour()
    {
        var rig = new Rig(MonitorA, MonitorB);

        rig.Apply(5000);

        Assert.Equal(new[] { 5000 }, rig.For(MonitorA.DeviceName).Applied);
        Assert.Equal(new[] { 5000 }, rig.For(MonitorB.DeviceName).Applied);
    }

    [Fact]
    public void A_colour_offset_follows_the_physical_monitor_when_windows_renumbers_the_displays()
    {
        var rig = new Rig();
        rig.Settings.MonitorKelvinOffset[MonitorKeys.For(MonitorB)] = -300;
        // After a dock change the monitor that was DISPLAY2 is now DISPLAY1, and the other one DISPLAY2.
        MonitorInfo bNowFirst = MonitorB with { DeviceName = @"\\.\DISPLAY1" };
        MonitorInfo aNowSecond = MonitorA with { DeviceName = @"\\.\DISPLAY2" };
        rig.Monitors.Active = new List<MonitorInfo> { bNowFirst, aNowSecond };
        rig.Gamma.Fakes.Add(new FakeGamma(@"\\.\DISPLAY1"));
        rig.Gamma.Fakes.Add(new FakeGamma(@"\\.\DISPLAY2"));

        rig.Apply(5000);

        Assert.Equal(new[] { 4700 }, rig.For(@"\\.\DISPLAY1").Applied); // monitor B, wherever Windows has put it
        Assert.Equal(new[] { 5000 }, rig.For(@"\\.\DISPLAY2").Applied);
    }

    [Fact]
    public void An_excluded_monitor_is_not_tinted_and_is_handed_back_once_if_it_had_been()
    {
        var rig = new Rig(MonitorA, MonitorB);
        rig.Settings.ExcludedMonitors.Add(MonitorKeys.For(MonitorB));
        rig.For(MonitorB.DeviceName).Modified = true; // for instance tinted before it was excluded, or by migraine mode

        rig.Apply();
        rig.Apply();

        FakeGamma b = rig.For(MonitorB.DeviceName);
        Assert.Empty(b.Applied);
        Assert.False(b.Modified);
        Assert.Equal(1, b.Restores);   // restored once, then left alone: no ramp write on every tick
        Assert.Equal(2, rig.For(MonitorA.DeviceName).Applied.Count);
    }

    [Fact]
    public void A_colour_accurate_monitor_keeps_its_native_colour_but_still_dims()
    {
        var rig = new Rig(MonitorA, MonitorB);
        rig.Settings.ColorExcludedMonitors.Add(MonitorKeys.For(MonitorA));
        rig.For(MonitorA.DeviceName).Modified = true;

        rig.Apply(5000, dim: 0.7);

        Assert.Empty(rig.For(MonitorA.DeviceName).Applied);
        Assert.False(rig.For(MonitorA.DeviceName).Modified);
        Assert.Equal(0.7, rig.Overlay.LastDim![MonitorA.DeviceName]);
    }

    [Fact]
    public void An_hdr_display_is_left_to_the_overlay_and_anything_migraine_mode_put_on_it_is_taken_off_every_time()
    {
        MonitorInfo hdr = MonitorB with { IsHdr = true };
        var rig = new Rig(MonitorA, hdr);

        rig.For(hdr.DeviceName).Modified = true;
        rig.Apply();
        Assert.Empty(rig.For(hdr.DeviceName).Applied);
        Assert.False(rig.For(hdr.DeviceName).Modified);

        rig.For(hdr.DeviceName).Modified = true; // migraine mode writes a ramp to every display, including this one
        rig.Apply();

        Assert.False(rig.For(hdr.DeviceName).Modified);
        Assert.Equal(2, rig.For(hdr.DeviceName).Restores);
        Assert.Single(rig.Fallbacks);            // the person is told once, not on every tick
        Assert.True(rig.Fallbacks[0].IsHdr);
    }

    [Fact]
    public void With_the_compatibility_preference_off_an_hdr_display_is_tinted_like_any_other()
    {
        MonitorInfo hdr = MonitorB with { IsHdr = true };
        var rig = new Rig(MonitorA, hdr);
        rig.Settings.PreferOverlayOnlyOnCompatibilityDisplays = false;

        rig.Apply();

        Assert.Single(rig.For(hdr.DeviceName).Applied);
        Assert.Empty(rig.Fallbacks);
    }

    [Fact]
    public void A_rejected_colour_write_does_not_stop_the_other_displays()
    {
        var rig = new Rig(MonitorA, MonitorB);
        rig.For(MonitorA.DeviceName).ApplyResult = false;

        Assert.Null(Record.Exception(() => rig.Apply()));

        Assert.Single(rig.For(MonitorB.DeviceName).Applied);
        Assert.NotNull(rig.Overlay.LastDim);
    }

    [Fact]
    public void Only_approved_non_excluded_non_compatibility_monitors_are_offered_to_hardware_brightness()
    {
        MonitorInfo hdr = new("\\\\.\\DISPLAY3", "Display C", false, "\\\\?\\DISPLAY#CCC0003#1&3&0&UID3#{guid}", IsHdr: true);
        var rig = new Rig(MonitorA, MonitorB, hdr);
        foreach (MonitorInfo monitor in new[] { MonitorA, MonitorB, hdr })
            rig.Settings.HardwareBrightnessSafetyByMonitor[HardwareBrightnessSafety.GetKey(monitor)!] = new HardwareBrightnessSafetyState { IsApproved = true };
        rig.Settings.ExcludedMonitors.Add(MonitorKeys.For(MonitorA));

        rig.Apply();

        Assert.Equal(new[] { MonitorB.DeviceName }, rig.Hardware.LastApproved);
    }

    [Fact]
    public void A_display_whose_physical_brightness_is_set_is_not_also_dimmed_by_the_overlay()
    {
        var rig = new Rig(MonitorA, MonitorB);
        rig.Hardware.AppliedDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { MonitorB.DeviceName };

        rig.Apply(5000, dim: 0.6);

        Assert.Equal(0.6, rig.Overlay.LastDim![MonitorA.DeviceName]);
        Assert.Equal(1.0, rig.Overlay.LastDim![MonitorB.DeviceName]);
        Assert.Equal(0.6, rig.Hardware.LastTargets![MonitorB.DeviceName]); // hardware is still asked for the real target
    }

    [Fact]
    public void A_hardware_failure_quarantines_that_monitor_and_asks_for_the_schedule_to_be_reapplied()
    {
        var rig = new Rig(MonitorA, MonitorB);
        string key = HardwareBrightnessSafety.GetKey(MonitorB)!;
        rig.Settings.HardwareBrightnessSafetyByMonitor[key] = new HardwareBrightnessSafetyState { IsApproved = true };

        rig.Hardware.Raise(new HardwareBrightnessApplicationResult(
            new HashSet<string>(),
            new Dictionary<string, string> { [MonitorB.DeviceName] = "the monitor stopped answering" }));

        HardwareBrightnessSafetyState state = rig.Settings.HardwareBrightnessSafetyByMonitor[key];
        Assert.True(state.IsQuarantined);
        Assert.False(HardwareBrightnessSafety.IsApproved(rig.Settings, MonitorB, DateTime.UtcNow));
        Assert.Equal(1, rig.HardwareChanges);
    }

    [Fact]
    public void Restoring_native_gives_back_physical_brightness_the_overlay_and_every_colour_ramp()
    {
        var rig = new Rig(MonitorA, MonitorB);

        rig.Applier.RestoreNative();

        Assert.Equal(1, rig.Hardware.RestoreAllCalls);
        Assert.Equal(1, rig.Overlay.Clears);
        Assert.Equal(1, rig.Gamma.RestoreAllCalls);
    }

    [Fact]
    public void The_dim_colour_reaches_the_overlay()
    {
        var rig = new Rig(MonitorA);

        rig.Applier.Apply(5000, new Dictionary<string, double> { [MonitorA.DeviceName] = 0.9 }, Color.FromRgb(40, 20, 10));

        Assert.Equal(Color.FromRgb(40, 20, 10), rig.Overlay.LastColor);
    }
}
