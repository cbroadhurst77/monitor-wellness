using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace MonitorWellness.Core;

/// <summary>
/// Creates an explicitly user-requested local support bundle. It includes technical state and
/// the rolling log, but deliberately excludes settings, location, history, and profiles.
/// </summary>
public static class DiagnosticBundleService
{
    public static void Create(string destinationPath)
    {
        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var archive = ZipFile.Open(destinationPath, ZipArchiveMode.Create);
        WriteText(archive, "README.txt", "This user-requested diagnostic bundle contains technical environment data and the local debug log. It does not include settings, location, history, or saved profiles. Review debug.log before sharing it.");
        WriteText(archive, "environment.txt", BuildEnvironmentReport());

        if (File.Exists(DebugLog.FilePath))
            archive.CreateEntryFromFile(DebugLog.FilePath, "debug.log", CompressionLevel.Optimal);
    }

    private static string BuildEnvironmentReport()
    {
        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"Generated UTC: {DateTime.UtcNow:O}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Application version: {Assembly.GetExecutingAssembly().GetName().Version}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Operating system: {Environment.OSVersion}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Process architecture: {RuntimeInformation.ProcessArchitecture}");
        report.AppendLine(CultureInfo.InvariantCulture, $"HDR enabled: {HdrDetector.IsAnyDisplayHdrEnabled()}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Ambient-light sensor available: {AmbientLightSensor.IsAvailable}");
        VisualStabilitySnapshot visualStability = VisualStabilityDiagnostics.GetSnapshot();
        report.AppendLine(CultureInfo.InvariantCulture, $"Flicker Guard session counters: topology signals={visualStability.DisplayTopologySignals}; coalesced refreshes={visualStability.CoalescedTopologyRefreshes}; unrelated foreground display writes avoided={visualStability.ForegroundDisplayWritesAvoided}");
        report.AppendLine();
        report.AppendLine("Active monitors:");
        foreach (MonitorInfo monitor in MonitorEnumerator.GetActiveMonitors())
        {
            string compatibility = DisplayCompatibilityAdvisor.TryGetOverlayOnlyReason(monitor, out string reason)
                ? $"; compatibility fallback recommended ({reason})"
                : "";
            report.AppendLine(CultureInfo.InvariantCulture, $"- {monitor.DeviceName}; primary={monitor.IsPrimary}; name={monitor.DeviceString}{compatibility}");
        }

        report.AppendLine();
        report.AppendLine("DDC/CI brightness capability (read-only probe):");
        foreach (DdcCiBrightnessCapability capability in DdcCiBrightnessProbe.GetCapabilities())
            report.AppendLine(CultureInfo.InvariantCulture, $"- {capability.DeviceName}; supported={capability.IsSupported}; {capability.Detail}");

        report.AppendLine();
        report.AppendLine("The Display Capability Passport is intentionally not included because it depends on personal approval/quarantine settings, which diagnostic bundles exclude for privacy.");
        return report.ToString();
    }

    private static void WriteText(ZipArchive archive, string entryName, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using StreamWriter writer = new(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }
}
