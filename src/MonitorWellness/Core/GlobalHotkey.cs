using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace MonitorWellness.Core;

/// <summary>
/// Registers a single system-wide hotkey via RegisterHotKey, replacing the AutoHotkey-based
/// hotkey in the old prototype (MigraineToggle.ahk's Ctrl+Alt+M). RegisterHotKey needs a
/// window handle to deliver WM_HOTKEY to, so this creates an invisible message-only window
/// (HWND_MESSAGE) purely to receive that message — it never appears on screen, in the
/// taskbar, or in Alt-Tab.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 0xA1F3; // arbitrary, just needs to be unique within this process

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000; // don't refire while the key is held down

    private readonly HwndSource _messageWindow;
    private readonly bool _registered;

    /// <summary>False if RegisterHotKey failed (most commonly ERROR_HOTKEY_ALREADY_REGISTERED, another app owns this combination). Callers should surface this visibly, not just log it — a silently-failed hotkey looks like the app is broken.</summary>
    public bool IsRegistered => _registered;

    public event Action? Pressed;

    public GlobalHotkey(uint modifiers, uint virtualKey)
    {
        var parameters = new HwndSourceParameters("MonitorWellnessHotkeyWindow")
        {
            Width = 0,
            Height = 0,
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
            WindowStyle = 0,
        };
        _messageWindow = new HwndSource(parameters);
        _messageWindow.AddHook(WndProc);

        _registered = RegisterHotKey(_messageWindow.Handle, HotkeyId, modifiers | MOD_NOREPEAT, virtualKey);
        if (!_registered)
        {
            int error = Marshal.GetLastWin32Error();
            // Most likely cause: another app already owns this combination (Win32 error
            // 1409, ERROR_HOTKEY_ALREADY_REGISTERED). Not fatal — the tray menu still works
            // as a fallback, so just log it here; the caller decides how to surface it.
            DebugLog.Write($"RegisterHotKey failed for modifiers=0x{modifiers:X}, vk=0x{virtualKey:X}, Win32Error={error}");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registered)
            UnregisterHotKey(_messageWindow.Handle, HotkeyId);
        _messageWindow.RemoveHook(WndProc);
        _messageWindow.Dispose();
    }
}
