using MonitorWellness.Core;

namespace MonitorWellness.Tests;

/// <summary>
/// Covers the argument-string construction only — the actual schtasks invocation (elevation,
/// process launch) isn't unit-testable without a real Windows session, consistent with the
/// known Win32/WPF-layer test gap noted in EVALUATION.md. What *is* testable, and matters
/// most given the Week 4 finding that a subtly wrong schtasks invocation fails silently or
/// with a confusing error, is that the arguments are built correctly and safely quoted.
/// </summary>
public class AutoStartManagerTests
{
    [Fact]
    public void BuildCreateArguments_IncludesTaskNameAndExePath()
    {
        string args = AutoStartManager.BuildCreateArguments(@"C:\Program Files\Monitor Wellness\MonitorWellness.exe");

        Assert.Contains("/create", args);
        Assert.Contains($"/tn \"{AutoStartManager.TaskName}\"", args);
        Assert.Contains(@"C:\Program Files\Monitor Wellness\MonitorWellness.exe", args);
        Assert.Contains("/sc onlogon", args);
        Assert.Contains("/rl limited", args); // runs with standard rights once triggered, even though registering it needs elevation
        Assert.Contains("/f", args);
    }

    [Fact]
    public void BuildCreateArguments_QuotesExePathContainingSpaces()
    {
        // This exact quoting pattern (\"...\" nested inside the outer /tr "...") was the
        // thing that took real trial and error to get right during the Week 4 installer
        // work — a regression here would silently produce a broken scheduled task.
        string args = AutoStartManager.BuildCreateArguments(@"C:\Program Files\Monitor Wellness\MonitorWellness.exe");
        Assert.Contains("/tr \"\\\"C:\\Program Files\\Monitor Wellness\\MonitorWellness.exe\\\"\"", args);
    }

    [Fact]
    public void BuildDeleteArguments_ReferencesTheSameTaskName()
    {
        string args = AutoStartManager.BuildDeleteArguments();
        Assert.Contains("/delete", args);
        Assert.Contains($"/tn \"{AutoStartManager.TaskName}\"", args);
        Assert.Contains("/f", args);
    }

    [Fact]
    public void BuildQueryArguments_ReferencesTheSameTaskName()
    {
        string args = AutoStartManager.BuildQueryArguments();
        Assert.Contains("/query", args);
        Assert.Contains($"/tn \"{AutoStartManager.TaskName}\"", args);
    }
}
