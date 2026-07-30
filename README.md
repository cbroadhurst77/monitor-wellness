# Monitor Wellness

*(working name — not finalized yet)*

A Windows tray app that adjusts monitor color temperature and brightness across multiple
displays based on sunrise/sunset, with a dedicated **migraine mode** for immediate relief
when a migraine or aura starts.

Rebuilt from an earlier PowerShell + f.lux + AutoHotkey prototype into a standalone .NET/WPF
application — no dependency on third-party tools.

## Features

- Smooth day/night color temperature and brightness scheduling based on your location's
  actual sunrise/sunset (not a fixed clock time)
- Per-monitor overrides — exclude a monitor entirely, or scale how much it dims relative to
  the others
- **Migraine mode**: instant activation (deep warm tint + heavy dim, no fade-in delay) via a
  global hotkey or the tray menu, with a gradual fade back to normal on deactivation
- Settings window for location, schedule bounds, migraine appearance, and hotkey rebinding
- "Identify Monitors" — flashes each display's internal name on-screen, since Windows'
  on-screen monitor numbers don't reliably match device enumeration order

## Status

Early — first 4-week build cycle just completed. Functional and tested on real hardware
(Windows 11, 3-monitor setup), but not yet signed, not yet published, and the installer's
full install/uninstall flow needs a manual verification pass. See
[IMPLEMENTATION.md](IMPLEMENTATION.md) for the full build log, architecture decisions, and
known issues.

## Building from source

Requires the .NET 8 SDK (or newer) and Windows.

```
dotnet build src/MonitorWellness/MonitorWellness.csproj -c Debug
```

To produce a self-contained single-file executable (no separate .NET runtime install
required to run it):

```
dotnet publish src/MonitorWellness/MonitorWellness.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o publish
```

## Building the installer

Requires [Inno Setup](https://jrsoftware.org/isinfo.php) 6+. Build the publish output above
first, then:

```
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

MIT — see [LICENSE](LICENSE).
