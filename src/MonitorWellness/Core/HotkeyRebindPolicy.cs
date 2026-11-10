namespace MonitorWellness.Core;

public enum HotkeyRebindChoice
{
    /// <summary>The new shortcut registered: switch to it and release the old one.</summary>
    UseNew,
    /// <summary>The new shortcut is taken by another app, but there was no working one to lose: adopt it anyway so the person is told.</summary>
    UseNewEvenThoughItFailed,
    /// <summary>The new shortcut is taken by another app and the old one still works: keep the old one.</summary>
    KeepPreviousBecauseNewFailed,
}

/// <summary>
/// The decisions behind changing the migraine shortcut. The app used to release the old
/// shortcut before trying the new one, so a rebind to a shortcut another app already owned left
/// the person with no shortcut at all. Now the new one is tried first and the old one is only
/// given up once the new one is secured. Pure, so it can be tested.
/// </summary>
public static class HotkeyRebindPolicy
{
    /// <summary>True when the working shortcut already is the requested one, so there is nothing to do. (Registering the same combination twice would fail even for this same app.)</summary>
    public static bool IsUnchanged(bool currentRegistered, uint currentModifiers, uint currentKey, uint newModifiers, uint newKey)
        => currentRegistered && currentModifiers == newModifiers && currentKey == newKey;

    public static HotkeyRebindChoice ChooseAfterAttempt(bool hadWorkingShortcut, bool newShortcutRegistered)
        => newShortcutRegistered
            ? HotkeyRebindChoice.UseNew
            : hadWorkingShortcut
                ? HotkeyRebindChoice.KeepPreviousBecauseNewFailed
                : HotkeyRebindChoice.UseNewEvenThoughItFailed;
}
