using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;

namespace MonitorWellness.Core;

public enum TaskLookup
{
    /// <summary>The task exists; details are available.</summary>
    Found,
    /// <summary>Task Scheduler answered, and there is no such task.</summary>
    NotFound,
    /// <summary>Task Scheduler could not be asked (COM unavailable, service stopped, access problem). The answer is unknown, not "no".</summary>
    Unavailable,
}

/// <param name="Enabled">False when the task exists but is disabled, so it will never fire.</param>
/// <param name="State">Task Scheduler's own state name (Ready, Running, Disabled...).</param>
/// <param name="RunAsUser">The account the task runs as, or null when Task Scheduler didn't say.</param>
public sealed record TaskInfo(bool Enabled, string State, DateTime? LastRunLocal, int LastResult, DateTime? NextRunLocal, string? RunAsUser);

/// <summary>
/// Reads a scheduled task through the Task Scheduler COM service (late-bound, no extra
/// references) instead of starting <c>schtasks.exe</c>. It takes milliseconds, so it can't
/// stall startup; it can tell "the task does not exist" apart from "I could not ask" (the
/// <c>schtasks</c> exit code could not, and a slow logon made it report a task that exists as
/// missing); and its fields are typed rather than parsed from English text, so it works on
/// any Windows display language. Read-only: needs no elevation.
/// </summary>
public static class TaskSchedulerQuery
{
    private const int HResultFileNotFound = unchecked((int)0x80070002);
    private const int HResultPathNotFound = unchecked((int)0x80070003);

    public static TaskLookup TryGet(string taskName, out TaskInfo? info)
    {
        info = null;
        try
        {
            Type? serviceType = Type.GetTypeFromProgID("Schedule.Service");
            if (serviceType is null)
                return TaskLookup.Unavailable;

            dynamic service = Activator.CreateInstance(serviceType)!;
            try
            {
                service.Connect();
                dynamic folder = service.GetFolder("\\");
                dynamic task;
                try
                {
                    task = folder.GetTask(taskName);
                }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException
                    || ex is COMException { HResult: HResultFileNotFound or HResultPathNotFound })
                {
                    // The dynamic COM binder surfaces "no such task" (0x80070002) as a
                    // FileNotFoundException rather than a COMException. Scoped to GetTask only, so a
                    // missing scheduler folder or service can't be mistaken for a missing task.
                    return TaskLookup.NotFound;
                }

                bool enabled = task.Enabled;
                int state = task.State;
                DateTime lastRun = task.LastRunTime;
                DateTime nextRun = task.NextRunTime;
                int lastResult = task.LastTaskResult;
                string? user = task.Definition.Principal.UserId;

                info = new TaskInfo(
                    enabled,
                    enabled ? StateName(state) : "Disabled",
                    NormalizeComDate(lastRun),
                    lastResult,
                    NormalizeComDate(nextRun),
                    string.IsNullOrWhiteSpace(user) ? null : user);
                return TaskLookup.Found;
            }
            finally
            {
                if (Marshal.IsComObject(service))
                    Marshal.FinalReleaseComObject(service);
            }
        }
        catch (Exception ex) when (ex is COMException or RuntimeBinderException or InvalidCastException or Win32Exception or NotSupportedException or UnauthorizedAccessException)
        {
            DebugLog.Write($"TaskSchedulerQuery: couldn't ask Task Scheduler about '{taskName}' ({ex.GetType().Name}: {ex.Message}); the answer is unknown.");
            return TaskLookup.Unavailable;
        }
    }

    /// <summary>
    /// A task's last result with the common Task Scheduler codes spelled out. The raw number alone
    /// (for example 267009) reads like an error but just means "currently running". Pure and testable.
    /// </summary>
    internal static string DescribeLastResult(int result) => result switch
    {
        0 => "0 (succeeded)",
        0x41300 => $"{result} (ready to run)",
        0x41301 => $"{result} (the task is running now)",
        0x41302 => $"{result} (the task is disabled)",
        0x41303 => $"{result} (it has not run yet)",
        0x41304 => $"{result} (no more scheduled runs)",
        0x41305 => $"{result} (no valid triggers)",
        0x41306 => $"{result} (it was ended by the user)",
        _ => $"{result} (0x{result:X})",
    };

    /// <summary>Task Scheduler's TASK_STATE values, as plain words. Pure and testable.</summary>
    internal static string StateName(int state) => state switch
    {
        1 => "Disabled",
        2 => "Queued",
        3 => "Ready",
        4 => "Running",
        _ => "Unknown",
    };

    /// <summary>
    /// Task Scheduler reports "never" as the COM zero date (30 December 1899). Anything that
    /// early means "no time", so it becomes null. Pure and testable.
    /// </summary>
    internal static DateTime? NormalizeComDate(DateTime value) => value.Year < 2000 ? null : value;
}
