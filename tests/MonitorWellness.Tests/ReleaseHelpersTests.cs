using System.IO;
using System.Diagnostics;

namespace MonitorWellness.Tests;

/// <summary>
/// Runs the real tools/ReleaseHelpers.ps1 against a temporary copy of the three files a release touches, so
/// the version bump, changelog fold and tag check that the release job relies on are exercised before a tag
/// is ever pushed. Skipped quietly where PowerShell 7 is not installed.
/// </summary>
public class ReleaseHelpersTests
{
    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "tools", "ReleaseHelpers.ps1")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static (int ExitCode, string Output) Pwsh(string command)
    {
        var psi = new ProcessStartInfo("pwsh.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(command);
        using Process p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output);
    }

    private static bool HasPwsh()
    {
        try { return Pwsh("exit 0").ExitCode == 0; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    private static string MakeCopy()
    {
        string root = RepoRoot();
        string copy = Path.Combine(Path.GetTempPath(), "mw-release-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(copy, "src", "MonitorWellness"));
        Directory.CreateDirectory(Path.Combine(copy, "installer"));
        File.Copy(Path.Combine(root, "src", "MonitorWellness", "MonitorWellness.csproj"), Path.Combine(copy, "src", "MonitorWellness", "MonitorWellness.csproj"));
        File.Copy(Path.Combine(root, "installer", "MonitorWellness.iss"), Path.Combine(copy, "installer", "MonitorWellness.iss"));
        File.WriteAllText(Path.Combine(copy, "CHANGELOG.md"),
            "# Changelog\n\n## Unreleased\n\n- A new thing.\n\n## 0.0.1 — Older\n\n- Old thing.\n");
        return copy;
    }

    [Fact]
    public void Bumping_a_version_updates_project_installer_and_changelog_and_the_notes_can_be_read_back()
    {
        if (!HasPwsh()) return;
        string copy = MakeCopy();
        string helpers = Path.Combine(RepoRoot(), "tools", "ReleaseHelpers.ps1");
        try
        {
            var (code, output) = Pwsh($". '{helpers}'; Set-ReleaseVersion -Root '{copy}' -Version 99.1.2 -Title 'Test release'; Assert-TagMatchesVersion -Root '{copy}' -Tag v99.1.2; Get-ReleaseNotes -ChangelogPath '{copy}/CHANGELOG.md' -Version 99.1.2");

            Assert.True(code == 0, output);
            Assert.Contains("A new thing.", output);
            Assert.DoesNotContain("Old thing.", output);
            Assert.Contains("<Version>99.1.2</Version>", File.ReadAllText(Path.Combine(copy, "src", "MonitorWellness", "MonitorWellness.csproj")));
            Assert.Contains("MyAppVersion \"99.1.2\"", File.ReadAllText(Path.Combine(copy, "installer", "MonitorWellness.iss")));
            string changelog = File.ReadAllText(Path.Combine(copy, "CHANGELOG.md"));
            Assert.Contains("## 99.1.2 — Test release", changelog);
            Assert.Contains("## Unreleased", changelog);
        }
        finally { Directory.Delete(copy, true); }
    }

    [Fact]
    public void A_wrong_tag_an_empty_changelog_and_a_lower_version_are_all_refused()
    {
        if (!HasPwsh()) return;
        string copy = MakeCopy();
        string helpers = Path.Combine(RepoRoot(), "tools", "ReleaseHelpers.ps1");
        try
        {
            Assert.NotEqual(0, Pwsh($". '{helpers}'; Assert-TagMatchesVersion -Root '{copy}' -Tag v99.9.9").ExitCode);
            Assert.NotEqual(0, Pwsh($". '{helpers}'; Set-ReleaseVersion -Root '{copy}' -Version 0.0.1 -Title x").ExitCode);

            File.WriteAllText(Path.Combine(copy, "CHANGELOG.md"), "# Changelog\n\n## Unreleased\n\n## 0.0.1 — Older\n\n- Old thing.\n");
            Assert.NotEqual(0, Pwsh($". '{helpers}'; Set-ReleaseVersion -Root '{copy}' -Version 99.0.0 -Title x").ExitCode);
            Assert.Contains("<Version>", File.ReadAllText(Path.Combine(copy, "src", "MonitorWellness", "MonitorWellness.csproj")));
            Assert.DoesNotContain("99.0.0", File.ReadAllText(Path.Combine(copy, "src", "MonitorWellness", "MonitorWellness.csproj")));
        }
        finally { Directory.Delete(copy, true); }
    }
}
