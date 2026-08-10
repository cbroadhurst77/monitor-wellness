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

No telemetry, no accounts, no ads. Settings and diagnostic logs stay local in
`%AppData%\MonitorWellness\`. There are exactly two network calls in the whole app, and both
require you to either click something or opt in first — see [PRIVACY.md](PRIVACY.md).

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
- **Emergency Restore Screen** via `Ctrl+Alt+Shift+R` or the tray menu — immediately clears
  dim/tint overlays, restores native gamma, and pauses scheduling for one hour
- Normal scheduling always keeps the Windows primary display at least 20% visible; every
  other display has a 5% minimum as a recovery floor
- Settings window with live sliders — color/brightness/contrast changes preview directly on
  your real screens before you commit to saving them
- **Portable**: run the downloaded exe directly with no installer, and optionally enable
  "Start with Windows" from the tray menu — no admin install required to try it
- "Identify Monitors" — flashes each display's internal name on-screen, since Windows'
  on-screen monitor numbers don't reliably match device enumeration order
- Optional update check (Settings → Profiles & History → Updates, **off by default**) —
  once a day at most, checks for a newer release and links to it; never downloads or
  installs anything automatically

## A note on how dimming works

Brightness is reduced entirely via a semi-transparent overlay window, never by lowering the
monitor's actual backlight. That's not just a workaround for gamma ramp's limits (see
IMPLEMENTATION.md) — it's arguably better for this app's own audience: most monitor and laptop
backlights dim using PWM (pulse-width modulation, rapidly strobing the backlight rather than
truly dimming it), and PWM flicker is a documented migraine/eye-strain trigger in its own right,
especially at low brightness where PWM frequency is often lowest. The overlay approach has no
flicker. If a future version adds direct hardware (DDC/CI) brightness control, that mode will be
opt-in specifically so this flicker-free default isn't lost for anyone who came to this app
because of light sensitivity.

While a dim overlay is visible, Monitor Wellness also reasserts its always-on-top placement
every two seconds to recover from notifications, game bars, and other topmost windows. Some
exclusive-fullscreen, protected-video, and HDR paths remain outside an ordinary desktop
window's control; use Emergency Restore Screen if a display ever becomes difficult to use.

## Known conflicts with other color-management tools

Color temperature is applied via the same OS-level gamma ramp mechanism Windows Night Light,
f.lux, and some GPU vendor "night mode"/color utilities also use. Two tools writing to that
state at once can cause visible flickering or color that "randomly" reverts. Monitor Wellness
warns you on startup if f.lux appears to be running (a reliable check); it does not attempt to
detect Windows Night Light's on/off state, since that would require parsing an undocumented,
unofficial registry format with no way to verify it holds across Windows versions — if your
screen color seems to fight itself, check Night Light and any GPU vendor color/night-mode
utility and turn off all but one.

## Status

v0.2.0. A full UX/accessibility audit pass has landed (see
[UX_AUDIT_IMPLEMENTATION_TRACKER.md](UX_AUDIT_IMPLEMENTATION_TRACKER.md)) and the app has been
built, tested (128 automated tests, all passing), and run for real on one Windows 11 3-monitor
dev machine. Not yet: code-signed, tested on other hardware (GPU vendors, HDR displays, a
machine with a real ambient-light sensor — see [QA_CHECKLIST.md](QA_CHECKLIST.md) for exactly
what's outstanding), or reviewed by a lawyer (the EULA/Privacy Policy below are DIY drafts, not
legal advice). See [IMPLEMENTATION.md](IMPLEMENTATION.md) for the full build log and
architecture decisions, and [EVALUATION.md](EVALUATION.md) for an honest assessment of
engineering maturity and the actual strength of the scientific claims behind the
color/brightness choices — including where evidence is solid, where it's mixed, and where a
design choice is a reasonable comfort feature rather than a proven intervention.

## Support

Bug reports and questions: [GitHub Issues](https://github.com/cbroadhurst77/monitor-wellness/issues).

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
privileges to write to Program Files — expect a UAC prompt. It does not enable auto-start.
Users who want that behavior can explicitly choose **Start with Windows** from the tray menu,
which requests its own UAC approval.

## Release gate

Commercial releases must be Authenticode-signed and timestamped before distribution. After
signing the published executable and installer, verify both their signatures, versions, and
SHA-256 hashes before publishing:

```powershell
.\tools\Verify-Release.ps1 `
  -ApplicationPath .\publish\MonitorWellness.exe `
  -InstallerPath .\dist\MonitorWellness-Setup-0.2.2.exe `
  -ExpectedVersion 0.2.2 `
  -ManifestPath .\dist\MonitorWellness-0.2.2-release-manifest.json
```

The script intentionally fails for unsigned, invalidly signed, or version-mismatched artifacts.

## Why Task Scheduler instead of a Registry Run key for auto-start?

More robust across some edge cases a Run key doesn't handle well, and easier to cleanly
remove on uninstall. See [IMPLEMENTATION.md](IMPLEMENTATION.md) for the full reasoning behind
this and other architecture decisions (gamma ramp vs. DDC/CI, overlay window design, why
settings live in a JSON file instead of the registry, etc.).

## License

Proprietary as of v0.2.0 (31 July 2026) — see [LICENSE](LICENSE) and [EULA.md](EULA.md).
Versions published before that date remain available under the MIT License they were
originally released under; see LICENSE for the historical MIT text and the exact cutover.
Bundles one third-party asset (the location picker's world map) under its own license — see
[ATTRIBUTIONS.md](ATTRIBUTIONS.md).
