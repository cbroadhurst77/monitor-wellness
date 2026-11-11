namespace MonitorWellness.Core;

/// <summary>How the break reminder reaches the person.</summary>
public static class BreakReminderStyles
{
    /// <summary>A small window in the corner of the monitor in use, with Start / In 5 minutes / Skip.</summary>
    public const string Window = "Window";

    /// <summary>A quiet tray notification only. Windows may hide it under Focus Assist.</summary>
    public const string Notification = "Notification";

    public static bool IsSupported(string? style)
        => string.Equals(style, Window, StringComparison.Ordinal) || string.Equals(style, Notification, StringComparison.Ordinal);

    /// <summary>Allowed lengths of the focus break, in seconds.</summary>
    public const int MinimumFocusSeconds = 10;
    public const int MaximumFocusSeconds = 120;
}
