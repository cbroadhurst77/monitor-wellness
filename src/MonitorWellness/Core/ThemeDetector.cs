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

    private const string BaseThemeUriSuffix = "Theme/BaseTheme.xaml";
    private const string DarkThemeUriSuffix = "Theme/DarkTheme.xaml";

    /// <summary>
    /// Merges Theme/DarkTheme.xaml into the window's own Resources if Windows is set to dark
    /// mode. No-op otherwise — including when Windows High Contrast mode is on. High Contrast
    /// is a distinct, accessibility-specific mode (SystemParameters.HighContrast) serving a
    /// different population (low vision) than the dark/light theme preference; it ships its
    /// own carefully-chosen system palette, and this app's own explicit Background/Foreground
    /// brush overrides (see below) would fight it rather than help — WPF's standard controls
    /// already pick up High Contrast's system colors automatically as long as nothing here
    /// overrides them.
    ///
    /// Also always merges Theme/BaseTheme.xaml (light-mode defaults for the MW.HelperText/
    /// MW.SafetyText/MW.WarningText/MW.ReadOnlyBackground brushes XAML pages reference via
    /// DynamicResource), whether or not dark mode is active — DarkTheme.xaml is merged
    /// afterward, as a sibling in the same MergedDictionaries collection, so its same-named
    /// keys win by merge order without needing BaseTheme.xaml to be removed first. Pages must
    /// use DynamicResource (not StaticResource) for these keys, both so an unmerged BaseTheme
    /// on a fresh window doesn't throw before this runs, and so EnableLiveThemeUpdates below
    /// can actually re-color already-rendered text on a live theme flip.
    ///
    /// Safe to call more than once on the same window (see EnableLiveThemeUpdates below) —
    /// any previously-merged dark dictionary is removed first, and Background/Foreground are
    /// reset to WPF's own default before re-deciding, so a live Windows theme flip either
    /// direction (light-&gt;dark or dark-&gt;light) leaves the window in the right state rather
    /// than stacking dictionaries or getting stuck on whichever theme was active at construction.
    /// </summary>
    public static void ApplyDarkThemeIfNeeded(Window window)
    {
        bool hasBaseTheme = false;
        for (int i = window.Resources.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            string? source = window.Resources.MergedDictionaries[i].Source?.OriginalString;
            if (source?.EndsWith(DarkThemeUriSuffix) == true)
                window.Resources.MergedDictionaries.RemoveAt(i);
            else if (source?.EndsWith(BaseThemeUriSuffix) == true)
                hasBaseTheme = true;
        }
        if (!hasBaseTheme)
        {
            window.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/MonitorWellness;component/" + BaseThemeUriSuffix)
            });
        }
        window.ClearValue(Window.BackgroundProperty);
        window.ClearValue(Window.ForegroundProperty);

        if (System.Windows.SystemParameters.HighContrast)
        {
            DebugLog.Write("ThemeDetector: Windows High Contrast is on — leaving system colors untouched, not applying dark theme");
            return;
        }

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
            Source = new Uri("pack://application:,,,/MonitorWellness;component/" + DarkThemeUriSuffix)
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

    /// <summary>
    /// Re-applies the dark/light theme live if Windows' theme setting changes while
    /// <paramref name="window"/> is still open — previously this was only ever read once, at
    /// construction (ApplyDarkThemeIfNeeded above), so a Settings/Onboarding window left open
    /// across a system theme change would silently keep showing the theme it opened with.
    /// Unsubscribes automatically when the window closes, since SystemEvents is a static,
    /// process-wide event that would otherwise keep this window's closure alive indefinitely.
    /// </summary>
    public static void EnableLiveThemeUpdates(Window window)
    {
        void Handler(object? sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General)
                return; // AppsUseLightTheme/HighContrast changes are reported under General

            if (!window.Dispatcher.CheckAccess())
            {
                window.Dispatcher.Invoke(() => ApplyDarkThemeIfNeeded(window));
                return;
            }
            ApplyDarkThemeIfNeeded(window);
        }

        SystemEvents.UserPreferenceChanged += Handler;
        window.Closed += (_, _) => SystemEvents.UserPreferenceChanged -= Handler;
    }
}
