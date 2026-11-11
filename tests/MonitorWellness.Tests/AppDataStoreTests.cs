using System.IO;
using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Text;
using MonitorWellness.Core;

namespace MonitorWellness.Tests;

/// <summary>
/// Real round trips through the stores that used to be untestable (their folder was hard-coded to
/// %AppData%): settings load/fallback/backup, profiles, history, the diagnostic bundle. The
/// collection attribute serialises these tests, because they share one folder.
/// </summary>
[Collection("AppDataStores")]
public sealed class AppDataStoreTests : IDisposable
{
    private static string Folder => AppDataFolder.FullPath;

    public AppDataStoreTests() => Reset();

    public void Dispose() => Reset();

    private static void Reset()
    {
        Directory.CreateDirectory(Folder);
        foreach (string file in Directory.GetFiles(Folder))
        {
            string name = Path.GetFileName(file);
            if (!string.Equals(name, "debug.log", StringComparison.OrdinalIgnoreCase))
                File.Delete(file);
        }
        string profiles = Path.Combine(Folder, "Profiles");
        if (Directory.Exists(profiles))
            Directory.Delete(profiles, recursive: true);
    }

    private static string SettingsFile => Path.Combine(Folder, "settings.json");

    // ---- the shared folder ----------------------------------------------------------------

    [Fact]
    public void DataFolder_IsTheTestOverride_AndResolvesTheDefaultUnderAppData()
    {
        Assert.Equal(TestEnvironment.DataDirectory, AppDataFolder.FullPath);
        Assert.Equal(Path.Combine(@"C:\Users\x\AppData\Roaming", "MonitorWellness"), AppDataFolder.Resolve(null, @"C:\Users\x\AppData\Roaming"));
        Assert.Equal(@"D:\data", AppDataFolder.Resolve(@" D:\data ", @"C:\Users\x\AppData\Roaming"));
    }

    // ---- settings -------------------------------------------------------------------------

    [Fact]
    public void Load_WithNoFile_GivesDefaults_AndNoProblem()
    {
        AppSettings settings = SettingsStore.Load();

        Assert.Equal(new AppSettings().DayKelvin, settings.DayKelvin);
        Assert.Null(SettingsStore.LastLoadProblem);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAndStampsTheSchemaVersion()
    {
        var settings = new AppSettings { DayKelvin = 5700, NightKelvin = 3600, SchemaVersion = 0 };

        SettingsStore.Save(settings);
        AppSettings loaded = SettingsStore.Load();

        Assert.Equal(5700, loaded.DayKelvin);
        Assert.Equal(3600, loaded.NightKelvin);
        Assert.Equal(AppSettings.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Null(SettingsStore.LastLoadProblem);
    }

    [Fact]
    public void Load_OfCorruptJson_FallsBackToDefaults_KeepsACopy_AndSaysSo()
    {
        const string original = "{ \"DayKelvin\": 5700, this is not json";
        File.WriteAllText(SettingsFile, original);

        AppSettings loaded = SettingsStore.Load();

        Assert.Equal(new AppSettings().DayKelvin, loaded.DayKelvin);
        Assert.NotNull(SettingsStore.LastLoadProblem);
        Assert.Contains("settings.invalid-", SettingsStore.LastLoadProblem);
        string kept = Assert.Single(Directory.GetFiles(Folder, "settings.invalid-*.json"));
        Assert.Equal(original, File.ReadAllText(kept));          // the person's file survives as a copy...
        Assert.Equal(original, File.ReadAllText(SettingsFile));  // ...and the original is untouched until the next save
    }

    [Fact]
    public void Load_OfAnOutOfRangeValue_ResetsOnlyThatValue_KeepsACopy_AndSaysSo()
    {
        File.WriteAllText(SettingsFile, "{ \"DayKelvin\": 5700, \"DayBrightness\": 7.5 }");

        AppSettings loaded = SettingsStore.Load();

        Assert.Equal(new AppSettings().DayBrightness, loaded.DayBrightness);
        Assert.Equal(5700, loaded.DayKelvin);                    // everything else is kept
        Assert.NotNull(SettingsStore.LastLoadProblem);
        Assert.Contains("brightness levels", SettingsStore.LastLoadProblem);
        Assert.Single(Directory.GetFiles(Folder, "settings.invalid-*.json"));
    }

    [Fact]
    public void Load_OfAWronglyTypedValue_ResetsOnlyThatValue()
    {
        File.WriteAllText(SettingsFile, "{ \"DayKelvin\": \"warm\", \"NightKelvin\": 3500 }");

        AppSettings loaded = SettingsStore.Load();

        Assert.Equal(new AppSettings().DayKelvin, loaded.DayKelvin);
        Assert.Equal(3500, loaded.NightKelvin);
        Assert.Contains("DayKelvin", SettingsStore.LastLoadProblem);
    }

    [Fact]
    public void Load_OfARepairedFile_SavesTheRepair_SoTheNoticeIsShownOnlyOnce()
    {
        File.WriteAllText(SettingsFile, "{ \"DayKelvin\": 5700, \"DayBrightness\": 7.5 }");

        SettingsStore.Load();
        Assert.Contains("saved", SettingsStore.LastLoadProblem);

        AppSettings second = SettingsStore.Load();

        Assert.Null(SettingsStore.LastLoadProblem);
        Assert.Equal(5700, second.DayKelvin);
        Assert.Equal(new AppSettings().DayBrightness, second.DayBrightness);
        Assert.Contains("7.5", File.ReadAllText(Assert.Single(Directory.GetFiles(Folder, "settings.invalid-*.json")))); // the original is still there as a copy
    }

    [Fact]
    public void Save_AfterAFailedLoad_DoesNotDestroyTheKeptCopy()
    {
        File.WriteAllText(SettingsFile, "garbage");
        SettingsStore.Load();

        SettingsStore.Save(new AppSettings());

        Assert.Equal("garbage", File.ReadAllText(Assert.Single(Directory.GetFiles(Folder, "settings.invalid-*.json"))));
        Assert.True(SettingsStore.Load().DayKelvin > 0);
        Assert.Null(SettingsStore.LastLoadProblem);
    }

    [Fact]
    public void Load_OfANewerSchema_StillLoads_ButKeepsTheOriginalOnce()
    {
        const string original = "{ \"SchemaVersion\": 99, \"DayKelvin\": 5800 }";
        File.WriteAllText(SettingsFile, original);

        AppSettings loaded = SettingsStore.Load();

        Assert.Equal(5800, loaded.DayKelvin);
        Assert.Null(SettingsStore.LastLoadProblem);
        Assert.Equal(original, File.ReadAllText(Path.Combine(Folder, "settings.from-newer-version.json")));
    }

    // ---- profiles -------------------------------------------------------------------------

    [Fact]
    public void Profiles_SaveListLoadDelete()
    {
        ProfileStore.Save("Evening", new AppSettings { DayKelvin = 5100 });
        ProfileStore.Save("Bright", new AppSettings { DayKelvin = 6400 });

        string[] bothProfiles = { "Bright", "Evening" };
        Assert.Equal(bothProfiles, ProfileStore.ListNames());
        Assert.Equal(5100, ProfileStore.Load("Evening")!.DayKelvin);

        Assert.True(ProfileStore.Delete("Evening"));
        string[] oneProfile = { "Bright" };
        Assert.Equal(oneProfile, ProfileStore.ListNames());
        Assert.Null(ProfileStore.Load("Evening"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("con")]
    [InlineData("a/b")]
    public void Profiles_RefuseNamesWindowsCannotUse(string name)
        => Assert.Throws<ArgumentException>(() => ProfileStore.Save(name, new AppSettings()));

    [Fact]
    public void Profiles_ACorruptFile_LoadsAsNull_InsteadOfThrowing()
    {
        Directory.CreateDirectory(Path.Combine(Folder, "Profiles"));
        File.WriteAllText(Path.Combine(Folder, "Profiles", "Broken.json"), "not json");

        Assert.Null(ProfileStore.Load("Broken"));
    }

    // ---- history --------------------------------------------------------------------------

    private static string HistoryFile => Path.Combine(Folder, "history.jsonl");

    [Fact]
    public void History_AppendLoadClear_AndAMalformedLineIsSkipped()
    {
        HistoryStore.Append(new HistoryEvent(new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc), "MigraineActivated", true, null));
        File.AppendAllText(HistoryFile, "{ half a line" + Environment.NewLine);
        HistoryStore.Append(new HistoryEvent(new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc), "MigraineDeactivated", true, 3600));

        IReadOnlyList<HistoryEvent> events = HistoryStore.Load();

        Assert.Equal(2, events.Count);
        Assert.Equal("MigraineDeactivated", events[1].EventType);
        Assert.Equal(3600, events[1].DurationSeconds);

        HistoryStore.Clear();
        Assert.Empty(HistoryStore.Load());
    }

    [Fact]
    public void History_WhenTheFileGrowsPastTheCap_KeepsOnlyTheRecentTail_OnWholeLines()
    {
        var line = new StringBuilder();
        for (int i = 0; i < 80_000; i++)
            line.Append("{\"TimestampUtc\":\"2026-11-01T09:00:00Z\",\"EventType\":\"SchedulePaused\",\"Mild\":null,\"DurationSeconds\":").Append(i).Append(",\"Rating\":null}\n");
        File.WriteAllText(HistoryFile, line.ToString());
        Assert.True(new FileInfo(HistoryFile).Length > 8 * 1024 * 1024);

        HistoryStore.Append(new HistoryEvent(DateTime.UtcNow, "MigraineActivated", false, null));

        Assert.True(new FileInfo(HistoryFile).Length < 3 * 1024 * 1024, "the file should have been trimmed to about 2 MB");
        IReadOnlyList<HistoryEvent> events = HistoryStore.Load();
        Assert.Equal("MigraineActivated", events[^1].EventType);
        Assert.True(events.Count > 1000);
        Assert.All(events, e => Assert.NotNull(e.EventType)); // the first kept line is whole, not a torn fragment
    }

    // ---- diagnostic bundle ----------------------------------------------------------------

    [Fact]
    public void DiagnosticBundle_HasItsParts_AndRemovesTheAccountNameFromOldLogLines()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // Opened with the same sharing as DebugLog itself: any other test that logs at this moment holds the file
        // open for writing, and File.WriteAllText (which refuses to share) would then fail at random.
        using (var logStream = new FileStream(DebugLog.FilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        using (var logWriter = new StreamWriter(logStream))
            logWriter.Write($"2026-01-01 00:00:00.000 loaded settings from {profile}\\AppData\\Roaming\\MonitorWellness\\settings.json{Environment.NewLine}");
        string zip = Path.Combine(Folder, "bundle.zip");

        DiagnosticBundleService.Create(zip);

        using ZipArchive archive = ZipFile.OpenRead(zip);
        string[] expectedEntries = { "README.txt", "debug.log", "environment.txt" };
        Assert.Equal(expectedEntries, archive.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal));
        string log = new StreamReader(archive.GetEntry("debug.log")!.Open()).ReadToEnd();
        Assert.DoesNotContain(profile, log, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%USERPROFILE%", log);
        string readme = new StreamReader(archive.GetEntry("README.txt")!.Open()).ReadToEnd();
        Assert.Contains("account name is removed", readme);
        Assert.Contains("Active monitors", new StreamReader(archive.GetEntry("environment.txt")!.Open()).ReadToEnd());
    }

    // ---- recovery matching ----------------------------------------------------------------

    [Fact]
    public void Recovery_FindsTheDisplayByHardwareIdentity_ThenByName_ElseNothing()
    {
        var monitors = new[]
        {
            new MonitorInfo(@"\\.\DISPLAY1", "A", true, @"\\?\DISPLAY#AAA#1"),
            new MonitorInfo(@"\\.\DISPLAY3", "B", false, @"\\?\DISPLAY#BBB#2"),
        };

        // The same physical monitor, now enumerated under a different Windows device name.
        Assert.Equal(@"\\.\DISPLAY3", DisplayStateRecovery.ResolveDevice(monitors, @"\\?\display#bbb#2", @"\\.\DISPLAY2"));
        // No identity recorded: fall back to the device name.
        Assert.Equal(@"\\.\DISPLAY1", DisplayStateRecovery.ResolveDevice(monitors, "", @"\\.\display1"));
        // Identity unknown and no such device: nothing to restore.
        Assert.Null(DisplayStateRecovery.ResolveDevice(monitors, @"\\?\DISPLAY#ZZZ#9", @"\\.\DISPLAY9"));
    }

    [Fact]
    public void Recovery_DiscardsBrightnessRecordsOlderThanAWeek()
    {
        var now = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
        BrightnessOriginalEntry At(DateTime saved) => new("d", "h", 0, 50, 0, 100, saved);

        Assert.False(DisplayStateRecovery.IsStale(At(now.AddDays(-6)), now));
        Assert.True(DisplayStateRecovery.IsStale(At(now.AddDays(-8)), now));
    }

    // ---- symbols --------------------------------------------------------------------------

    [Fact]
    public void TheAppAssembly_CarriesItsSymbols_SoStackTracesHaveLineNumbers()
    {
        string path = typeof(AppSettings).Assembly.Location;

        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);

        Assert.Contains(reader.ReadDebugDirectory(), entry => entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb);
    }
}
