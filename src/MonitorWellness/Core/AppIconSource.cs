using System.Windows.Media.Imaging;

namespace MonitorWellness.Core;

/// <summary>
/// Every window in this app (Settings, Onboarding, About, Troubleshooting, Migraine Rating,
/// Profile Name) was showing WPF's generic default title-bar/taskbar icon instead of the app's
/// own — the tray icon itself (App.xaml.cs's LoadEmbeddedIcon) already loads the same embedded
/// .ico via System.Drawing.Icon for the NotifyIcon API specifically; this loads the same asset
/// as a WPF-native BitmapSource so plain Window.Icon can use it too. Cached (Lazy) since every
/// window would otherwise reload and re-decode the same bytes from the assembly.
/// </summary>
public static class AppIconSource
{
    private static readonly Lazy<BitmapSource?> Icon = new(Load);

    /// <summary>The app's default (migraine-off) icon, or null if it couldn't be loaded — callers should treat null as "leave Window.Icon unset," never throw.</summary>
    public static BitmapSource? Default => Icon.Value;

    private static BitmapSource? Load()
    {
        try
        {
            using var stream = System.Reflection.Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("MonitorWellness.Assets.migraine_off.ico");
            if (stream is null)
                return null;

            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            frame.Freeze(); // shared across every window's Icon property — must be immutable to be usable off the thread that created it
            return frame;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"AppIconSource: failed to load icon: {ex.Message}");
            return null;
        }
    }
}
