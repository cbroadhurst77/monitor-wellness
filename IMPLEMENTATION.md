# Monitor Wellness App — Implementation Tracker

Rebuild of the PowerShell + f.lux + Dimmer.exe + AutoHotkey prototype
(`MigraineMode_on.ps1`, `MigraineMode_off.ps1`, `MigraineToggle.ahk`, `Dimmer/Dimmer.exe`)
into a standalone Windows tray app. Those old files stay in place as behavioral reference
only — none of their code carries forward as-is.

Target: shippable v1 in 4 weeks, solo, free/open source, Windows-only.

## Locked decisions

- **Stack**: .NET 8 / WPF, `System.Windows.Forms.NotifyIcon` for the tray icon.
- **Color temperature**: Win32 `SetDeviceGammaRamp` per monitor. No dependency on f.lux.
- **Brightness**: custom topmost click-through overlay windows per monitor (replaces
  `Dimmer.exe` — do not bundle the third-party binary, license unconfirmed).
- **Hotkey**: `RegisterHotKey` P/Invoke, not a raw keyboard hook, not AutoHotkey.
- **Settings**: JSON file under `%AppData%`, not the registry.
- **Auto-start**: Task Scheduler entry, not a Run key.
- **Installer**: Inno Setup.
- **Distribution**: GitHub Releases.
- **Location input**: superseded post-v1 — manual lat/long entry, town/postcode search
  (Nominatim), and a click-to-set world map picker are all now supported (Week 6). Still no
  *automatic* IP-based geolocation — the user always explicitly searches or clicks, nothing
  happens without that action.
- **DDC/CI hardware brightness**: deferred to v1.1. v1 relies on gamma ramp + overlay only.

## Deferred to v1.1+

- [ ] DDC/CI hardware monitor brightness control (`Dxva2.dll`, `SetMonitorBrightness`)
- [ ] Laptop panel brightness via WMI (`WmiMonitorBrightnessMethods`)
- [ ] IP-based geolocation auto-prefill for lat/long (manual search/map picker now covers the
  "I don't know my exact coordinates" problem without this — see Week 6)
- [ ] Auto-update checker against GitHub Releases
- [ ] Code signing cert
- [ ] Per-monitor contrast control for migraine mode

---

## Week 1 — Engine core (no UI)

- [x] Project scaffold: WPF app, solution structure, target `net8.0-windows`
  (`src/MonitorWellness/`)
- [x] Solar elevation calculation (NOAA algorithm) from lat/long + system clock
  (`Core/SolarCalculator.cs`) — verified against known reference points
  (`tools/SmokeTest`): near-zero elevation at sunrise/sunset, strongly negative at
  midnight, high at midday, all consistent with London late-July expectations.
- [x] Monitor enumeration — list active displays, handle add/remove/sleep-wake without
  crashing (`Core/MonitorEnumerator.cs`)
- [x] Gamma ramp control wrapper (`SetDeviceGammaRamp` P/Invoke) — set color temp per
  monitor (`Core/GammaRampController.cs`)
- [x] Manual verification: color temp change confirmed working across all 3 real monitors
  (`tools/GammaCheck`) — see finding below
- [x] Curve function: solar elevation → color temperature (smooth, not linear-by-clock-time)
  (`Core/ScheduleCurve.cs`)

### Finding: gamma ramp cannot carry migraine-mode-level warmth or real dimming

Confirmed empirically on this hardware (`tools/GammaCheck`), and consistent with
long-documented Windows behavior: `SetDeviceGammaRamp` **rejects the call outright** if any
color channel's ramp deviates from identity by more than roughly a factor of 2 (effective
per-channel multiplier must stay above ~0.5). Measured results:

| Case | Result |
|---|---|
| 6500K @ 100% | pass |
| 3400K @ 100% (planned night default) | pass |
| 3000K @ 100% | **fail** (blue factor ≈0.43, below cutoff) |
| 1900K @ 100% (planned migraine default) | **fail** |
| 3400K + 85% brightness together | **fail** (each alone passes; combined factor fails) |
| 1900K + 65% brightness together | **fail** |

`GammaRampController.ApplyColorTemperature` now self-clamps the brightness assist to a safe
floor so it never triggers an outright rejection — but when the color temperature alone is
already past the safety margin (roughly below ~3300K), no brightness value can fix it; the
call fails regardless. Re-verified after the fix: 3400K+85% now succeeds (assist is silently
forced toward 0%, i.e. no gamma-based dimming applied, but the color temp is still set);
1900K still fails outright at any brightness, exactly as expected.

**Consequence for the plan below**: gamma ramp is only reliable for the moderate day/night
range (~3400K–6500K) and cannot be used for real brightness dimming at all. Migraine mode's
deep warmth (1900K) and all meaningful dimming — for both normal scheduling and migraine
mode — must come from the overlay window layer, not gamma ramp. The Week 2/3 items below
are updated to reflect that the overlay window carries **both tint (color) and dim
(brightness)**, not dimming alone — this mirrors what `Dimmer.ini`'s per-screen `Tint` field
was already doing in the old prototype, just not yet reimplemented.

## Week 2 — Scheduling + dimming

- [x] Background scheduler tick (30s) driving gamma ramp from the curve (color temp only,
  self-clamped to a safe range by `GammaRampController` per the Week 1 finding)
- [x] Overlay window per monitor: topmost, click-through, adjustable alpha (dim) **and**
  color tint (`Core/OverlayWindow.xaml(.cs)`, `Core/OverlayController.cs`) — positioned in
  physical pixels via `SetWindowPos`, click-through via `WS_EX_TRANSPARENT`, rebuilds itself
  on `SystemEvents.DisplaySettingsChanged` (monitor add/remove/resolution change)
- [x] Settings JSON: lat/long, day/night color temp + brightness bounds, excluded monitors,
  **and per-monitor dim multiplier** (`Core/AppSettings.cs`) — added the per-monitor
  multiplier after real-hardware testing showed a uniform dim level doesn't work across
  different monitors (see finding below)
- [x] Load/save settings on startup and on change (`Core/SettingsStore.cs`,
  `%AppData%\MonitorWellness\settings.json`)
- [x] Manual verification: schedule produces a smooth day→night ramp with overlay + color
  temp together — verified live across all 3 real monitors

### Finding: a single global dim level doesn't work across different monitors

Testing a uniform 40% brightness overlay across all 3 monitors, the Asus monitor looked
noticeably too dark relative to the others (likely just how that panel behaves — possibly
viewing angle — not a software bug). This is why `AppSettings.MonitorDimMultiplier` exists:
it scales how much a specific monitor dims *relative to* the global day/night target, rather
than every monitor following the same absolute value. Current setting: Asus monitor
(`\\.\DISPLAY3`) has a multiplier of 0.6.

**Caveat**: 0.6 was confirmed correct at an artificially strong 40%-brightness test level
(exaggerated on purpose so the effect was visible in daytime testing). The multiplier scales
the *dim amount* (1 − brightness), so at the real, much gentler default night brightness
(0.85, i.e. only 15% dimming), the same 0.6 multiplier will produce a much smaller absolute
effect on that monitor. **Re-check this value once it's actually experienced under a real
night schedule** — it may need to go lower (more aggressive relative reduction) to have a
similar felt effect at realistic dim levels.

### Finding: monitor device names aren't safe path strings

Used `Path.GetFileName()` to derive a short label (e.g. "DISPLAY1") from a device name like
`\\.\DISPLAY1` for the Identify Monitors feature below. It silently returned `""` for every
monitor — confirmed via an isolated test that .NET's `Path` parsing treats the `\\.\` prefix
as a UNC host+share pattern (`.` as "server", `DISPLAY1` as "share"), concluding the entire
string is a root with no filename left. Fixed by doing a plain last-backslash split instead
of using `Path` at all — Win32 device path strings aren't real filesystem paths and
shouldn't be passed through `Path` methods.

### Added: "Identify Monitors" tray menu item

Needed this sooner than expected — Windows' on-screen monitor numbers (Display Settings)
don't reliably match the internal device name enumeration order, so there was no trustworthy
way to tell the user "which monitor is `\\.\DISPLAY3`" other than asking them to look. The
tray menu now has "Identify Monitors", which flashes each monitor's device name on-screen for
6 seconds (`OverlayController.IdentifyMonitors`, `OverlayWindow.ShowLabel/HideLabel`). This
will also be needed by the Week 4 settings UI for per-monitor overrides, so it's staying in
as a real feature, not just a debug tool.

### Added: temporary diagnostic logging

`Core/DebugLog.cs` writes timestamped lines to `%AppData%\MonitorWellness\debug.log` for
startup, overlay window creation, and tray menu actions, with a global
`DispatcherUnhandledException` hook. This is what actually found the `Path.GetFileName` bug
above in minutes instead of guessing blind. Explicitly marked as pre-release-only in its own
doc comment — **Week 4 must decide whether to strip it or gate it behind a debug flag**
before the installer build ships (added to the Week 4 list below).

### Fixed: DPI awareness for the WPF-hosted app

Initially declared `ApplicationHighDpiMode` in the `.csproj` (a WinForms-specific mechanism)
and removed the manifest-based DPI declaration to silence an analyzer warning (WFAC010).
That was backwards for this app: it's WPF-hosted (WinForms is only used for the NotifyIcon),
and WPF's own Per-Monitor-V2 support is driven by the app manifest, not by
`Application.SetHighDpiMode` — which never fires here since there's no WinForms-generated
entry point. Restored the manifest declaration and suppressed WFAC010 in the `.csproj` with
a comment explaining why it doesn't apply to this app.

## Week 3 — Migraine mode

- [x] Instant activation: hard cutover to migraine color temp + dim level, driven primarily
  by the overlay tint/dim (`Core/MigraineModeController.cs`) — gamma ramp set to its safe
  floor (~3400K) plus a deep amber overlay tint (`#321408` @ 72% opacity by default) carries
  the rest of the warmth gamma ramp can't reach
- [x] Gradual deactivation: fades color temp, tint color (toward black), and opacity back to
  the *live* schedule-computed target over 20s (200ms ticks) — not a hardcoded target, so it
  fades to wherever the day/night schedule actually is at that moment
- [x] Migraine mode suspends the schedule engine while active or fading
  (`MigraineModeController.SuspendsNormalSchedule`) — the 30s schedule tick checks this and
  skips entirely, so the two never fight over the same monitors
- [x] Global hotkey (`RegisterHotKey` via a hidden `HWND_MESSAGE` window,
  `Core/GlobalHotkey.cs`) toggling migraine mode — replaces `MigraineToggle.ahk`
- [x] Tray icon: on/off state, icon swap (reused `icons/migraine_on.ico` /
  `migraine_off.ico` from the old prototype), menu (Toggle / Activate / Deactivate /
  Identify Monitors / Exit) — parity with the old AHK menu, plus Identify Monitors
- [x] Manual verification: hotkey and tray menu both toggle correctly, state stays in sync
  — confirmed live, including a rapid-toggle edge case (re-activating 3s into a 20s fade-out)
  that correctly interrupted the fade and restarted cleanly, per the debug log

### Finding: the default hotkey conflicted with another app

`RegisterHotKey` for Ctrl+Alt+M (matching the old AHK script's default) failed with Win32
error 1409 (`ERROR_HOTKEY_ALREADY_REGISTERED`) — some other running app already owns that
combination on this machine. Not a bug: `GlobalHotkey` logs the failure via `DebugLog` and
degrades gracefully (the tray menu still works). Switched the default to Ctrl+Alt+Shift+M,
which registered cleanly. **This is exactly the kind of conflict a real user will hit** —
Week 4's settings UI needs a hotkey rebind control, already on the list below, and ideally
the app should surface a visible (not just logged) warning when registration fails so a
silent hotkey failure doesn't look like the app just doesn't work.

## Week 4 — Polish, packaging, ship

- [x] Fixed a real robustness gap: `GammaControllerManager` (new) mirrors
  `OverlayController`'s rebuild-on-topology-change behavior — the gamma controller list was
  previously built once at startup and never rebuilt, so a monitor added/removed after launch
  would silently go stale. Both gamma and overlay layers now also explicitly reapply on
  `SystemEvents.PowerModeChanged` (Resume), since some driver configurations reset gamma ramp
  state across sleep/resume.
- [x] Surfaced a visible warning (tray balloon tip, not just a log line) when hotkey
  registration fails — see Week 3 finding.
- [x] Settings window (`SettingsWindow.xaml(.cs)`): lat/long, day/night color temp +
  brightness, migraine overlay color/opacity, hotkey rebind (captured live via
  `PreviewKeyDown` + `Keyboard.Modifiers`, using `KeyInterop.VirtualKeyFromKey` to get the
  Win32 VK code), per-monitor exclude + dim multiplier, and an "Identify Monitors" button
  reusing the Week 2 feature. Saving mutates the live `AppSettings` instance in place and
  calls back into `App` to rebuild the hotkey and reapply the schedule immediately — no
  restart needed. Verified live: hotkey rebind captured and worked end-to-end (Ctrl+Shift+M
  captured, saved, and successfully toggled migraine mode via the new combo).
- [x] Task Scheduler auto-start registration on install (`installer/MonitorWellness.iss`
  `[Run]`/`[UninstallRun]` sections) — see finding below on why this needs an elevated installer
- [x] Inno Setup installer script (`installer/MonitorWellness.iss`), compiles cleanly to
  `dist/MonitorWellness-Setup-0.1.0.exe` (~49MB, self-contained single-file publish, no .NET
  runtime install required for end users)
- [x] Smoke test: app startup, all 3 monitors, migraine mode, settings window all verified
  live repeatedly throughout Weeks 1-4. **Not verified by me**: the actual installer GUI/UAC
  flow and post-install state (Task Scheduler entry, Start Menu shortcut, uninstall) — see
  finding below on why, and Chris still needs to run this manually.
- [ ] Publish v1 to GitHub Releases — needs a repo; not created yet

### Finding: the tray icons were completely blank, and single-file publish doesn't carry Content files

Two separate bugs surfaced while testing the settings window's icon swap. First, the tray
showed a solid black box instead of an icon. Investigated by direct pixel inspection rather
than guessing: `migraine_on.ico`/`migraine_off.ico` (from the old prototype) are **fully
transparent, every single pixel** (`nonTransparentPixels=0/1024`) — they were blank from
whatever originally generated them, unrelated to anything in this rebuild. Replaced with
programmatically-drawn placeholder icons (`tools/GammaCheck` was reused as a one-off
generator) — a simple brain-shaped silhouette, blue for normal/off and red for migraine/on.
First attempt (6 overlapping bumps) read as a flower cluster at real tray size; simplified to
2 bumps + a center groove, confirmed legible at 16-32px. Explicitly a placeholder for real
artwork later — swapping it needs no code changes, just replacing the two `.ico` files.

Second, separately: testing a self-contained single-file publish
(`dotnet publish ... -p:PublishSingleFile=true`) crashed on startup with
`DirectoryNotFoundException` looking for `Assets\migraine_off.ico` next to the exe. `Content`
items with `CopyToOutputDirectory` are not carried alongside a single-file exe at runtime —
only a normal build/publish copies them. Fixed by switching to `EmbeddedResource` (compiled
directly into the assembly, loaded via `Assembly.GetManifestResourceStream`), which works
identically whether single-file or not. This would have been a silent last-minute breakage
if it hadn't been caught by actually testing the real publish output, not just the Debug build.

### Finding: registering an auto-start scheduled task needs an elevated installer

Set `PrivilegesRequired=lowest` in the Inno Setup script specifically to avoid a UAC prompt.
That turned out to be incompatible with the Task Scheduler auto-start requirement: isolated
by testing directly (bypassing the installer entirely) that `schtasks /create /sc onlogon`
fails with "Access is denied" under a standard token, while the identical command with
`/sc once` succeeds fine — confirming it's specifically the `onlogon` trigger type that needs
admin rights to *register* (the `/rl limited` flag still makes the task *run* with standard,
non-elevated rights once it fires — only registering it needs elevation). Changed
`PrivilegesRequired` to `admin`, which is standard for a Windows installer that also writes
to Program Files anyway.

**Could not fully verify the installer end-to-end from this session**: running it with
`/VERYSILENT` produced inconsistent exit codes (0 once, 1 on repeat attempts, no log file
ever written) and `Start-Process -Verb RunAs` also failed — consistent with UAC consent
requiring a real interactive desktop session that this automated tool environment doesn't
reliably have. This is an environment limitation, not a script defect: the installer
compiles cleanly, and the exact `schtasks` command it runs was independently confirmed
correct. **Chris needs to double-click the installer manually** to complete this
verification (expect a normal UAC prompt — that's expected and correct now).

### Decision: keep DebugLog for v1, not just pre-release

Originally planned to strip or gate `Core/DebugLog.cs` behind a flag before shipping. Kept it
instead — it's what found the `Path.GetFileName` bug, the DPI manifest mistake, the silent
settings-load failure, and the blank-icon bug, each in minutes via direct log inspection
rather than guessing from a description. That same value applies to a real user's future bug
report. Added a size cap (2MB, rotates to the most recent 512KB) so long-term use doesn't
grow the file unboundedly.

### Finding: SettingsStore was silently swallowing JSON parse errors

While testing the settings window, the Asus monitor's dim multiplier (set to 0.6 back in
Week 2) had reverted to the default of 1.0 with no explanation. Root-caused by direct
reproduction rather than guessing: every settings.json written by hand via a bash heredoc
during earlier testing turned out to be **invalid JSON** — writing `"\\.\DISPLAY3"` (3
backslash characters) instead of the correctly-escaped `"\\\\.\\DISPLAY3"` (6 backslash
characters needed to represent 3 literal backslashes in JSON). Confirmed with
`JsonDocument.Parse`: `'D' is an invalid escapable character within a JSON string`.
`SettingsStore.Load()`'s catch block silently fell back to `new AppSettings()` (100%
defaults) on any parse failure, with **no log line at all** — so this had presumably been
happening every time the app started with a hand-edited settings file, invisibly.

Two fixes: `SettingsStore.Load()` now logs both successful loads and failures via
`DebugLog`, so this can never be silently invisible again. And more importantly — **stop
hand-editing settings.json via shell heredocs entirely**; the app's own
`JsonSerializer.Serialize` call (used by the Settings window's Save) always produces
correctly-escaped JSON, so the settings window is now the only way settings get changed
during testing, and should be treated as the canonical path going forward, not a fallback to
manual file edits.

---

## Open questions / risks to watch

- Gamma ramp behavior may vary by GPU driver — test on the actual hardware early (Week 1),
  not at the end. (Done for this hardware; unverified on other GPUs/drivers.)
- Overlay window behavior across monitor topology changes (docking/undocking, sleep/wake) —
  gamma controller rebuild-on-topology-change and sleep/wake reapply now handled (Week 4),
  but only tested via `SystemEvents` firing correctly, not an actual physical sleep/resume
  cycle or hot-plug — worth a real test before relying on it.
- **Product name confirmed: "Monitor Wellness."** No rename needed — it was already the name
  used throughout the code (assembly, namespace, folder names, installer AppName, Task
  Scheduler task name, `%AppData%\MonitorWellness\` settings path).
- **The new deep-night phase (Week 5) has only been verified synthetically, not with real
  eyes after dark.** The math checks out (see Week 5 section), but nobody has actually looked
  at the screen once elevation drops past -12° to confirm the warm-brown overlay blend looks
  intentional rather than muddy or off.
- **Installer is unsigned.** Expect a SmartScreen "unknown publisher" warning on first run
  for any real user — code signing is already deferred to v1.1+ above, but worth deciding
  whether that's acceptable for an initial free/OSS release or a blocker.
- **The installer's actual UAC/install/uninstall flow could not be verified on this
  machine — it's an IT-managed device that blocks installing unapproved software.** This
  also explains some earlier oddities from this same dev session (a deny-only Administrators
  group token, permission-denied errors in unrelated AppData folders) — this machine's admin
  rights are filtered/restricted by policy, consistent with a managed corporate device. The
  installer itself compiles cleanly and the exact `schtasks` command was independently
  verified correct (see the Week 4 finding), but the actual install/uninstall flow needs to
  be tested on a machine without this restriction — a personal/unmanaged Windows machine, or
  a VM.

---

## Week 5 — Research-backed schedule and migraine tint redesign

The original day/night schedule and migraine mode tint were built on plausible-sounding
assumptions (warm = cozy = good for migraines) rather than actual research. This pass did a
real literature/industry search and changed two things as a direct result.

### Migraine mode: amber/red tint replaced with green

**This is the more important finding of the two.** The original migraine mode used a deep
amber/red-brown overlay tint (`#321408`), on the assumption that "warm" is inherently
soothing for light-sensitive users — the same logic that led f.lux and most consumer
night-light tools toward warm color temperatures.

That assumption doesn't hold for *migraine photophobia specifically*. Noseda & Burstein,
["Migraine photophobia originating in cone-driven retinal
pathways"](https://academic.oup.com/brain/article/139/7/1971/2464334) (*Brain*, 139(7),
1971-1986, 2016) exposed migraine patients mid-attack to different light colors and measured
reported pain intensity directly. Finding: **white, blue, amber, and red light all increased
headache pain; only a narrow band of green light reduced it** — at low intensity, green
measurably decreased pain rather than just failing to worsen it. This is now a widely-cited
result (covered by [Harvard Medicine](https://eye.hms.harvard.edu/publications/migraine-photophobia-originating-cone-driven-retinal-pathways),
[ScienceDaily](https://www.sciencedaily.com/releases/2016/05/160517083042.htm), and the basis
for commercial FL-41-style migraine glasses from
[Avulux](https://avulux.com/pages/understanding-the-science-behind-avulux-migraine-glasses)
and similar). The mechanism is melanopsin-containing ipRGCs (peak sensitivity ~481nm blue,
~587nm amber/yellow — both aggravating), distinct from the classic rod/cone visual pathway.

Changed `AppSettings.MigraineOverlayColorHex` default to `#173620`, a muted, desaturated
green — deliberately not a bright/saturated green, since the research is about a narrow
soothing band, not "more green is more better," and a garish tint would defeat the purpose
of a comfort feature. Verified live: visibly a muted green now, not amber. Kept
`MigraineOverlayOpacity` (~0.7) — the research also ties benefit to *low intensity*, and
overlay opacity is already how this app controls perceived intensity.

Removed the separate `MigraineKelvin` setting (previously a mild warm gamma-ramp shift
alongside the overlay tint). At the overlay's opacity, it dominates the perceived color
enough that the gamma layer underneath doesn't need a distinct value — migraine mode's gamma
now just reuses `NightKelvin`, simplifying the settings surface by one field.

### Normal schedule: added a third "deep night" phase

Cross-checked the existing Day (6500K) / Night (3400K) defaults against f.lux's own
published defaults ([justgetflux.com FAQ](https://justgetflux.com/faq.html) and
[coverage](https://www.ghacks.net/2009/04/06/computer-monitor-lighting-software-flux/)):
day 6500K, sunset/evening 3400K, **bedtime 2700K** — confirming the existing two-point
schedule already matched f.lux's day/evening stage exactly, but was missing its third,
warmer bedtime stage.

Circadian/melatonin research supports going warmer still near actual sleep time — evening
light below 3000K, and commonly-cited guidance of ~1800-2400K in the immediate lead-up to
sleep, is associated with less melatonin suppression than cooler light (blue-rich light
above 5000K suppresses it most; see the [Mudita
summary](https://mudita.com/community/blog/how-light-temperature-affects-melatonin-production/)
and the broader [systematic review](https://www.tandfonline.com/doi/full/10.1080/07420528.2018.1527773)
of light exposure and circadian rhythm).

Gamma ramp is already at its hardware-safe floor (~3400K) by the time full night is reached
(Week 1 finding) — it cannot go to 2700K or lower on this hardware. So the extra bedtime-like
warmth has to come from the overlay layer instead, the same pattern already established for
migraine mode. Added:

- `ScheduleCurve.DeepNightThresholdDeg = -12.0` (end of nautical twilight) as a third anchor
  point, smoothly blended in via `GetDeepNightFactor()` starting at the existing
  `NightThresholdDeg = -6.0` (end of civil twilight) — reusing the same smoothstep
  interpolation technique as the existing Day/Night blend, not a new mechanism.
- `AppSettings.DeepNightBrightness` (0.7, deeper than the existing 0.85 Night value) and
  `AppSettings.DeepNightOverlayColorHex` (`#190C04`, a very dark warm brown) — the overlay's
  dim color now blends from plain black toward this warm brown as deep night approaches,
  approximating the extra warmth a lower color temperature would provide, layered on top of
  gamma's already-maxed-out 3400K.
- `OverlayController.ApplyDim` and `App.ComputeScheduleTarget` updated to carry this color
  through instead of a hardcoded black — `MigraineModeController`'s fade-out was also updated
  to target this dynamic color (previously hardcoded to fade toward black, which would have
  been wrong once fading out during deep night).

Verified with synthetic elevation values (`tools/SmokeTest`) rather than waiting for real
nighttime: civil twilight end (`deepNightFactor=0.00`), a midpoint (`deepNightFactor=0.53`,
brightness correctly between 0.85 and 0.7), and true midnight (`deepNightFactor=1.00`,
brightness exactly 0.70) all matched the intended blend exactly. The migraine tint change was
verified live (visually confirmed green); the deep-night phase could only be verified
synthetically since it wasn't actually nighttime during this session — **worth a real visual
check after dark**.

### Settings migration for the existing running instance

The already-populated `settings.json` had an explicit (old amber) value for
`MigraineOverlayColorHex`, which meant the new C# default wouldn't have taken effect for it
automatically (JSON deserialization only fills in *missing* keys from defaults). Rather than
hand-edit the file — the exact mistake that caused the Week 4 silent-JSON-failure bug — wrote
a one-off migration using the app's own `SettingsStore.Load()`/`Save()` (guarantees correct
JSON), updating only the tint color and leaving the user's other customizations (hotkey
rebind, per-monitor dim multipliers) untouched.

---

## Week 6 — Automated tests and easier location entry

### Automated test suite (`tests/MonitorWellness.Tests`)

EVALUATION.md's top engineering recommendation was closing the "zero automated tests" gap —
every prior verification in this project was a hand-run, throwaway console script
(`tools/SmokeTest`, `tools/GammaCheck`), effective at finding bugs in the moment but leaving
nothing behind to catch a regression later. Added an xUnit project covering the pure-logic
core with no Win32/WPF dependency — `SolarCalculator`, `ScheduleCurve`, `ColorTemperature` —
targeting plain `net8.0` rather than `net8.0-windows` since none of it needs the Windows
Desktop runtime.

**Writing these tests immediately found a real, live bug**, exactly the outcome automated
tests are supposed to produce: `ColorTemperature.IsSafeForGammaRamp(3400)` returned `false`.
3400K is this app's own `NightKelvin` default and has been confirmed working on real hardware
repeatedly throughout this project — meaning the Kelvin safety validation added to the
settings window earlier this session would have **rejected the app's own default value** the
next time someone opened Settings and hit Save without touching that field. Root cause:
`MinSafeChannelFactor` was set to 0.55 as "a small margin above the observed ~0.5 cutoff," but
3400K's actual factor is 0.5301 — inside that margin. Lowered the constant to 0.52, which sits
strictly between the confirmed-fail exact boundary (0.50) and 3000K's confirmed-fail factor
(0.4310), while correctly including 3400K. Re-verified against real hardware afterward
(`tools/GammaCheck`) — no regression, 4000K/3400K still pass, 3000K still correctly fails.

A second test (assuming elevation is symmetric around solar noon within a small window) was
also wrong — replaced with a more robust monotonicity check, since the symmetry assumption
didn't hold well enough over multi-hour windows at this latitude/season to be a reliable test
invariant.

### Easier location entry: search by place/postcode, and a clickable map

Two additions to the settings window, both feeding the same `Latitude`/`Longitude` fields:

- **`Core/GeocodingService.cs`**: looks up a town, city, or postcode/zip via
  [Nominatim](https://nominatim.openstreetmap.org/) (OpenStreetMap's free geocoding API, no
  API key required). This is the only network call anywhere in the app — everything else
  stays fully local — and it only fires when the user explicitly clicks "Find." Uses a
  descriptive `User-Agent` per Nominatim's usage policy.
- **A clickable world map** in the settings window. The map image
  (`Assets/worldmap.jpg`, embedded resource, CC BY-SA 3.0 — see `ATTRIBUTIONS.md`) is a true
  equirectangular projection, so screen-to-coordinate conversion is a straightforward linear
  formula (longitude across full width -180..+180, latitude down full height +90..-90) with no
  letterboxing to account for, since the container's fixed XAML size matches the source
  image's aspect ratio. Clicking anywhere sets Latitude/Longitude directly; a marker shows the
  current location and updates live as either text field changes.

Precision here doesn't need to be exact — sunrise/sunset timing barely changes over tens of
kilometers, so "roughly click where you live" is entirely sufficient for what this app uses
the coordinates for.

Both features verified live: geocoding search and map click both correctly updated the
schedule's location.

---

## Week 7 — Slider-based settings with live preview

Replaced the Day/Night Kelvin, Day/Night brightness, and migraine opacity text boxes with
sliders that apply directly to the real gamma ramp/overlay as they're dragged — the point
being to let a value be *judged on screen* before committing to it, not just typed and hoped
for. Save persists whatever the sliders currently show; Cancel needs no explicit revert logic
at all, since closing the window (either way) just stops the preview from overriding the
normal schedule tick, which then naturally displays whatever's actually saved.

Mechanically: `App._settingsPreviewActive` suspends `RunScheduleTick()` for as long as the
settings window is open (mirroring how migraine mode already suspends it), and the window's
`Closed` handler clears that flag and calls `RunScheduleTick()` immediately so nothing stays
stuck showing a preview after the window goes away. `MigraineModeController`'s fade-out was
updated to fade toward whatever the *live* schedule target's dim color actually is rather
than a hardcoded black, since that could now be a warm brown (deep night) depending on when
the fade happens to run.

A live "this Kelvin value won't work on this hardware" warning was added to the Day/Night
sliders, reusing `ColorTemperature.IsSafeForGammaRamp` — dragging into unsafe territory during
preview just silently doesn't change anything on screen without this, which would otherwise
look like a bug rather than a hardware limit.

Verified live: dragging sliders visibly changes the screen in real time, and Cancel correctly
leaves everything as it was before opening Settings.

## Week 8 — Five feature additions from a broader improvement brainstorm

After a wider "what would make this all-singing-and-dancing" discussion, implemented five of
the higher-value items, each verified against real hardware/behavior before considering it
done, consistent with this project's testing discipline throughout. Automated test count grew
from 24 to 40 in this pass.

### 1. Sunrise/sunset time display

Added `SolarCalculator.FindSunriseUtc`/`FindSunsetUtc` — a coarse 5-minute scan across the day
to bracket the crossing of the standard -0.833° sunrise/sunset threshold (accounting for solar
radius + atmospheric refraction), then bisected for sub-second precision. Displayed in the
settings window, recomputed live as location changes (via map click, search, or manual entry).
Tested against plausible London late-July ranges and a polar-location edge case (should return
null for "no sunset," not throw or loop forever) — `tests/SunriseSunsetTests.cs`.

### 2. Pause schedule for N hours

Tray menu submenu (30 min / 1 hour / 2 hours / Until tomorrow) plus a "Resume Schedule" item
enabled only while paused. Deliberately does *not* touch the gamma ramp/overlay when pausing —
whatever's already on screen just stays there untouched, which is the actually-useful behavior
for its intended use case (temporarily neutral screen for color-sensitive work). "Until
tomorrow" resolves to 08:00 the next calendar day regardless of current time — simpler and
more predictable than trying to guess a wake time; extracted as `SchedulePause
.ComputeUntilTomorrowLocal` specifically so this one non-trivial calculation has test coverage
(`tests/SchedulePauseTests.cs`) rather than being buried in App.xaml.cs untested.

### 3. Migraine mode auto-revert timeout

`AppSettings.MigraineAutoRevertMinutes`, default **0 (disabled)** — a real migraine can last
many hours, so auto-reverting on a fixed timer by default could be actively unwelcome if
someone's genuinely still mid-migraine when it fires. Opt-in via a settings slider (0-240
min). The timer itself (armed on `Activate()`, cancelled on manual `Deactivate()`) has no
automated test — it's Win32/WPF-timer-dependent, consistent with the known gap EVALUATION.md
already notes for that layer.

### 4. Portable mode: in-app "Start with Windows" toggle

The bigger point of this one: it directly solves the exact problem hit earlier this session
(IT-managed machine blocking installer execution). `Core/AutoStartManager.cs` lets the running
exe register/unregister its own Task Scheduler auto-start entry from a tray menu toggle — no
installer required, works from a portable, unzipped copy of the self-contained publish output.

Registering an onlogon-triggered task needs elevation (confirmed directly during Week 4:
`schtasks /create /sc onlogon` fails under a standard token). Implemented via
`Process.Start` with `Verb = "runas"` on just the `schtasks.exe` call, not the whole app.
**Finding**: on this specific machine, that elevation happened with no visible UAC prompt at
all — the call simply succeeded (confirmed both by the logged exit code and independently via
`schtasks /query` showing a correctly-configured task, "Run As User: chris", "At logon time").
This is consistent with the deny-only Administrators token and other managed-machine
behaviors already noted in IMPLEMENTATION.md/EVALUATION.md — some corporate UAC policies
auto-elevate admin accounts without prompting rather than disabling UAC outright. **On a
machine with normal UAC prompting behavior, a real consent dialog should appear** — this
hasn't been observed directly since this machine doesn't produce one. Removal (`Unregister`)
verified working the same way, confirmed via `schtasks /query` returning "not found" afterward.
Argument-string construction is unit tested (`tests/AutoStartManagerTests.cs`); the actual
elevated process launch is not (same Win32-dependent-layer gap as above).

### 5. Contrast reduction for migraine mode

The one item here that needed real investigation before writing any production code, per this
project's established discipline of not assuming hardware behavior. Built a throwaway
`tools/GammaCheck` harness applying a floor-raised ("contrast-compressed") gamma ramp —
`output = (contrastReduction + (1 - contrastReduction) * normalizedInput) * ceiling`, i.e.
raising the black point toward the white point while leaving the ceiling untouched — and
tested it directly against this hardware.

**Finding**: this is a genuinely different safety mechanism than the factor-of-2 rule
characterized in Week 1. Contrast reduction *alone* (no brightness-assist scaling in the same
call) stays accepted by the driver up to at least 30%, even at 3400K where the resulting blue
channel floor factor drops to ~0.05 — nowhere near the ~0.5 boundary that governs uniform
brightness scaling. Combining it with brightness-assist scaling in the same gamma call *does*
reproduce the old rejection failure — but this app's architecture already keeps brightness
dimming entirely on the overlay layer, so gamma only ever needs to carry color temperature and
now contrast together, never combined with a scaled-down ceiling.

Implemented as `GammaRampController.ApplyColorTemperatureWithContrast` (deliberately has no
brightness parameter at all, enforcing the separation at the API level) and
`ColorTemperature.ApplyContrastCompression` (the pure formula, unit tested). Wired into
`MigraineModeController` (new `AppSettings.MigraineContrastReduction`, default 0.15, fades
back to 0 during deactivation alongside everything else) and the settings window (slider,
0-30%, live preview).

### Outstanding from the broader improvement brainstorm (as of Week 8)

The Week 8 discussion surfaced about a dozen candidate improvements; five were built that
pass. See Week 9 below for six more of the remaining items.

## Week 9 — Six more feature additions from the same brainstorm

Continuing straight down the Week 8 "outstanding" list. Same discipline as before: real
hardware/behavior verification before considering anything done, and automated test coverage
grown alongside. Test count grew from 40 to 50 in this pass.

### 1. Reset to Defaults button in Settings

`ResetButton_Click` re-populates color/brightness/migraine controls from a fresh
`new AppSettings()` via a new `LoadPreferencesFrom(AppSettings source)` helper (extracted from
the existing load path so both the initial load and Reset share one code path). Deliberately
excludes Latitude/Longitude/ExcludedMonitors/MonitorDimMultiplier — those are personal
location and hardware setup, not "preferences" in the sense Reset is meant to cover.
Confirmation dialog before resetting; live-previews the reset values immediately after,
consistent with every other control in the window.

### 2. Hotkey confirmation feedback

The hotkey handler in `App.xaml.cs` now shows a balloon tip ("Migraine Mode ON/OFF") on every
toggle, and optionally plays a system sound (`AppSettings.PlaySoundOnMigraineToggle`, default
**false**). Motivation: migraine mode is most likely to be triggered mid-aura, when vision is
already compromised, so a purely visual confirmation isn't reliable on its own — but sound is
opt-in because phonophobia (sound sensitivity) is a common migraine comorbidity in the same
population this feature serves. The balloon tip is unconditional; the sound is the only
opt-in part.

### 3. Auto-start drift check on startup

`AppSettings.AutoStartEnabled` now tracks *intent* (did the user turn this on) separately from
the live Task Scheduler state. On startup, `App.xaml.cs` compares the two — if the user
enabled auto-start previously but the task is no longer registered (Windows update, IT policy,
manual removal), a warning balloon explains what likely happened rather than auto-start just
silently stopping. Not the same as verifying a real reboot actually launched the app (still
outstanding, see below) — this only detects that the *registration* has drifted, checked at
every startup.

### 4. Per-monitor color-only exclusion

New `AppSettings.ColorExcludedMonitors`, distinct from the existing `ExcludedMonitors`: a
monitor on this list keeps dimming with the rest of the schedule but is reset to identity
gamma (`GammaRampController.ResetToIdentity`) rather than shifting color temperature —
for a photo/video reference monitor that needs to stay color-accurate but doesn't need to
stay at full brightness all night. Settings window's per-monitor rows gained a second
"Color-accurate" checkbox alongside the existing Exclude checkbox; both the live preview path
and the real schedule tick (`RunScheduleTick`) respect it.

### 5. Migraine intensity presets (mild/severe)

Rather than a second fully independent set of tuned values, "mild" activation
(`MigraineModeController.Activate(mild: true)`) scales the *configured* overlay opacity and
contrast reduction by a fixed 0.6 multiplier — same color/hue, less intense. Simpler mental
model for the user ("lighter than my usual setting") than maintaining two presets. Tray menu
gained "Activate Migraine Mode (Full)" and "(Mild)" entries alongside the existing toggle;
`IsMild` is tracked through activation, deactivation (the fade-out correctly fades from the
scaled values, not the full ones), and the tray tooltip (shows a "(Mild)" suffix while active).

### 6. Bedtime-aware deep night

New `ScheduleCurve.GetBedtimeFactor(DateTime nowLocal, TimeSpan bedtimeOfDay, rampMinutes=90,
maxPastMinutes=600)` — an alternative, clock-time-driven path to the same 0.0-1.0 deep-night
factor that `GetDeepNightFactor` already produces from solar elevation. Ramps up over the 90
minutes before bedtime, holds at 1.0 through 10 hours after it, then eases back down —
handling bedtimes near midnight correctly via day-rollover normalization on the
minutes-from-bedtime calculation. `App.ComputeScheduleTarget()` combines the two factors via
`Math.Max`, so whichever signal (sun or clock) reaches deep night first wins; this matters most
in winter, when full solar deep night arrives long after a typical bedtime. New
`AppSettings.BedtimeLocal` (nullable "HH:mm" string, null = feature off, sun alone still
drives deep night). Settings window gained an enable checkbox + HH:mm text box under Day/Night
Schedule. Ten new tests cover the ramp-up, the hold window's both ends, the ramp-down, and
both midnight-crossing directions explicitly (`tests/ScheduleCurveTests.cs`) — this was the one
item in this batch complex enough to need edge-case coverage beyond a single hardware check.

### Outstanding from the broader improvement brainstorm (as of Week 9)

- **Per-monitor color temp override** — still only the dim *multiplier* is per-monitor;
  Kelvin is still global across all monitors.
- **Verify auto-start actually worked after a real reboot** — the Week 9 drift check confirms
  the Task Scheduler *registration* persisted, but nothing has confirmed the app actually
  launched and is running after a genuine logon (not just a manual process start).
- **Saved profiles**, **first-run onboarding**, **dark mode for the settings window** —
  straightforward UX polish, not attempted.
- Already tracked elsewhere, still open: DDC/CI hardware brightness, HDR display handling,
  ambient light sensor support, auto-update checker, code signing (all in "Deferred to
  v1.1+" near the top of this file); accessibility — screen reader, high-contrast mode,
  keyboard-only navigation (tracked in EVALUATION.md, untouched by this pass); the Inno Setup
  installer's UAC/install/uninstall flow has still never been verified end-to-end (this
  machine's IT policy blocks installer execution — needs an unmanaged machine or VM).

## Week 10 — Closing out the brainstorm's UX-polish items

Finished the remaining generally-applicable items from the brainstorm list. Test count grew
from 50 to 54.

### 1. Per-monitor color temperature override

New `AppSettings.MonitorKelvinOffset` (device name -> Kelvin offset, default empty/0), added
to the schedule-computed Kelvin before applying it to that specific monitor's gamma ramp — for
a panel that reads visibly warmer or cooler than the others at the same nominal setting, the
same real-world variation `MonitorDimMultiplier` already exists to correct for brightness.
Rides on the existing driver-rejection safety net rather than adding new clamping logic: if an
offset pushes a monitor's ramp outside the safe range, `ApplyColorTemperature` already returns
`false` and `RunScheduleTick` already logs it (Week 6 finding) — no new failure mode to
handle. Settings window's per-monitor rows gained a fifth "Kelvin offset" box; both the live
preview path and the real schedule tick apply it.

### 2. Saved profiles

`Core/ProfileStore.cs` saves/lists/loads/deletes named snapshots of the Day/Night/migraine/
bedtime "preferences" — the same subset the Reset button and `LoadPreferencesFrom` already
treat as one unit, under `%AppData%\MonitorWellness\Profiles\<name>.json`. Deliberately
excludes location and per-monitor setup, same reasoning as Reset: those are hardware/place
facts, not a "mode" someone switches between. New Settings window section (dropdown + Load/
Save As/Delete) sits right under Location. Saving reuses the migraine-color-hex and bedtime
validation already written for the main Save button (extracted into two shared private
helpers, `TryValidateMigraineColorHex`/`TryValidateBedtime`, used by both `TryParseAll` and the
new `TryBuildPreferencesSnapshot`) so a profile can never be saved with values Save itself
would reject. "Save As..." prompts for a name via a small new `ProfileNameDialog` window rather
than pulling in `Microsoft.VisualBasic.Interaction.InputBox` for one text prompt. Verified live:
saved a profile from a changed slider value, moved the slider again, loaded the profile back,
confirmed it snapped to the saved value.

### 3. First-run onboarding

New `AppSettings.HasCompletedOnboarding` (default `false`, so it's naturally `false` exactly
once — the very first launch on a fresh install). `OnboardingWindow` shows automatically at the
end of `OnStartup` when it's still `false`: brief explanation of the color/brightness schedule,
Migraine Mode and its hotkey, where the tray menu lives, and a nudge to set a real location
(the app still defaults to London). Either button (open Settings now, or dismiss) marks
onboarding complete on `Closed` — there's no path that leaves it stuck re-showing.

### 4. Dark mode for settings/onboarding windows

New `Core/ThemeDetector.IsSystemDarkTheme()` reads Windows' own
`HKCU\...\Themes\Personalize\AppsUseLightTheme` — the *app* theme setting specifically, which
can differ from the *system* (taskbar/Start) theme; confirmed live on this machine, where the
system looked dark but `AppsUseLightTheme` was still 1 (light), and Settings correctly kept
rendering light until that specific setting was actually switched. `Theme/DarkTheme.xaml`
holds implicit styles for `TextBox`/`Button`/`ComboBox`/`CheckBox`/`Border`, merged into a
window's own `Resources` at construction time when dark mode is detected. Two real bugs
surfaced and fixed via live testing rather than assumption, consistent with this project's
whole testing discipline:

- A plain relative resource URI (`"Theme/DarkTheme.xaml"`) silently failed to resolve when the
  `ResourceDictionary` is constructed from code rather than from XAML — no exception, dark
  mode just never appeared. Fixed with the explicit `pack://application:,,,/...` form, which
  resolves correctly regardless of calling context.
- Even after that fix, only descendant controls (TextBox, etc.) picked up their dark styling —
  the window's own background/foreground did not. A `Window` does not pick up an implicit
  `TargetType=Window` style from a dictionary merged into its own `Resources` *after*
  `InitializeComponent()`; only descendants re-resolve correctly at that point. Fixed by
  setting `Background`/`Foreground` directly on the window instance in
  `ThemeDetector.ApplyDarkThemeIfNeeded` instead of relying on an implicit self-style.

Applied to `SettingsWindow`, `OnboardingWindow`, and the new `ProfileNameDialog`. Read once at
construction — no live re-theming if Windows' setting changes while a window is already open,
an accepted v1 limitation consistent with not over-building this particular corner.

### 5. Auto-start reboot diagnostics

Can't trigger or verify a real reboot from inside an agentic coding session — that's
disruptive to interrupt for testing, and the previous Week 9 drift check already covers the
*registration* half. What this adds is the tooling to check the other half without one:
`AutoStartManager.GetDiagnostics()` runs `schtasks /query /v /fo LIST` and
`AutoStartManager.ParseFields()` (pure, tested against a real captured sample) extracts
Status/Last Run Time/Last Result/Next Run Time — the only way to tell, from inside the app,
whether the scheduled task has ever actually *fired* rather than just still being registered.
New tray item "Auto-start Diagnostics..." shows this in a message box, with an explicit note
that Last Run Time only updates after a genuine Windows logon. **Still outstanding**: an actual
reboot-and-check hasn't been performed — this tooling exists so the user can do that check
themselves whenever convenient, without needing another coding session for it.

### Outstanding from the broader improvement brainstorm (as of Week 10)

- **Verify auto-start actually worked after a real reboot** — tooling now exists (Auto-start
  Diagnostics above); the actual reboot-and-check itself hasn't been performed.
- Already tracked elsewhere, still open: DDC/CI hardware brightness, HDR display handling,
  ambient light sensor support, auto-update checker, code signing (all in "Deferred to
  v1.1+" near the top of this file); accessibility — screen reader, high-contrast mode,
  keyboard-only navigation (tracked in EVALUATION.md, untouched by this pass); the Inno Setup
  installer's UAC/install/uninstall flow has still never been verified end-to-end (this
  machine's IT policy blocks installer execution — needs an unmanaged machine or VM); live
  re-theming if Windows' dark/light app mode changes while a window is already open (Week 10,
  item 4 — read once at window construction only).

## Change log

- 2026-07-30: Initial plan created from architecture discussion.
- 2026-07-30: Week 1 engine core built and verified on real hardware (3 monitors, Intel UHD
  Graphics). Solar calculator, monitor enumeration, gamma ramp controller, and curve
  function all working. Discovered gamma ramp cannot carry migraine-level warmth or real
  dimming — updated Week 2/3 scope so the overlay window owns both tint and dim.
- 2026-07-30: Week 2 overlay window, settings JSON, and per-monitor dim multiplier built and
  verified on real hardware. Found and fixed a `Path.GetFileName` bug (misparses Win32
  device path strings as UNC paths) and a DPI-awareness manifest mistake, both via a real
  bug report on the Asus monitor (confirmed as DISPLAY3). Added an "Identify Monitors" tray
  feature and temporary diagnostic logging, both earlier than planned but for good reason.
- 2026-07-30: Week 3 migraine mode built and verified live — instant activate, 20s
  live-target fade-out, global hotkey, tray icon/menu parity with the old AHK script. Found
  the default hotkey (Ctrl+Alt+M) conflicts with another app on this machine; switched
  default to Ctrl+Alt+Shift+M pending the Week 4 rebind UI.
- 2026-07-30: Week 4 packaging pass. Fixed a gamma-controller lifecycle gap (never rebuilt on
  topology change, unlike the overlay layer) and added sleep/wake reapply. Built the settings
  window with live hotkey rebind. Found the original tray icons were completely blank and
  replaced them with a simple generated brain silhouette; found and fixed a single-file
  publish packaging bug (Content files don't survive single-file publish — switched to
  embedded resources). Found SettingsStore was silently swallowing JSON parse errors — root
  cause of an earlier "lost setting" mystery, traced to invalid hand-written JSON from shell
  heredocs, not a real app bug. Built and compiled the Inno Setup installer with Task
  Scheduler auto-start; found registering an onlogon-triggered task requires an elevated
  installer, fixed by changing PrivilegesRequired to admin. Could not verify the installer's
  actual UAC/install flow end-to-end from this session (no reliable interactive UAC in this
  tool environment) — flagged for manual verification.
- 2026-07-30: Week 5 research pass. Real literature/industry research (Noseda & Burstein,
  *Brain* 2016; f.lux defaults; circadian/melatonin studies) found the migraine mode's amber
  tint was actually one of the *aggravating* colors for migraine photophobia, not a soothing
  one — switched to a research-backed muted green, confirmed live. Added a third "deep night"
  schedule phase (overlay-assisted extra warmth/dim beyond gamma's floor) to match f.lux's
  missing third "bedtime" stage and circadian research supporting warmer light near sleep.
  Verified the new phase's math synthetically; still needs a real visual check after dark.
- 2026-07-30: Live user tuning + a real hardware bug caught from it. The out-of-the-box
  daytime defaults (6500K, 100% brightness) were uncomfortable in practice — real-time
  feedback settled on 4000K / 55% brightness as personally comfortable, confirming the
  eye-strain research found earlier (daytime dimming, not just evening) was worth acting on,
  not just noting. Separately, adjusting Night color temp down to 2500K via the settings
  window silently failed on this hardware (confirmed directly: `ApplyColorTemperature`
  returns `false` for 2500K, `true` for 3400K) — exactly the Week 1 gamma ramp floor finding,
  but this time hit by a real user typing a value into a text box rather than by design.
  Fixed the immediate setting (back to the confirmed-safe 3400K floor) and closed the gap
  properly: `ColorTemperature.IsSafeForGammaRamp()` now backs live validation in the settings
  window (rejects unsafe values with an explanation before they can be saved), and
  `RunScheduleTick` now logs a rejected gamma call instead of silently ignoring the return
  value — previously the one place in the whole app that dropped this signal on the floor.
- 2026-07-30: Week 6. Added an xUnit test suite for the pure-logic core — writing it
  immediately caught a live bug (the settings window's own Kelvin safety check would have
  rejected the app's own NightKelvin default). Added town/postcode search (Nominatim) and a
  clickable world map to the settings window for easier location entry, closing the "manual
  lat/long only" limitation without adding automatic IP-based geolocation.
- 2026-07-30: Week 7. Replaced Day/Night/migraine text-box settings with sliders that preview
  live on real hardware before Save commits anything; Cancel needs no revert logic since it
  just lets the normal schedule resume.
- 2026-07-30: Week 8. Implemented 5 of ~12 items from a broader improvement brainstorm:
  sunrise/sunset display, pause-schedule-for-N-hours, migraine auto-revert timeout, an in-app
  portable-mode auto-start toggle (directly solving the IT-blocked-installer problem from
  Week 4), and migraine contrast reduction. The contrast reduction and auto-start elevation
  both needed real verification before shipping — contrast reduction turned out to be a
  genuinely different, more permissive safety mechanism than the Week 1 brightness-scaling
  rule; auto-start elevation succeeded with no visible UAC prompt on this machine, consistent
  with its already-unusual UAC/token behavior. Test count: 24 -> 40. Outstanding items from
  the brainstorm are listed explicitly rather than left implicit.
- 2026-07-30: Week 9. Implemented 6 more items from the same brainstorm: Reset to Defaults,
  hotkey confirmation feedback (visual + opt-in sound), auto-start drift detection on startup,
  per-monitor color-only exclusion, migraine mild/severe intensity presets, and bedtime-aware
  deep night. All six verified live against the running app (3 monitors) after building
  cleanly; the bedtime factor's day-rollover math got explicit edge-case tests since it was the
  one item complex enough to need them beyond a single hardware check. Test count: 40 -> 50.
  Remaining outstanding items updated accordingly.
- 2026-07-30: Week 10. Closed out the rest of the generally-applicable brainstorm items:
  per-monitor Kelvin override, saved profiles, first-run onboarding, dark mode for the
  settings/onboarding windows, and auto-start reboot diagnostics tooling. Dark mode caught two
  real bugs live rather than by inspection: a relative resource URI silently failing to
  resolve when set from code (fixed with an absolute pack URI), and a Window not picking up
  its own implicit style from a dictionary merged into its Resources after
  InitializeComponent, unlike its descendants (fixed by setting Background/Foreground
  directly). Verified all five live, including a full profile save/change/load round-trip.
  Test count: 50 -> 54. The only item left genuinely open from the whole brainstorm is
  confirming auto-start survives an actual reboot — the diagnostics tooling now exists for the
  user to check that themselves.
