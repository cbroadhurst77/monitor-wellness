using System.IO;
using Microsoft.Win32;

namespace MonitorWellness.Core;

/// <summary>
/// Tells whether a call is probably going on, from the microphone and camera "in use" records Windows
/// keeps for the privacy indicators (Settings > Privacy > Microphone/Camera shows "last used"). Each app
/// that uses a device has a start and a stop time there; the stop time is 0 (or older than the start)
/// while the device is open. Nothing is read from the device itself and nothing leaves the machine.
/// Failure to read means "not in a call": this only decides whether a reminder waits a little, and must
/// never get in the way of anything else.
///
/// A microphone alone is not enough: recorders, dictation tools and assistants hold it open for hours
/// (a meeting recorder was found holding it all day on the development machine). So a call is a camera
/// in use by any app, or a microphone in use by an app that is known to make calls.
/// </summary>
public static class CallActivityDetector
{
    private const string ConsentStoreRoot = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

    /// <summary>
    /// A device that has been "in use" for longer than this is a stale record (an app that crashed
    /// while using it leaves the stop time at 0 forever), not a call.
    /// </summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(12);

    /// <summary>Parts of an app's name (its executable path or package name, as Windows records it) that mark it as a calling or meeting app.</summary>
    private static readonly string[] CallAppMarkers =
    {
        "teams", "zoom", "webex", "skype", "slack", "discord", "whatsapp", "signal", "telegram",
        "gotomeeting", "goto", "bluejeans", "lync", "around", "chrome", "msedge", "firefox", "brave", "opera",
    };

    public static bool IsInCall()
        => IsDeviceInUse("webcam", _ => true) || IsDeviceInUse("microphone", IsCallApp);

    /// <summary>Whether the way Windows names an app (for example "C:#Users#me#AppData#Roaming#Zoom#bin#Zoom.exe" or "MSTeams_8wekyb3d8bbwe") is a calling app. Pure and testable.</summary>
    public static bool IsCallApp(string recordedName)
        => !string.IsNullOrWhiteSpace(recordedName)
            && CallAppMarkers.Any(marker => recordedName.Contains(marker, StringComparison.OrdinalIgnoreCase));

    /// <summary>The meaning of one app's start and stop times (Windows FILETIME ticks). Pure and testable.</summary>
    public static bool IsInUse(long lastUsedStart, long lastUsedStop, DateTime nowUtc)
    {
        if (lastUsedStart <= 0 || lastUsedStart <= lastUsedStop)
            return false; // never used, or the last use has ended

        try
        {
            DateTime started = DateTime.FromFileTimeUtc(lastUsedStart);
            return started <= nowUtc.AddMinutes(5) && nowUtc - started <= StaleAfter;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false; // not a real time
        }
    }

    private static bool IsDeviceInUse(string capability, Func<string, bool> appCounts)
    {
        try
        {
            DateTime nowUtc = DateTime.UtcNow;
            using RegistryKey? root = Registry.CurrentUser.OpenSubKey($@"{ConsentStoreRoot}\{capability}");
            if (root is null)
                return false;

            foreach (string name in root.GetSubKeyNames())
            {
                using RegistryKey? app = root.OpenSubKey(name);
                if (app is null)
                    continue;

                if (string.Equals(name, "NonPackaged", StringComparison.OrdinalIgnoreCase))
                {
                    // Desktop (Win32) apps are one level down, one key per executable.
                    foreach (string desktopApp in app.GetSubKeyNames())
                    {
                        using RegistryKey? desktop = app.OpenSubKey(desktopApp);
                        if (desktop is not null && appCounts(desktopApp) && KeyIsInUse(desktop, nowUtc))
                            return true;
                    }
                }
                else if (appCounts(name) && KeyIsInUse(app, nowUtc))
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or ObjectDisposedException)
        {
            DebugLog.Write($"CallActivityDetector: couldn't read the {capability} usage records: {ex.Message}");
        }
        return false;
    }

    private static bool KeyIsInUse(RegistryKey key, DateTime nowUtc)
        => key.GetValue("LastUsedTimeStart") is long start
            && IsInUse(start, key.GetValue("LastUsedTimeStop") is long stop ? stop : 0, nowUtc);
}
