using System.IO;
using System.Text.Json;
using MonitorWellness.Core;

namespace MonitorWellness.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void TryImportFrom_RejectsSemanticallyInvalidSettings()
    {
        string path = Path.GetTempFileName();
        try
        {
            var invalid = new AppSettings { Latitude = 91 };
            File.WriteAllText(path, JsonSerializer.Serialize(invalid));

            bool imported = SettingsStore.TryImportFrom(path, out _, out string error);

            Assert.False(imported);
            Assert.Contains("invalid", error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ExportTo_WritesValidSettingsWithoutLeavingTemporaryFiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"MonitorWellnessTests-{Guid.NewGuid():N}");
        string destination = Path.Combine(directory, "settings.json");
        try
        {
            SettingsStore.ExportTo(new AppSettings(), destination);

            Assert.True(File.Exists(destination));
            Assert.Empty(Directory.GetFiles(directory, ".settings.json.*.tmp"));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
