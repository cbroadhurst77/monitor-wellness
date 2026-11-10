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

        // Write to a temp file and move it into place only once every entry succeeded — a
        // crash/exception partway through (e.g. BuildEnvironmentReport hitting a flaky
        // DDC/CI or monitor-enumeration call) must not leave a truncated-but-openable zip at
        // the exact path the user chose and may go on to share as "the diagnostic bundle."
        string tempPath = Path.Combine(directory ?? Path.GetTempPath(), $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var archive = ZipFile.Open(tempPath, ZipArchiveMode.Create))
            {
                WriteText(archive, "README.txt", "This user-requested diagnostic bundle contains technical environment data and the local debug log. It does not include settings, location, history, or saved profiles. Your Windows account name is removed from file paths in the log, but the names of applications you created comfort rules for can still appear. Review debug.log before sharing it.");
                WriteText(archive, "environment.txt", BuildEnvironmentReport());

                string? log = ReadLogRedacted();
                if (log is not null)
                    WriteText(archive, "debug.log", log);
            }

            File.Move(tempPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    /// <summary>Reads the log (the app may be appending to it, hence the shared read) with the Windows account name removed, including from lines written before redaction existed.</summary>
    private static string? ReadLogRedacted()
    {
        try
        {
            if (!File.Exists(DebugLog.FilePath))
                return null;

            using var stream = new FileStream(DebugLog.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return LogRedaction.Redact(reader.ReadToEnd());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DebugLog.Write($"DiagnosticBundleService: couldn't read the log: {ex.Message}");
            return null;
        }
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
