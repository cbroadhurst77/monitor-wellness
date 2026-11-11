using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MonitorWellness.Core;

/// <summary>Reads and sets Windows' own light/dark theme.</summary>
public interface IWindowsThemeStore
{
    /// <summary>True for the light theme, false for dark, null when it cannot be read or must not be touched (high contrast).</summary>
    bool? IsLight { get; }

    void SetLight(bool light);
}

/// <summary>Pure decision: which theme belongs to the sun's height now.</summary>
public static class ScheduleTheme
{
    /// <summary>
    /// Dark once the sun is as low as the schedule's night threshold, light once it is as high as its day
    /// threshold, and no decision in between: through twilight the theme stays as it is, so it does not
    /// flap back and forth around one angle.
    /// </summary>
    public static bool? WantsLight(double elevationDegrees)
    {
        if (elevationDegrees <= ScheduleCurve.NightThresholdDeg)
            return false;
        if (elevationDegrees >= ScheduleCurve.DayThresholdDeg)
            return true;
        return null;
    }
}

/// <summary>
/// Switches Windows between its light and dark theme with the day/night schedule (opt-in). It changes the
/// theme only when the schedule moves from day to night or the other way, never to enforce it: if the
/// person switches by hand in between, that stays until the next change of phase. Turning the option off
/// leaves whatever theme is current.
/// </summary>
public sealed class ScheduleThemeFollower
{
    private readonly IWindowsThemeStore _store;
    private bool? _lastWantsLight;

    public ScheduleThemeFollower(IWindowsThemeStore store) => _store = store;

    /// <summary>Call regularly with the current sun elevation (the schedule's own, so fixed-time schedules work too).</summary>
    public void Update(bool enabled, double elevationDegrees)
    {
        if (!enabled)
        {
            _lastWantsLight = null; // so that turning it on again applies the current phase
            return;
        }

        bool? wantsLight = ScheduleTheme.WantsLight(elevationDegrees);
        if (wantsLight is null || wantsLight == _lastWantsLight)
            return;

        try
        {
            if (_store.IsLight is bool current && current != wantsLight.Value)
            {
                _store.SetLight(wantsLight.Value);
                DebugLog.Write($"ScheduleTheme: switched Windows to the {(wantsLight.Value ? "light" : "dark")} theme (sun at {elevationDegrees:F1} degrees).");
            }
            _lastWantsLight = wantsLight; // remembered once handled, so a manual change is not undone every half minute
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or ObjectDisposedException)
        {
            DebugLog.Write($"ScheduleTheme: couldn't change the Windows theme: {ex.Message}");
            // not remembered: tried again at the next update
        }
    }
}

/// <summary>The real thing: the per-user personalization registry values, plus the broadcast that makes open apps notice.</summary>
public sealed class RegistryWindowsThemeStore : IWindowsThemeStore
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;
    private static readonly IntPtr HwndBroadcast = new(0xFFFF);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam, uint flags, uint timeoutMilliseconds, out UIntPtr result);

    public bool? IsLight
    {
        get
        {
            // High contrast is an accessibility mode of its own; the theme values mean nothing there and must not be touched.
            if (System.Windows.SystemParameters.HighContrast)
                return null;

            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int flag ? flag != 0 : null;
        }
    }

    public void SetLight(bool light)
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(PersonalizeKey))
        {
            int value = light ? 1 : 0;
            key.SetValue("AppsUseLightTheme", value, RegistryValueKind.DWord);
            key.SetValue("SystemUsesLightTheme", value, RegistryValueKind.DWord);
        }

        // Tell running apps (and the taskbar, Start, File Explorer) to re-read it, without hanging on one that is stuck.
        SendMessageTimeout(HwndBroadcast, WmSettingChange, UIntPtr.Zero, "ImmersiveColorSet", SmtoAbortIfHung, 200, out _);
    }
}
