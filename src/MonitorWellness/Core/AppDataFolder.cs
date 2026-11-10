namespace MonitorWellness.Core;

/// <summary>
/// The one place that says where the app keeps its files (settings, profiles, history, recovery
/// journals, log). It used to be spelled out separately in six places, which made every store
/// impossible to test without touching the real %AppData% folder. Setting the
/// <c>MONITORWELLNESS_DATA_DIR</c> environment variable redirects all of them; the test run does,
/// so tests read and write a temp folder and never the developer's real settings.
/// </summary>
public static class AppDataFolder
{
    public const string Variable = "MONITORWELLNESS_DATA_DIR";

    /// <summary>The folder every store uses: the override if one is set, otherwise %AppData%\MonitorWellness.</summary>
    public static string FullPath { get; } = Resolve(
        Environment.GetEnvironmentVariable(Variable),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    /// <summary>Pure and testable: the override folder itself when given, otherwise the default under AppData.</summary>
    internal static string Resolve(string? overrideDirectory, string appDataDirectory)
        => string.IsNullOrWhiteSpace(overrideDirectory)
            ? System.IO.Path.Combine(appDataDirectory, "MonitorWellness")
            : overrideDirectory.Trim();
}
