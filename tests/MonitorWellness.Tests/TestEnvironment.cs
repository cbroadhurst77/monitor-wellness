using System.IO;
using System.Runtime.CompilerServices;
using MonitorWellness.Core;

namespace MonitorWellness.Tests;

/// <summary>
/// Runs before any test code (and so before DebugLog first reads its path): sends the log to a
/// temp folder, so running the tests no longer appends their activity to the developer's real
/// %AppData%\MonitorWellness\debug.log.
/// </summary>
internal static class TestEnvironment
{
#pragma warning disable CA2255 // A module initializer is the only hook that runs before DebugLog's static initialiser; this is a test assembly.
    [ModuleInitializer]
    internal static void RedirectLog()
        => Environment.SetEnvironmentVariable(
            DebugLog.LogDirectoryVariable,
            Path.Combine(Path.GetTempPath(), "MonitorWellness.Tests", "logs"));
#pragma warning restore CA2255
}
