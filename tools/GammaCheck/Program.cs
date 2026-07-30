using MonitorWellness.Core;

// One-off migration: the running app's settings.json already has an explicit (old amber)
// MigraineOverlayColorHex value, so the new green C# default won't take effect for it
// automatically. Load via the app's own SettingsStore (guarantees correct JSON round-trip),
// update just the field that needs it, leave everything else (hotkey rebind, per-monitor
// multipliers) untouched, and save back the same way.
var settings = SettingsStore.Load();

Console.WriteLine($"Before: MigraineOverlayColorHex={settings.MigraineOverlayColorHex}");
Console.WriteLine($"Before: DeepNightBrightness={settings.DeepNightBrightness}, DeepNightOverlayColorHex={settings.DeepNightOverlayColorHex}");

settings.MigraineOverlayColorHex = new AppSettings().MigraineOverlayColorHex; // pick up the new green default

SettingsStore.Save(settings);

var reloaded = SettingsStore.Load();
Console.WriteLine($"After:  MigraineOverlayColorHex={reloaded.MigraineOverlayColorHex}");
Console.WriteLine($"After:  DeepNightBrightness={reloaded.DeepNightBrightness}, DeepNightOverlayColorHex={reloaded.DeepNightOverlayColorHex}");
Console.WriteLine($"Preserved: NightBrightness={reloaded.NightBrightness}, HotkeyModifiers={reloaded.MigraineHotkeyModifiers}, HotkeyKey={reloaded.MigraineHotkeyKey}");
foreach (var (k, v) in reloaded.MonitorDimMultiplier)
    Console.WriteLine($"Preserved: MonitorDimMultiplier[{k}]={v}");
