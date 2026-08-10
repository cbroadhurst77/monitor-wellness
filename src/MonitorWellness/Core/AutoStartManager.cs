using System.Diagnostics;

namespace MonitorWellness.Core;

/// <summary>
/// Registers/unregisters this app's own Task Scheduler auto-start entry from within the
/// running app itself — the "portable mode" story: download the self-contained single-file
/// exe, run it directly with no installer, and opt into auto-start from the tray menu. This
/// is exactly what the Week 4 IT-managed-machine finding needed: installing is blocked on
/// some machines, but running an already-downloaded exe directly isn't.
///
/// Registering an onlogon-triggered task needs elevation (confirmed directly against real
/// Task Scheduler behavior during Week 4 -- schtasks /create /sc onlogon fails with "Access
/// is denied" under a standard token, while /sc once succeeds fine). Unregistering is
/// elevated too even though that specific case wasn't independently confirmed to need it --
/// an extra UAC prompt on removal is a small, safe cost for certainty rather than an assumption.
/// Querying whether the task exists does not need elevation and is not elevated here.
/// </summary>
public static class AutoStartManager
{
    public const string TaskName = "MonitorWellness";

    /// <summary>Builds the schtasks argument string for registering the auto-start task. Pure and testable — no process is started here.</summary>
    public static string BuildCreateArguments(string exePath)
        => $"/create /tn \"{TaskName}\" /tr \"\\\"{exePath}\\\"\" /sc onlogon /rl limited";

    /// <summary>Builds the schtasks argument string for removing the auto-start task. Pure and testable.</summary>
    public static string BuildDeleteArguments()
        => $"/delete /tn \"{TaskName}\" /f";

    /// <summary>Builds the schtasks argument string for querying whether the task exists. Pure and testable.</summary>
    public static string BuildQueryArguments()
        => $"/query /tn \"{TaskName}\"";

    /// <summary>Builds the schtasks argument string for a verbose, parseable query. Pure and testable.</summary>
    public static string BuildQueryVerboseArguments()
        => $"/query /tn \"{TaskName}\" /v /fo LIST";

    /// <summary>True if the auto-start task is currently registered. Does not require elevation.</summary>
    public static bool IsRegistered()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("schtasks.exe", BuildQueryArguments())
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            DebugLog.Write($"AutoStartManager.IsRegistered check failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Registers auto-start for the current running exe. Triggers a UAC prompt. Returns
    /// false if the user cancels the prompt or the call otherwise fails — callers should
    /// treat that as "didn't happen," not a crash.
    /// </summary>
    public static bool Register()
    {
        string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Could not determine the running executable's path.");
        return RunElevated(BuildCreateArguments(exePath));
    }

    public static bool Unregister() => RunElevated(BuildDeleteArguments());

    /// <summary>
    /// Task Scheduler's own record of whether this task has actually fired -- specifically
    /// LastRunTime/LastResult -- which is the only way to check, from inside the running app,
    /// whether auto-start survived a real reboot. IsRegistered() alone only confirms the task
    /// *definition* still exists; it says nothing about whether it has ever successfully run.
    /// Does not require elevation.
    /// </summary>
    public sealed record AutoStartDiagnostics(bool IsRegistered, string? Status, string? LastRunTime, string? LastResult, string? NextRunTime);

    public static AutoStartDiagnostics GetDiagnostics()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("schtasks.exe", BuildQueryVerboseArguments())
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            string output = process?.StandardOutput.ReadToEnd() ?? "";
            process?.WaitForExit(5000);

            if (process?.ExitCode != 0)
                return new AutoStartDiagnostics(false, null, null, null, null);

            var fields = ParseFields(output);
            return new AutoStartDiagnostics(
                IsRegistered: true,
                Status: fields.GetValueOrDefault("Status"),
                LastRunTime: fields.GetValueOrDefault("Last Run Time"),
                LastResult: fields.GetValueOrDefault("Last Result"),
                NextRunTime: fields.GetValueOrDefault("Next Run Time"));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            DebugLog.Write($"AutoStartManager.GetDiagnostics failed: {ex.Message}");
            return new AutoStartDiagnostics(false, null, null, null, null);
        }
    }

    /// <summary>Parses schtasks' "/fo LIST" plain-text output into a label -&gt; value map. Pure and testable -- no process involved.</summary>
    public static Dictionary<string, string> ParseFields(string schtasksListOutput)
    {
        var result = new Dictionary<string, string>();
        foreach (string rawLine in schtasksListOutput.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            int colonIndex = line.IndexOf(':');
            if (colonIndex <= 0)
                continue;

            string label = line[..colonIndex].Trim();
            string value = line[(colonIndex + 1)..].Trim();
            if (label.Length > 0 && value.Length > 0)
                result[label] = value;
        }
        return result;
    }

    private static bool RunElevated(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            process?.WaitForExit(30_000);
            bool success = process?.ExitCode == 0;
            DebugLog.Write($"AutoStartManager: schtasks {arguments} -> exit {process?.ExitCode}");
            return success;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // Most commonly: the user clicked "No" on the UAC prompt (ERROR_CANCELLED, 1223).
            DebugLog.Write($"AutoStartManager: elevated schtasks call failed or was cancelled: {ex.Message}");
            return false;
        }
    }
}
