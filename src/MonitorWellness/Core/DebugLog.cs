using System.IO;

namespace MonitorWellness.Core;

/// <summary>
/// Diagnostic logging that writes to %AppData%\MonitorWellness\debug.log. Kept in for v1
/// (not stripped, per the Week 4 decision in IMPLEMENTATION.md) — it's what actually found
/// several real bugs during development in minutes instead of guessing blind from a user's
/// description, and that same value applies to a real user's bug report post-release. Caps
/// the file size so unbounded long-term use doesn't grow it forever.
/// </summary>
public static class DebugLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MonitorWellness",
        "debug.log");

    private const long MaxSizeBytes = 2 * 1024 * 1024; // 2MB
    private const long TrimToBytes = 512 * 1024;       // keep the most recent 512KB when rotating

    public static void Write(string message)
    {
        try
        {
            string? directory = Path.GetDirectoryName(LogPath);
            if (directory is not null)
                Directory.CreateDirectory(directory);

            RotateIfTooLarge();
            File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must never be the thing that crashes the app. RotateIfTooLarge touches
            // FileInfo.Length and opens a FileStream before this catch takes effect, and both
            // can throw UnauthorizedAccessException under permission conditions IOException
            // doesn't cover -- matches the pattern already used in ProfileStore.Load/HistoryStore.Load.
        }
    }

    private static void RotateIfTooLarge()
    {
        var info = new FileInfo(LogPath);
        if (!info.Exists || info.Length <= MaxSizeBytes)
            return;

        // Keep only the tail: read the last TrimToBytes bytes and drop everything before the
        // first newline in that chunk, so the file starts on a clean line boundary.
        byte[] tail;
        using (var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            stream.Seek(-TrimToBytes, SeekOrigin.End);
            tail = new byte[TrimToBytes];
            stream.ReadExactly(tail);
        }

        int firstNewline = Array.IndexOf(tail, (byte)'\n');
        int start = firstNewline >= 0 ? firstNewline + 1 : 0;

        File.WriteAllBytes(LogPath, tail[start..]);
    }
}
