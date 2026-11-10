using System.IO;
using System.Text;

namespace MonitorWellness.Core;

/// <summary>
/// Writes a text file so that a crash or power loss leaves either the old or the new contents,
/// never an empty or half-written file: the new contents go to a temporary sibling, are flushed
/// to the disk (not just the OS cache), and only then replace the destination.
/// </summary>
internal static class DurableFile
{
    /// <param name="keepBackup">When the destination already exists, keep its previous contents as "&lt;name&gt;.bak".</param>
    public static void WriteAllText(string destinationPath, string contents, bool keepBackup = false)
    {
        string fullPath = Path.GetFullPath(destinationPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory))
            throw new IOException("The destination has no parent directory.");

        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(new UTF8Encoding(false).GetBytes(contents));
                stream.Flush(flushToDisk: true);
            }

            if (keepBackup && File.Exists(fullPath))
                File.Replace(temporaryPath, fullPath, BackupPath(fullPath));
            else
                File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public static string BackupPath(string path) => path + ".bak";
}
