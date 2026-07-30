using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace MonitorWellness.Core;

/// <summary>
/// Reads Windows' own light/dark app theme preference so the settings/onboarding windows can
/// follow it, rather than always rendering with WPF's default light chrome regardless of what
/// the rest of the desktop looks like. Read once at window construction time -- there's no
/// live re-theming if the user flips the Windows setting while a window is already open.
/// </summary>
public static class ThemeDetector
{
    public static bool IsSystemDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int lightThemeFlag)
                return lightThemeFlag == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            DebugLog.Write($"ThemeDetector: failed to read system theme, defaulting to light: {ex.Message}");
        }
        return false;
    }

    /// <summary>Merges Theme/DarkTheme.xaml into the window's own Resources if Windows is set to dark mode. No-op otherwise.</summary>
    public static void ApplyDarkThemeIfNeeded(Window window)
    {
        if (!IsSystemDarkTheme())
            return;

        // A plain relative URI ("Theme/DarkTheme.xaml") only resolves when WPF's XAML parser
        // establishes an implicit base URI -- which doesn't happen when a ResourceDictionary
        // is constructed from code rather than from XAML. Confirmed directly: with the plain
        // relative form, this silently failed to apply (no exception, no dark theme) even
        // though Windows was actually in dark mode. The absolute pack URI form works from
        // code regardless of calling context.
        var dark = new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/MonitorWellness;component/Theme/DarkTheme.xaml")
        };
        window.Resources.MergedDictionaries.Add(dark);

        // The Window's own background/foreground can't come from an implicit style in this
        // same dictionary -- confirmed live that a Window doesn't pick up its own
        // TargetType=Window style when the dictionary is merged into its own Resources after
        // InitializeComponent (descendant controls like TextBox do pick up their styles fine
        // via the same merge; only the root element itself doesn't). Setting them directly
        // here sidesteps that gap; Foreground still inherits down to every child as normal.
        if (dark["MW.WindowBackground"] is System.Windows.Media.Brush background)
            window.Background = background;
        if (dark["MW.WindowForeground"] is System.Windows.Media.Brush foreground)
            window.Foreground = foreground;
    }
}
