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
        Assert.DoesNotContain("/f", args); // never overwrite an existing task without an explicit recovery path
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

    [Fact]
    public void BuildQueryVerboseArguments_RequestsListFormat()
    {
        string args = AutoStartManager.BuildQueryVerboseArguments();
        Assert.Contains("/query", args);
        Assert.Contains($"/tn \"{AutoStartManager.TaskName}\"", args);
        Assert.Contains("/v", args);
        Assert.Contains("/fo LIST", args);
    }

    // Captured verbatim from a real `schtasks /query /tn MonitorWellness /v /fo LIST` on a
    // task that has never actually fired yet (Last Run Time is schtasks' epoch placeholder,
    // Last Result 267011 = "task has not yet run") -- exactly the state GetDiagnostics needs
    // to distinguish from "fired at least once," which is the whole point of this feature.
    private const string SampleListOutput =
        "Folder: \\\r\n" +
        "HostName:                             EQ-LT-002\r\n" +
        "TaskName:                             \\MonitorWellness\r\n" +
        "Next Run Time:                        N/A\r\n" +
        "Status:                               Ready\r\n" +
        "Logon Mode:                           Interactive only\r\n" +
        "Last Run Time:                        30/11/1999 00:00:00\r\n" +
        "Last Result:                          267011\r\n" +
        "Author:                               EQ-LT-002\\chris\r\n" +
        "Task To Run:                          \"C:\\Program Files\\dotnet\\dotnet.exe\" \r\n" +
        "Run As User:                          chris\r\n" +
        "Schedule Type:                        At logon time\r\n" +
        "Repeat: Every:                        N/A\r\n";

    [Fact]
    public void ParseFields_ExtractsTheFieldsDiagnosticsNeeds()
    {
        var fields = AutoStartManager.ParseFields(SampleListOutput);

        Assert.Equal("Ready", fields["Status"]);
        Assert.Equal("N/A", fields["Next Run Time"]);
        Assert.Equal("30/11/1999 00:00:00", fields["Last Run Time"]);
        Assert.Equal("267011", fields["Last Result"]);
    }

    [Fact]
    public void ParseFields_DoesNotThrowOnMalformedRepeatLines()
    {
        // "Repeat: Every:" has two colons -- schtasks' own quirk, not something this parser
        // can fix -- but it must not throw or corrupt the fields that come before it.
        var fields = AutoStartManager.ParseFields(SampleListOutput);
        Assert.True(fields.ContainsKey("Status"));
    }

    [Fact]
    public void ParseFields_EmptyInput_ReturnsEmptyMap()
    {
        var fields = AutoStartManager.ParseFields("");
        Assert.Empty(fields);
    }
}
