using System.Text.RegularExpressions;

namespace MonitorWellness.Core;

/// <summary>
/// Removes the Windows account name from log text. Log lines mention file locations such as
/// <c>C:\Users\&lt;name&gt;\AppData\...</c>, so a log someone shares for support would otherwise
/// identify them. Applied when each line is written and again when a diagnostic bundle is
/// built (to cover lines written before this existed). The names of apps the person set rules
/// for are deliberately kept: they are the point of those log lines.
/// </summary>
public static class LogRedaction
{
    public const string Placeholder = "%USERPROFILE%";

    private static readonly string ProfileDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string AccountName = Environment.UserName;

    public static string Redact(string text) => Redact(text, ProfileDirectory, AccountName);

    /// <summary>
    /// Replaces the user's profile folder, and any other drive's <c>Users\&lt;account&gt;</c>
    /// folder, with a placeholder. Pure and testable.
    /// </summary>
    public static string Redact(string text, string profileDirectory, string accountName)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        string result = text;
        if (!string.IsNullOrWhiteSpace(profileDirectory))
            // (?!\w) stops at the end of the folder name, so a longer name sharing the prefix
            // ("chris" inside "christine") is left alone.
            result = Regex.Replace(result, Regex.Escape(profileDirectory.TrimEnd('\\')) + @"(?!\w)", Placeholder, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!string.IsNullOrWhiteSpace(accountName))
        {
            result = Regex.Replace(
                result,
                @"[A-Za-z]:\\Users\\" + Regex.Escape(accountName) + @"(?!\w)",
                Placeholder,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return result;
    }
}
