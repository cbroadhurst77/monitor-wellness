using MonitorWellness.Core;

var settings = SettingsStore.Load();
Console.WriteLine($"Before: NightKelvin={settings.NightKelvin}");

settings.NightKelvin = 3400; // confirmed safe floor on this hardware

SettingsStore.Save(settings);

var reloaded = SettingsStore.Load();
Console.WriteLine($"After:  NightKelvin={reloaded.NightKelvin}");
