using System.IO;
using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public sealed class RecoveryJournalTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mw-journal-tests-" + Guid.NewGuid().ToString("N"));

    private RecoveryJournal<BrightnessOriginalEntry> NewJournal() => new(Path.Combine(_directory, "j.json"));

    private static BrightnessOriginalEntry Entry(string hardwareId, int index, uint value, string device = @"\\.\DISPLAY1")
        => new(device, hardwareId, index, value, 0, 100, DateTime.UtcNow);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEntries()
    {
        var journal = NewJournal();
        journal.Save(new[] { Entry("MONITOR\\A", 0, 80) });

        var loaded = journal.Load();

        Assert.Single(loaded);
        Assert.Equal(80u, loaded[0].Value);
        Assert.Equal("MONITOR\\A", loaded[0].HardwareDeviceId);
    }

    [Fact]
    public void SavingAnEmptyList_DeletesTheFile()
    {
        var journal = NewJournal();
        journal.Save(new[] { Entry("A", 0, 50) });
        Assert.True(File.Exists(journal.FilePath));

        journal.Save(Array.Empty<BrightnessOriginalEntry>());

        Assert.False(File.Exists(journal.FilePath));
        Assert.Empty(journal.Load());
    }

    [Fact]
    public void Load_OfACorruptOrMissingFile_ReturnsEmptyInsteadOfThrowing()
    {
        var journal = NewJournal();
        Assert.Empty(journal.Load());

        Directory.CreateDirectory(_directory);
        File.WriteAllText(journal.FilePath, "{ not json");
        Assert.Empty(journal.Load());
    }

    [Fact]
    public void Add_KeepsTheFirstRecordedValue_SoADimmedReadingNeverReplacesTheRealOriginal()
    {
        var journal = NewJournal();

        BrightnessOriginalJournal.Add(new[] { Entry("A", 0, 90) }, journal);
        BrightnessOriginalJournal.Add(new[] { Entry("A", 0, 40) }, journal); // already dimmed by us

        var loaded = journal.Load();
        Assert.Single(loaded);
        Assert.Equal(90u, loaded[0].Value);
    }

    [Fact]
    public void Remove_DropsOnlyTheMatchingMonitor_AndDeletesTheFileWhenEmpty()
    {
        var journal = NewJournal();
        BrightnessOriginalJournal.Add(new[] { Entry("A", 0, 90), Entry("B", 0, 70) }, journal);

        BrightnessOriginalJournal.Remove(new[] { Entry("A", 0, 0) }, journal);
        Assert.Equal("B", Assert.Single(journal.Load()).HardwareDeviceId);

        BrightnessOriginalJournal.Remove(new[] { Entry("B", 0, 0) }, journal);
        Assert.False(File.Exists(journal.FilePath));
    }

    [Fact]
    public void SameMonitor_UsesHardwareIdWhenBothHaveOne_OtherwiseTheDeviceName()
    {
        Assert.True(BrightnessOriginalJournal.SameMonitor(Entry("a", 0, 1, @"\\.\DISPLAY1"), Entry("A", 0, 2, @"\\.\DISPLAY3")));
        Assert.False(BrightnessOriginalJournal.SameMonitor(Entry("a", 0, 1), Entry("a", 1, 1)));
        Assert.True(BrightnessOriginalJournal.SameMonitor(Entry("", 0, 1, @"\\.\DISPLAY2"), Entry("", 0, 9, @"\\.\display2")));
        Assert.False(BrightnessOriginalJournal.SameMonitor(Entry("", 0, 1, @"\\.\DISPLAY2"), Entry("", 0, 1, @"\\.\DISPLAY3")));
    }
}
