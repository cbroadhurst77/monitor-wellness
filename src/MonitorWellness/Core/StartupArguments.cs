namespace MonitorWellness.Core;

/// <summary>Command-line switches the app starts with.</summary>
public static class StartupArguments
{
    /// <summary>"--restore": put the display back to normal and exit (no tray app).</summary>
    public const string Restore = "--restore";

    /// <summary>"--restart-wait &lt;pid&gt;": the previous instance (this process id) is exiting; wait for it before taking over.</summary>
    public const string RestartWait = "--restart-wait";

    public static bool HasSwitch(IEnumerable<string> args, string name)
        => args.Any(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads the process id after <see cref="RestartWait"/>. False if the switch is absent or not followed by a positive number.</summary>
    public static bool TryGetRestartWaitProcessId(IReadOnlyList<string> args, out int processId)
    {
        processId = 0;
        for (int i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], RestartWait, StringComparison.OrdinalIgnoreCase))
                return int.TryParse(args[i + 1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out processId) && processId > 0;
        }
        return false;
    }

    /// <summary>The arguments for the process that replaces this one when restarting.</summary>
    public static string BuildRestartArguments(int currentProcessId)
        => $"{RestartWait} {currentProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}
