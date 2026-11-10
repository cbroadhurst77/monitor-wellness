#pragma warning disable CA1861 // small literal arrays in tests are clearer than shared static fields
using System.Diagnostics;
using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class MonitorKeysTests
{
    private static MonitorInfo Monitor(string deviceName, string hardwareId)
        => new(deviceName, "Display", false, hardwareId);

    private static readonly MonitorInfo Reference = Monitor(@"\\.\DISPLAY1", @"\\?\DISPLAY#REF0001#1&1&0&UID1#{guid}");
    private static readonly MonitorInfo Desk = Monitor(@"\\.\DISPLAY2", @"\\?\DISPLAY#DEL0002#1&2&0&UID2#{guid}");

    [Fact]
    public void A_setting_follows_the_physical_monitor_when_windows_renumbers_the_displays()
    {
        var settings = new AppSettings();
        settings.ColorExcludedMonitors.Add(MonitorKeys.For(Reference));

        // After a dock change the reference monitor is DISPLAY2 and the other one is DISPLAY1.
        MonitorInfo referenceRenumbered = Reference with { DeviceName = @"\\.\DISPLAY2" };
        MonitorInfo deskRenumbered = Desk with { DeviceName = @"\\.\DISPLAY1" };

        Assert.True(MonitorKeys.Contains(settings.ColorExcludedMonitors, referenceRenumbered));
        Assert.False(MonitorKeys.Contains(settings.ColorExcludedMonitors, deskRenumbered));
    }

    [Fact]
    public void A_display_name_is_only_trusted_for_a_monitor_with_no_physical_identity()
    {
        var entries = new List<string> { @"\\.\DISPLAY1" };

        Assert.False(MonitorKeys.Contains(entries, Reference));
        Assert.True(MonitorKeys.Contains(entries, MonitorKeys.Unidentified(@"\\.\DISPLAY1")));
    }

    [Fact]
    public void Migration_converts_names_of_connected_monitors_and_leaves_others()
    {
        var settings = new AppSettings();
        settings.ExcludedMonitors.AddRange(new[] { @"\\.\DISPLAY1", @"\\.\DISPLAY9" });
        settings.MonitorDimMultiplier[@"\\.\DISPLAY2"] = 0.8;
        settings.MonitorKelvinOffset[@"\\.\DISPLAY2"] = -300;
        settings.MonitorKelvinOffset[@"\\.\DISPLAY9"] = 100;

        bool changed = MonitorKeys.MigrateLegacyNames(settings, new[] { Reference, Desk });

        Assert.True(changed);
        Assert.Contains(MonitorKeys.For(Reference), settings.ExcludedMonitors);
        Assert.Contains(@"\\.\DISPLAY9", settings.ExcludedMonitors); // not connected: untouched
        Assert.Equal(0.8, settings.MonitorDimMultiplier[MonitorKeys.For(Desk)]);
        Assert.Equal(-300, settings.MonitorKelvinOffset[MonitorKeys.For(Desk)]);
        Assert.Equal(100, settings.MonitorKelvinOffset[@"\\.\DISPLAY9"]);
        Assert.False(settings.MonitorDimMultiplier.ContainsKey(@"\\.\DISPLAY2"));
    }

    [Fact]
    public void Migration_is_a_no_op_the_second_time_and_when_nothing_has_a_physical_identity()
    {
        var settings = new AppSettings();
        settings.ExcludedMonitors.Add(@"\\.\DISPLAY1");

        Assert.False(MonitorKeys.MigrateLegacyNames(settings, new[] { MonitorKeys.Unidentified(@"\\.\DISPLAY1") }));
        Assert.True(MonitorKeys.MigrateLegacyNames(settings, new[] { Reference }));
        Assert.False(MonitorKeys.MigrateLegacyNames(settings, new[] { Reference }));
    }

    [Fact]
    public void Migration_keeps_the_value_already_stored_under_the_physical_key()
    {
        var settings = new AppSettings();
        settings.MonitorDimMultiplier[MonitorKeys.For(Desk)] = 0.5;
        settings.MonitorDimMultiplier[@"\\.\DISPLAY2"] = 0.9;

        MonitorKeys.MigrateLegacyNames(settings, new[] { Desk });

        Assert.Equal(0.5, settings.MonitorDimMultiplier[MonitorKeys.For(Desk)]);
        Assert.Single(settings.MonitorDimMultiplier);
    }

    [Fact]
    public void Saving_settings_keeps_the_entries_of_monitors_that_are_not_connected()
    {
        string undocked = "monitor:" + @"\\?\DISPLAY#GONE#1&9&0&UID9#{GUID}";
        var existing = new List<string> { undocked, MonitorKeys.For(Reference) };

        // The window showed only Desk, and the user ticked it; Reference was unticked elsewhere.
        List<string> merged = MonitorKeys.MergeFlags(existing, new[] { Desk }, new[] { Desk });

        Assert.Contains(undocked, merged);
        Assert.Contains(MonitorKeys.For(Desk), merged);
        Assert.Contains(MonitorKeys.For(Reference), merged); // not shown, so untouched
    }

    [Fact]
    public void Unticking_a_shown_monitor_removes_its_entry()
    {
        var existing = new List<string> { MonitorKeys.For(Desk) };

        List<string> merged = MonitorKeys.MergeFlags(existing, new[] { Desk }, Array.Empty<MonitorInfo>());

        Assert.Empty(merged);
    }

    [Fact]
    public void Merged_values_replace_shown_monitors_and_keep_the_rest()
    {
        var existing = new Dictionary<string, int>
        {
            [MonitorKeys.For(Desk)] = -500,
            [MonitorKeys.For(Reference)] = 200,
        };

        var merged = MonitorKeys.MergeValues(existing, new[] { Desk }, Array.Empty<(MonitorInfo, int)>());

        Assert.False(merged.ContainsKey(MonitorKeys.For(Desk)));
        Assert.Equal(200, merged[MonitorKeys.For(Reference)]);
    }

    [Fact]
    public void The_schedule_resolves_exclusions_and_multipliers_by_physical_identity()
    {
        var settings = new AppSettings();
        settings.ExcludedMonitors.Add(MonitorKeys.For(Reference));
        settings.MonitorDimMultiplier[MonitorKeys.For(Desk)] = 0.7;
        MonitorInfo referenceNow = Reference with { DeviceName = @"\\.\DISPLAY2" };
        MonitorInfo deskNow = Desk with { DeviceName = @"\\.\DISPLAY1" };

        var (excluded, multipliers) = MonitorKeys.ResolveDimSettings(
            settings, new[] { @"\\.\DISPLAY1", @"\\.\DISPLAY2" }, new[] { referenceNow, deskNow });

        Assert.Equal(new[] { @"\\.\DISPLAY2" }, excluded.ToArray());
        Assert.Equal(0.7, multipliers[@"\\.\DISPLAY1"]);
        Assert.False(multipliers.ContainsKey(@"\\.\DISPLAY2"));
    }
}

public class HardwareBrightnessWorkerTests
{
    private sealed class FakeSession : IHardwareBrightnessSession
    {
        public List<double> Applied { get; } = new();
        public bool FailApply { get; set; }
        public bool Disposed { get; private set; }
        public bool TryApplyNormalizedBrightness(double normalizedBrightness, out string error)
        {
            error = FailApply ? "monitor said no" : "";
            if (FailApply) return false;
            lock (Applied) Applied.Add(normalizedBrightness);
            return true;
        }
        public bool TryRefreshHandles(out string error) { error = ""; return true; }
        public void ForgetLastApplied() { }
        public void Dispose() => Disposed = true;
    }

    private sealed class Rig : IDisposable
    {
        public Dictionary<string, FakeSession> Sessions { get; } = new(StringComparer.OrdinalIgnoreCase);
        public ManualResetEventSlim OpenGate { get; } = new(initialState: true);
        public List<HardwareBrightnessApplicationResult> Reported { get; } = new();
        public HardwareBrightnessControllerManager Manager { get; }

        public Rig(TimeSpan? stuckAfter = null)
        {
            Manager = new HardwareBrightnessControllerManager(Open, action => action(), stuckAfter ?? TimeSpan.FromSeconds(30));
            Manager.ResultChanged += result => { lock (Reported) Reported.Add(result); };
        }

        private bool Open(string deviceName, out IHardwareBrightnessSession? session, out string error)
        {
            OpenGate.Wait();
            error = "";
            lock (Sessions)
            {
                if (!Sessions.TryGetValue(deviceName, out FakeSession? fake))
                    Sessions[deviceName] = fake = new FakeSession();
                session = fake;
            }
            return true;
        }

        public void Dispose() => Manager.Dispose();
    }

    private static void WaitFor(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10), "Timed out waiting for the worker.");
            Thread.Sleep(5);
        }
    }

    private static Dictionary<string, double> Level(string device, double value) => new() { [device] = value };

    [Fact]
    public void Requesting_a_change_never_waits_for_a_slow_monitor()
    {
        using var rig = new Rig();
        rig.OpenGate.Reset(); // the monitor's first DDC call hangs

        var timer = Stopwatch.StartNew();
        rig.Manager.RequestApply(new[] { "D1" }, Level("D1", 0.5));
        rig.Manager.RequestApply(new[] { "D1" }, Level("D1", 0.4));
        timer.Stop();

        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(1));
        Assert.Empty(rig.Manager.AppliedDevices);

        rig.OpenGate.Set();
        WaitFor(() => rig.Manager.AppliedDevices.Contains("D1"));
    }

    [Fact]
    public void A_newer_request_replaces_one_that_has_not_started()
    {
        using var rig = new Rig();
        rig.OpenGate.Reset();
        rig.Manager.RequestApply(new[] { "D1" }, Level("D1", 0.9)); // the worker picks this up and blocks in Open
        Thread.Sleep(50);

        for (int i = 1; i <= 5; i++)
            rig.Manager.RequestApply(new[] { "D1" }, Level("D1", 0.1 * i));
        rig.OpenGate.Set();
        WaitFor(() => rig.Sessions.TryGetValue("D1", out FakeSession? s) && s.Applied.Count > 0 && s.Applied[^1] == 0.5);

        FakeSession session = rig.Sessions["D1"];
        lock (session.Applied)
            Assert.True(session.Applied.Count <= 2, $"expected the intermediate requests to be skipped, saw {session.Applied.Count} writes");
    }

    [Fact]
    public void A_hung_monitor_is_reported_as_stuck_so_the_overlay_can_take_over()
    {
        using var rig = new Rig(stuckAfter: TimeSpan.FromMilliseconds(50));
        rig.OpenGate.Reset();

        rig.Manager.RequestApply(new[] { "D1" }, Level("D1", 0.5));
        WaitFor(() => rig.Manager.IsStuck);

        Assert.Empty(rig.Manager.AppliedDevices);
        rig.OpenGate.Set();
        WaitFor(() => !rig.Manager.IsStuck);
        WaitFor(() => rig.Manager.AppliedDevices.Contains("D1"));
    }

    [Fact]
    public void A_change_in_what_is_applied_is_reported_once_not_on_every_tick()
    {
        using var rig = new Rig();

        for (int i = 0; i < 5; i++)
            rig.Manager.RequestApply(new[] { "D1" }, Level("D1", 0.5));
        WaitFor(() => rig.Manager.AppliedDevices.Contains("D1"));
        Thread.Sleep(100);

        lock (rig.Reported)
            Assert.Single(rig.Reported);
    }

    [Fact]
    public void A_failure_is_reported_and_the_monitor_is_given_back()
    {
        using var rig = new Rig();
        rig.Manager.RequestApply(new[] { "D1" }, Level("D1", 0.5));
        WaitFor(() => rig.Manager.AppliedDevices.Contains("D1"));
        rig.Sessions["D1"].FailApply = true;

        rig.Manager.RequestApply(new[] { "D1" }, Level("D1", 0.4));

        WaitFor(() => { lock (rig.Reported) return rig.Reported.Any(result => result.FailuresByDeviceName.ContainsKey("D1")); });
        Assert.True(rig.Sessions["D1"].Disposed);
        Assert.Empty(rig.Manager.AppliedDevices);
    }

    [Fact]
    public void RestoreAll_gives_every_monitor_back_and_waits_only_as_long_as_allowed()
    {
        using var rig = new Rig();
        rig.Manager.RequestApply(new[] { "D1", "D2" }, new Dictionary<string, double> { ["D1"] = 0.5, ["D2"] = 0.6 });
        WaitFor(() => rig.Manager.AppliedDevices.Count == 2);

        Assert.True(rig.Manager.RestoreAll(TimeSpan.FromSeconds(5)));

        Assert.All(rig.Sessions.Values, session => Assert.True(session.Disposed));
        Assert.Empty(rig.Manager.AppliedDevices);
    }

    [Fact]
    public void RestoreAll_does_not_hang_the_caller_when_the_monitor_hangs()
    {
        using var rig = new Rig();
        rig.OpenGate.Reset();
        rig.Manager.RequestApply(new[] { "D1" }, Level("D1", 0.5));
        Thread.Sleep(50);

        var timer = Stopwatch.StartNew();
        bool finished = rig.Manager.RestoreAll(TimeSpan.FromMilliseconds(200));
        timer.Stop();

        Assert.False(finished);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2));
        rig.OpenGate.Set();
    }

    [Fact]
    public void With_nothing_approved_no_work_is_done()
    {
        using var rig = new Rig();

        rig.Manager.RequestApply(Array.Empty<string>(), new Dictionary<string, double>());
        rig.Manager.RefreshAfterWake();

        Assert.Empty(rig.Sessions);
        Assert.True(rig.Manager.RestoreAll(TimeSpan.FromMilliseconds(50)));
    }
}
