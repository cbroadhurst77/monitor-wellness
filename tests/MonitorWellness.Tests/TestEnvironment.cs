using System.IO;
using System.Runtime.CompilerServices;
using MonitorWellness.Core;

namespace MonitorWellness.Tests;

/// <summary>
/// Runs before any test code (and so before any store reads its path): sends every file the app
/// writes (settings, profiles, history, recovery journals, the log) to a private temp folder, so
/// running the tests never touches the developer's real %AppData%\MonitorWellness and the stores
/// can be tested for real.
/// </summary>
internal static class TestEnvironment
{
    /// <summary>The temp folder this test run uses in place of %AppData%\MonitorWellness.</summary>
    internal static string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "MonitorWellness.Tests", Guid.NewGuid().ToString("N"));

#pragma warning disable CA2255 // A module initializer is the only hook that runs before the stores' static initialisers; this is a test assembly.
    [ModuleInitializer]
    internal static void RedirectData()
    {
        Environment.SetEnvironmentVariable(AppDataFolder.Variable, DataDirectory);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                if (Directory.Exists(DataDirectory))
                    Directory.Delete(DataDirectory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup of a temp folder; nothing depends on it.
            }
        };
    }
#pragma warning restore CA2255
}
