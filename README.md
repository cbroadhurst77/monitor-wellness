# Monitor Wellness

A Windows tray app that adjusts monitor color temperature and brightness across multiple
displays based on sunrise/sunset, with a dedicated **migraine mode** you can trigger the
moment a migraine or aura starts.

Rebuilt from an earlier PowerShell + f.lux + AutoHotkey prototype into a standalone .NET/WPF
application — no dependency on third-party tools.

**This is not a medical device and is not a substitute for professional medical care.**
Migraine mode's color choice is based on published research on light and migraine
photophobia (see below), but that research studied ambient light exposure, not a screen
overlay — this app applies the same underlying idea through the best mechanism available in
software, not a reproduction of the clinical studies. If you experience migraines, talk to a
doctor about treatment; use this as one comfort tool alongside that, not instead of it.

No telemetry, no network calls, no accounts — everything (settings, diagnostic logs) stays
local in `%AppData%\MonitorWellness\`.

## Features

- Smooth day/night color temperature and brightness scheduling based on your location's
  actual sunrise/sunset (not a fixed clock time), with today's sunrise/sunset times shown
  right in the settings window
- Find your location by searching a town/postcode, clicking a world map, or entering exact
  coordinates — whichever's easiest
- Per-monitor overrides — exclude a monitor entirely, or scale how much it dims relative to
  the others
- **Migraine mode**: instant activation (a muted green tint, heavy dim, and contrast
  reduction, no fade-in delay — green rather than the warm tint you might expect, based on
  research on light and migraine photophobia; see [EVALUATION.md](EVALUATION.md)) via a
  global hotkey or the tray menu, with a gradual fade back to normal on deactivation, and an
  optional auto-off timer (disabled by default — a real migraine can last hours)
- **Pause the schedule** for 30 min / 1 hour / 2 hours / until tomorrow — useful for
  color-sensitive work like photo/video editing
- Settings window with live sliders — color/brightness/contrast changes preview directly on
  your real screens before you commit to saving them
- **Portable**: run the downloaded exe directly with no installer, and optionally enable
  "Start with Windows" from the tray menu — no admin install required to try it
- "Identify Monitors" — flashes each display's internal name on-screen, since Windows'
  on-screen monitor numbers don't reliably match device enumeration order

## Status

Early — first 4-week build cycle just completed. Functional and tested on real hardware
(Windows 11, 3-monitor setup), but not yet signed, not yet published, and the installer's
full install/uninstall flow needs a manual verification pass. See
[IMPLEMENTATION.md](IMPLEMENTATION.md) for the full build log, architecture decisions, and
known issues, and [EVALUATION.md](EVALUATION.md) for an honest assessment of engineering
maturity and the actual strength of the scientific claims behind the color/brightness
choices — including where evidence is solid, where it's mixed, and where a design choice is
a reasonable comfort feature rather than a proven intervention.

## Building from source

Requires the .NET 8 SDK (or newer) and Windows.

```bash
dotnet build src/MonitorWellness/MonitorWellness.csproj -c Debug
```

To produce a self-contained single-file executable (no separate .NET runtime install
required to run it):

```powershell
dotnet publish src/MonitorWellness/MonitorWellness.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o publish
```

That output is fully portable — copy `MonitorWellness.exe` anywhere and run it directly, no
installer or admin rights needed. Use the "Start with Windows" tray menu item afterward if you
want it to launch automatically (that one step does need administrator approval, since it
registers a Task Scheduler entry — everything else about running the app doesn't).

## Building the installer

Requires [Inno Setup](https://jrsoftware.org/isinfo.php) 6+. Build the publish output above
first, then:

```powershell
"C:\path\to\Inno Setup 6\ISCC.exe" installer\MonitorWellness.iss
```

Produces `dist/MonitorWellness-Setup-<version>.exe`. The installer requires administrator
privileges (needed to register the Task Scheduler auto-start entry and write to Program
Files) — expect a UAC prompt.

## Why Task Scheduler instead of a Registry Run key for auto-start?

More robust across some edge cases a Run key doesn't handle well, and easier to cleanly
remove on uninstall. See [IMPLEMENTATION.md](IMPLEMENTATION.md) for the full reasoning behind
this and other architecture decisions (gamma ramp vs. DDC/CI, overlay window design, why
settings live in a JSON file instead of the registry, etc.).

## License

MIT — see [LICENSE](LICENSE). Bundles one third-party asset (the location picker's world
map) under its own license — see [ATTRIBUTIONS.md](ATTRIBUTIONS.md).
