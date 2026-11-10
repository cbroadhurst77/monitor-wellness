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
    /// <summary>
    /// Environment variable that redirects the log to another folder. The test run sets it so
    /// unit tests stop appending their activity to the person's real debug.log.
    /// </summary>
    public const string LogDirectoryVariable = "MONITORWELLNESS_LOG_DIR";

    // The dedicated log override wins; otherwise the shared data-folder override (AppDataFolder),
    // otherwise %AppData%\MonitorWellness.
    private static readonly string LogPath = ResolveLogPath(
        Environment.GetEnvironmentVariable(LogDirectoryVariable) ?? Environment.GetEnvironmentVariable(AppDataFolder.Variable),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    /// <summary>The log file location: the override folder if one is set, otherwise %AppData%\MonitorWellness. Pure and testable.</summary>
    internal static string ResolveLogPath(string? overrideDirectory, string appDataDirectory)
        => string.IsNullOrWhiteSpace(overrideDirectory)
            ? Path.Combine(appDataDirectory, "MonitorWellness", "debug.log")
            : Path.Combine(overrideDirectory.Trim(), "debug.log");

    private const long MaxSizeBytes = 2 * 1024 * 1024; // 2MB
    private const long TrimToBytes = 512 * 1024;       // keep the most recent 512KB when rotating

    /// <summary>Path to the local rolling log, exposed for explicit user-requested support bundles.</summary>
    public static string FilePath => LogPath;

    private static readonly object Gate = new();
    private static readonly System.Text.UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static bool _directoryReady;
    private static long _knownSize = -1; // -1 = look at the file before the next write

    /// <summary>
    /// Appends one line. Writers inside the process are serialised, the directory is created once,
    /// the file size is tracked instead of asked for on every line, and a sharing violation (a
    /// support tool reading the log, antivirus) is retried once rather than silently dropping the line.
    /// </summary>
    public static void Write(string message)
    {
        byte[] line = Utf8.GetBytes($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {LogRedaction.Redact(message)}{Environment.NewLine}");
        lock (Gate)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    EnsureDirectory();
                    RotateIfTooLarge(line.Length);
                    using var stream = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    stream.Write(line);
                    _knownSize += line.Length;
                    return;
                }
                catch (IOException) when (attempt == 0)
                {
                    _knownSize = -1;
                    Thread.Sleep(15);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Logging must never be the thing that crashes the app.
                    _knownSize = -1;
                    return;
                }
            }
        }
    }

    private static void EnsureDirectory()
    {
        if (_directoryReady)
            return;

        string? directory = Path.GetDirectoryName(LogPath);
        if (directory is not null)
            Directory.CreateDirectory(directory);
        _directoryReady = true;
    }

    private static void RotateIfTooLarge(int incomingBytes)
    {
        if (_knownSize < 0)
        {
            var info = new FileInfo(LogPath);
            _knownSize = info.Exists ? info.Length : 0;
        }
        if (_knownSize + incomingBytes <= MaxSizeBytes)
            return;

        // Keep only the tail: read the last TrimToBytes bytes and drop everything before the
        // first newline in that chunk, so the file starts on a clean line boundary.
        long length = new FileInfo(LogPath).Length;
        if (length < TrimToBytes)
        {
            _knownSize = length;
            return;
        }

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
        _knownSize = tail.Length - start;
    }
}
