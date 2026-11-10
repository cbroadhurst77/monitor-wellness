using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security;
using Microsoft.Win32;

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

    /// <summary>
    /// schtasks.exe by its full System32 path. Launching it by bare name makes Windows look in the
    /// application's own folder first; in portable mode that is a folder the person can write to
    /// (Downloads, say), so a planted schtasks.exe next to the app would run, and for create/delete
    /// it would run elevated after the person approves the admin prompt.
    /// </summary>
    internal static string SchtasksPath => Path.Combine(Environment.SystemDirectory, "schtasks.exe");

    /// <summary>Per-user fallback used when Task Scheduler registration isn't possible (see <see cref="Register"/>). Needs no elevation.</summary>
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string RunValueName = "MonitorWellness";
    public const string TaskSchedulerMethod = "Task Scheduler";
    public const string RunKeyMethod = "Windows sign-in (Run key)";

    /// <summary>
    /// Set by <see cref="Register"/> when it succeeded in a way the person should hear about
    /// (it had to use the no-admin fallback). Null when it did exactly what was asked.
    /// </summary>
    public static string? LastRegistrationNote { get; private set; }

    /// <summary>The registry value for the Run-key fallback. Pure and testable.</summary>
    public static string BuildRunValue(string exePath) => $"\"{exePath}\"";

    /// <summary>
    /// True when a task's "Run As User" is this user, or is unknown. Unknown (a localized
    /// schtasks label, or a field that wasn't parsed) is treated as fine so the check can only
    /// ever catch a positively identified different account. Compares the name after any
    /// DOMAIN\ prefix, ignoring case. Pure and testable.
    /// </summary>
    public static bool RunsAsUser(string? runAsUser, string currentUserName)
    {
        if (string.IsNullOrWhiteSpace(runAsUser) || string.IsNullOrWhiteSpace(currentUserName))
            return true;

        string name = runAsUser.Trim();
        int slash = name.LastIndexOf('\\');
        if (slash >= 0)
            name = name[(slash + 1)..];
        return string.Equals(name, currentUserName.Trim(), StringComparison.OrdinalIgnoreCase);
    }

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

    /// <summary>
    /// How long to wait for the <c>schtasks</c> fallback. The old fixed 5 seconds was too short at
    /// logon, when the machine is busy: the wait timed out, reading the exit code threw, and a
    /// task that existed was reported missing (43 false "auto-start was turned off" warnings in
    /// the author's own log).
    /// </summary>
    private const int SchtasksQueryTimeoutMilliseconds = 15_000;

    /// <summary>What <c>schtasks.exe</c> did, kept separate from what it means so the mapping can be tested.</summary>
    public readonly record struct SchtasksResult(bool Started, bool Exited, int ExitCode);

    /// <summary>
    /// Maps a <c>schtasks /query</c> outcome to an answer. A process that never started or did not
    /// finish is <see cref="AutoStartCheck.Unknown"/> — it says nothing about whether the task
    /// exists. Pure and testable.
    /// </summary>
    public static AutoStartCheck ClassifySchtasksQuery(SchtasksResult result)
        => !result.Started || !result.Exited
            ? AutoStartCheck.Unknown
            : result.ExitCode == 0 ? AutoStartCheck.Registered : AutoStartCheck.NotRegistered;

    /// <summary>
    /// Whether auto-start is registered by either method (Task Scheduler, or the per-user Run key
    /// fallback), with "could not tell" kept distinct from "no". Does not require elevation.
    /// </summary>
    public static AutoStartCheck CheckRegistration()
    {
        AutoStartCheck task = CheckTask();
        return task == AutoStartCheck.Registered || IsRunKeyRegistered()
            ? AutoStartCheck.Registered
            : task;
    }

    /// <summary>True only when auto-start is definitely registered; "unknown" counts as false here, so prefer <see cref="CheckRegistration"/> when a wrong "no" would cause a warning.</summary>
    public static bool IsRegistered() => CheckRegistration() == AutoStartCheck.Registered;

    public static bool IsRunKeyRegistered()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(RunValueName) is string;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            DebugLog.Write($"AutoStartManager.IsRunKeyRegistered check failed: {ex.Message}");
            return false;
        }
    }

    private static bool TryRegisterRunKey(string exePath)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            key.SetValue(RunValueName, BuildRunValue(exePath), RegistryValueKind.String);
            return true;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            DebugLog.Write($"AutoStartManager: couldn't write the Run key: {ex.Message}");
            return false;
        }
    }

    private static bool TryUnregisterRunKey()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(RunValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            DebugLog.Write($"AutoStartManager: couldn't remove the Run key value: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The Task Scheduler entry's state. Asks the Task Scheduler service directly (fast, typed,
    /// tells "missing" from "couldn't ask"); only if that is unavailable does it fall back to
    /// <c>schtasks.exe</c>, with a timeout that is treated as unknown rather than as "missing".
    /// </summary>
    public static AutoStartCheck CheckTask()
    {
        switch (TaskSchedulerQuery.TryGet(TaskName, out TaskInfo? info))
        {
            case TaskLookup.Found:
                return info!.Enabled ? AutoStartCheck.Registered : AutoStartCheck.Disabled;
            case TaskLookup.NotFound:
                return AutoStartCheck.NotRegistered;
        }

        return ClassifySchtasksQuery(RunSchtasksQuery(BuildQueryArguments(), out _));
    }

    private static SchtasksResult RunSchtasksQuery(string arguments, out string output)
    {
        output = "";
        try
        {
            using var process = Process.Start(new ProcessStartInfo(SchtasksPath, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null)
                return new SchtasksResult(false, false, -1);

            // Read asynchronously: a synchronous ReadToEnd has no timeout and would hang the UI
            // thread for as long as schtasks does.
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(SchtasksQueryTimeoutMilliseconds))
            {
                DebugLog.Write($"AutoStartManager: schtasks {arguments} did not finish within {SchtasksQueryTimeoutMilliseconds / 1000}s; the answer is unknown.");
                try
                {
                    process.Kill();
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // It exited on its own between the timeout and the kill; nothing to do.
                }
                return new SchtasksResult(true, false, -1);
            }

            output = standardOutput.Wait(2_000) ? standardOutput.Result : "";
            return new SchtasksResult(true, true, process.ExitCode);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            DebugLog.Write($"AutoStartManager: couldn't run schtasks: {ex.Message}");
            return new SchtasksResult(false, false, -1);
        }
    }

    /// <summary>
    /// Registers auto-start for the current running exe. Task Scheduler stays the primary
    /// method (it needs a UAC prompt; see IMPLEMENTATION.md). Two cases used to end badly and
    /// now fall back to a per-user Run-key entry, which needs no elevation:
    ///  - the prompt is cancelled or blocked by policy (previously: no auto-start at all);
    ///  - a standard user approves with a *different* administrator's credentials, which
    ///    registers the task for that administrator's account, so it never starts for the
    ///    person who asked. That registration is detected afterwards, removed, and replaced.
    /// <see cref="LastRegistrationNote"/> says when the fallback was used. Returns false only
    /// if neither method worked — callers should treat that as "didn't happen," not a crash.
    /// </summary>
    public static bool Register()
    {
        LastRegistrationNote = null;
        string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Could not determine the running executable's path.");

        if (RunElevated(BuildCreateArguments(exePath)))
        {
            AutoStartDiagnostics task = GetTaskDiagnostics();
            if (RunsAsUser(task.RunAsUser, Environment.UserName))
            {
                TryUnregisterRunKey(); // the task is the single source of truth; avoid a second launch entry
                return true;
            }

            DebugLog.Write($"AutoStartManager: the task was registered for '{task.RunAsUser}', not '{Environment.UserName}'; removing it and using the per-user entry.");
            RunElevated(BuildDeleteArguments());
            if (!TryRegisterRunKey(exePath))
                return false;
            LastRegistrationNote = "Windows approved this with a different administrator's account, which would not start Monitor Wellness for you. It has been set up for your own account instead.";
            return true;
        }

        if (TryRegisterRunKey(exePath))
        {
            LastRegistrationNote = "Administrator approval wasn't given, so Monitor Wellness was set to start with Windows using a standard entry for your account instead.";
            return true;
        }
        return false;
    }

    /// <summary>Removes auto-start by every method. A UAC prompt appears only if the Task Scheduler entry exists.</summary>
    public static bool Unregister()
    {
        bool runKeyGone = TryUnregisterRunKey();
        if (CheckTask() == AutoStartCheck.NotRegistered)
            return runKeyGone;
        return RunElevated(BuildDeleteArguments()) && runKeyGone;
    }

    /// <summary>
    /// Task Scheduler's own record of whether this task has actually fired -- specifically
    /// LastRunTime/LastResult -- which is the only way to check, from inside the running app,
    /// whether auto-start survived a real reboot. IsRegistered() alone only confirms the task
    /// *definition* still exists; it says nothing about whether it has ever successfully run.
    /// Does not require elevation.
    /// </summary>
    public sealed record AutoStartDiagnostics(
        bool IsRegistered,
        string? Status,
        string? LastRunTime,
        string? LastResult,
        string? NextRunTime,
        string? RunAsUser = null,
        string Method = TaskSchedulerMethod);

    public static AutoStartDiagnostics GetDiagnostics()
    {
        AutoStartDiagnostics task = GetTaskDiagnostics();
        if (task.IsRegistered || !IsRunKeyRegistered())
            return task;

        return new AutoStartDiagnostics(true, "Registered", null, null, null, Method: RunKeyMethod);
    }

    private static AutoStartDiagnostics GetTaskDiagnostics()
    {
        switch (TaskSchedulerQuery.TryGet(TaskName, out TaskInfo? info))
        {
            case TaskLookup.Found:
                return new AutoStartDiagnostics(
                    IsRegistered: true,
                    Status: info!.State,
                    LastRunTime: FormatTime(info.LastRunLocal),
                    LastResult: TaskSchedulerQuery.DescribeLastResult(info.LastResult),
                    NextRunTime: FormatTime(info.NextRunLocal),
                    RunAsUser: info.RunAsUser);
            case TaskLookup.NotFound:
                return new AutoStartDiagnostics(false, null, null, null, null);
        }

        SchtasksResult result = RunSchtasksQuery(BuildQueryVerboseArguments(), out string output);
        if (ClassifySchtasksQuery(result) != AutoStartCheck.Registered)
            return new AutoStartDiagnostics(false, null, null, null, null);

        var fields = ParseFields(output);
        return new AutoStartDiagnostics(
            IsRegistered: true,
            Status: fields.GetValueOrDefault("Status"),
            LastRunTime: fields.GetValueOrDefault("Last Run Time"),
            LastResult: DescribeLastResultText(fields.GetValueOrDefault("Last Result")),
            NextRunTime: fields.GetValueOrDefault("Next Run Time"),
            RunAsUser: fields.GetValueOrDefault("Run As User"));
    }

    private static string? DescribeLastResultText(string? raw)
        => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int code) ? TaskSchedulerQuery.DescribeLastResult(code) : raw;

    private static string? FormatTime(DateTime? time) => time?.ToString("g", CultureInfo.CurrentCulture);

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
            using var process = Process.Start(new ProcessStartInfo(SchtasksPath, arguments)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (process is null)
                return false;

            // A slow UAC response (the user switches away, reads the prompt, etc.) is a
            // plausible, ordinary way to exceed this wait — ExitCode must not be read unless
            // WaitForExit actually reports the process has exited, or it throws.
            if (!process.WaitForExit(30_000))
            {
                DebugLog.Write($"AutoStartManager: schtasks {arguments} did not exit within 30s (UAC prompt likely still pending); treating as failed.");
                return false;
            }

            bool success = process.ExitCode == 0;
            DebugLog.Write($"AutoStartManager: schtasks {arguments} -> exit {process.ExitCode}");
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

/// <summary>The answer to "is auto-start registered?", with "I couldn't tell" kept apart from "no".</summary>
public enum AutoStartCheck
{
    Registered,
    NotRegistered,
    /// <summary>The Task Scheduler entry exists but is disabled, so it will never start the app.</summary>
    Disabled,
    /// <summary>The check could not be completed; nothing should be concluded from it.</summary>
    Unknown,
}
