# Changelog

User-facing changes only — written for someone deciding whether to update, not a developer
picking the project back up. See [IMPLEMENTATION.md](IMPLEMENTATION.md) for the full
development log (architecture decisions, bugs found, why things work the way they do) and
[TECHNICAL_UX_REVIEW.md](TECHNICAL_UX_REVIEW.md) for the independent gap analysis driving
recent work.

## Unreleased

### Changed
- The dimming overlay is now only on screen while it is actually dimming or tinting. In full daytime
  there are no overlay windows over your monitors at all (before, three invisible full-screen windows
  sat on top of everything all day). The app starts about 4 seconds faster (1.6 s instead of 5.9 s in
  one test on three monitors) and uses roughly 120 MB less memory while idle.
- Trade-off: because the overlay is no longer permanently on screen, the dimming part of migraine mode
  now appears about a quarter of a second after the shortcut (about 0.27 s in testing; the colour and
  contrast change is still immediate). The overlay windows are prepared in the background shortly after
  the app starts so it stays that quick.

### Fixed
- The update check can now find newer versions. It asked GitHub only for the "latest" release,
  which skips pre-releases, so it kept reporting 0.2.2 as the newest version and could never tell
  anyone about 0.2.5 to 0.2.8. It now looks at recent releases and includes pre-releases by
  default (Settings > Profiles & History, "Include pre-releases"; the update check itself is still
  off unless you turn it on).
- Changing the migraine shortcut to one another app already uses no longer leaves you with no
  shortcut: the new one is tried first, and if it is taken the previous one is kept and you are
  told.
- Setting up or removing "Start with Windows" now runs Windows' own `schtasks.exe` by its full
  system path instead of searching the app's folder first, which closes a way for a planted file
  to run when the app is used from a folder you can write to (such as Downloads).

## 0.2.8 — Display recovery and diagnostics update

### Fixed
- After the display layout changes (docking, a monitor waking, a resolution or HDR change) or you
  unlock your PC, the screen colour is now put back within about two seconds. Before, it could sit
  at neutral for up to 30 seconds until the next scheduled update, and Windows sometimes rejected the
  first colour change because the app was still holding an out-of-date connection to that display.
- If the app crashes on a background thread, the error is now written to the log (it used to leave
  no trace) and the app first tries to put your screen colour and monitor brightness back to normal.
- Error details in the log now include line numbers, which makes problems much quicker to track down.

### Changed
- Displays running in Windows HDR are now detected individually and at any time, not only at
  startup. While HDR is on for a display, Monitor Wellness only dims it and does not change its
  colour (colour changes are not reliable there), and tells you once when that happens. Turning HDR
  off brings colour warmth back. This follows the existing "overlay-only compatibility mode"
  setting, so you can switch it off if you would rather try colour changes on HDR.
- Internal: every file the app keeps now comes from one data-folder setting, which lets the
  settings, profiles, history and diagnostic-bundle code be tested for real. Test coverage of those
  went from none to about 80-90%.

## 0.2.7 — Recovery, reliability and schedule update

### Fixed
- If Monitor Wellness is closed abruptly (killed, crashed, or the PC loses power) while it has
  changed your screen colour or a monitor's own brightness, it now puts both back the next time
  it starts. Previously a monitor's dimmed brightness could be remembered as its "normal".
- Native/restore now returns a calibrated display to its colour-calibration curve instead of a
  plain linear one, so colour-accurate monitors keep their calibration when the schedule is
  paused, excluded, or the app exits.
- A "pause until tomorrow" or migraine auto-off that ended while the PC was asleep is now ended
  as soon as the PC wakes, rather than running on.
- After waking from sleep, migraine mode keeps its Mild setting and its auto-off time instead
  of restarting as a Full activation with a fresh timer.
- A single hiccup talking to a monitor (common right after wake or a dock change) no longer
  turns off hardware brightness for good: the app re-connects to the monitor and retries, and a
  monitor you had approved is retried automatically a few times (30 minutes, 2 hours, 12 hours)
  before it asks you to re-test it.

- Exporting a diagnostic bundle and opening the Display Capability Passport no longer freeze the
  tray while the app probes each monitor; they run in the background.
- Less constant background work: the app no longer looks up the foreground window every two
  seconds when you have no app rules and the full-screen guard is off, and the monitor list is
  reused for a couple of seconds instead of being re-queried several times a second during the
  migraine fade-out.
- The Windows account name is removed from file paths in the debug log and in diagnostic bundles
  (including lines already in an existing log when a bundle is made).
- The "Today: sunrise / sunset" line in Settings now uses your own calendar day, not the UTC
  date, which was wrong for some locations far from Greenwich.
- The auto-start diagnostics now say what the last result means (for example "the task is
  running now") instead of showing a bare number.
- No more false "auto-start seems to have been turned off" warnings at logon, and a faster
  start: the app now asks Windows' Task Scheduler directly instead of waiting on a slow command,
  and a check that can't complete is treated as "unknown", never as "off". A task that exists
  but has been disabled is now reported as disabled.
- A sensor that keeps failing (such as an ambient-light sensor) is now retried less often and
  logged once with its real error, instead of filling the log with identical lines.
- When Windows rejects a colour change, the app now re-creates its connection to that display
  and retries once, and the log says whether it was a transient state or a value that is
  genuinely too warm (it previously blamed the hardware for ordinary 5,000K values).
- "Start with Windows" no longer silently fails when the administrator prompt is cancelled or
  blocked, and no longer sets up auto-start for the wrong account when a different
  administrator approves the prompt. In both cases it now uses a standard entry for your own
  account and tells you it did.
- If your settings file can't be read (for example a hand-edited value is out of range),
  Monitor Wellness now keeps a dated copy (`settings.invalid-<date>.json`) and tells you,
  instead of quietly overwriting it with defaults the next time you save.
- The update check now also runs for a copy of the app that stays open for days, not only at
  startup, and tells you about each new release once.
- Upgrading while Monitor Wellness is running no longer fails or needs a reboot: the installer
  asks to close it first.

### Changed
- If you never chose a location, the app now says so instead of quietly using London's sunrise
  and sunset: a notice in Settings (with a "London is correct" button), a note in the status
  line and the tray tooltip, and a single reminder balloon on a later start. It goes away as soon
  as you pick your place or confirm London.
- Internal tidy-up with no change to how the app behaves: the schedule's decision order, the
  display-writing code, the break reminder and the tray menu were moved out of the largest source
  files, the settings window was split by topic, and the tests now run against the real app
  assembly.
- The dimming overlay is now hidden from screenshots, screen recordings and screen sharing, so
  other people see your real screen, not your dimmed and tinted view. This is on by default
  (Settings > Display); turn it off to capture exactly what you see. Needs Windows 10 version
  2004 or later.
- The standalone app file is about 60% smaller (82 MB instead of 199 MB) and the installed app takes about 110 MB less disk space, because it is now compressed inside its single file. The installer download is about 20 MB larger than before (74 MB instead of 53 MB), since an already-compressed app can't be compressed further by the installer.

### Added
- **Fixed clock times instead of the sun** (Settings > Schedule): choose when your day starts and
  when the evening starts, and no location is needed. The screen changes gradually around those
  times the way it does around sunrise and sunset, and deepens into night about two hours after
  the evening starts. It works for night shifts too (set the day to start in the evening).
- **Wake-up time** next to bedtime: the bedtime warmth and dimming now ends when you wake (easing
  off over half an hour) instead of always holding for about ten hours after bedtime. Leave it
  blank to keep the old behaviour.
- `MonitorWellness.exe --restore` (and a Start Menu shortcut, "Restore display (emergency)")
  puts your screen colour and any changed monitor brightness back to normal without starting
  the tray app.

## 0.2.6 — Maintenance update

### Changed
- No change to how the app looks or behaves. Internal tidy-up: the break-reminder snooze rules
  and the day/night schedule calculation were moved into separately tested components, and
  CI now builds and checks the installer on every release.

## 0.2.5 — Compatibility and reliability update

### Added
- Overlay-only compatibility mode is on by default for displays Windows explicitly identifies
  as Remote Desktop, virtual, USB, or indirect. It keeps gamma-ramp and physical-brightness
  commands away from those less predictable display paths while retaining the reversible
  dimmer overlay.
- Application-aware comfort rules can now apply a temporary built-in comfort plan while a
  chosen app or title-matched window is in the foreground. This does not save or overwrite the
  person's normal schedule; switching away restores it automatically.
- The tray menu now offers **Temporary Comfort Plan** for an immediate, non-persistent switch
  to Balanced, Reading, Colour-critical, Early sensitivity, or Recovery. Emergency Restore
  clears the temporary plan as part of returning the display to normal.
- The optional local History summary now compares the last seven days of recorded activity with
  the preceding seven days, while clearly distinguishing these personal records from medical
  insight.
- Break reminders can be snoozed from the tray for 30 minutes, an hour, or until tomorrow.
  Snoozes are session-only and do not alter saved preferences.
- Add Foreground App Rule captures the target executable from the tray without copying a window
  title, making application-aware rules quicker to set up without collecting document names.
- Optional fullscreen presentation guard restores the native display for fullscreen content and
  safely resumes afterward.
- Display Capability Passport now refreshes, copies its local report to the clipboard, and
  exposes privacy-preserving Flicker Guard session counters.
- Profiles & History now shows an optional weekly view of your own history (activations per
  week, and average helpfulness once a week has at least 3 ratings), with the date range and
  sample counts. Sparse data is shown as counts, not averages. It stays on your computer and is
  a personal record, not medical insight.

### Improved
- Switching between unrelated apps or windows no longer re-applies gamma and overlay state.
  This reduces unnecessary display churn while preserving immediate changes when an
  application-aware rule begins or ends.

### Fixed
- Scheduled dimming now reaches every active display that has a dim overlay, even when Windows
  refuses a gamma-ramp device context for it. Colour temperature still applies only to
  gamma-capable displays; exclusions and the primary-display brightness floor are unchanged.

### Changed
- Built and published on .NET 10 LTS (.NET 8 reached end of support on 10 November 2026). The
  release stays self-contained: no separate .NET install is needed.

## 0.2.3 — Sensory safety and comfort update

### Added
- **Display Capability Passport** under Diagnostics: a local, read-only explanation of the
  active displays, stable identity, HDR state, ambient-light availability, DDC/CI eligibility,
  and the safest brightness backend. It explicitly leaves PWM, temporal dithering, spectrum,
  and medical suitability as unknown when software cannot measure them.
- Five editable built-in comfort plans: Balanced, Reading, Colour-critical, Early sensitivity,
  and Recovery. They are previews and starting points, not treatment recommendations.
- Optional window-title conditions for application-aware comfort rules. A matching
  title-specific rule now takes precedence over a general rule for the same executable.
- Enterprise deployment and localisation/accessibility implementation guidance.

### Improved
- Display-topology notifications are coalesced before overlays rebuild, and unchanged overlay
  bounds are not repositioned. This reduces unnecessary topmost-window churn that could look
  like a flash during docking, app switching, or Windows display changes.
- CI now treats build warnings as errors.

## 0.2.2 — UX and reliability update

### Added
- An opt-in break reminder (the 20-20-20 rule): every N minutes, a tray balloon suggests
  looking at something ~20 feet away for ~20 seconds. Off by default; automatically skipped
  while Migraine Mode is active.
- An optional "how helpful was that?" prompt (1-5, or skip) after each Migraine Mode use, so
  the local History summary can show whether it's actually helping, not just how often it's
  used. Fully local and opt-in, alongside the existing history tracking.
- "Try Cooler & Brighter (A)" / "Try Warmer & Dimmer (B)" preset buttons for the Day/Night
  schedule, so the color temp/brightness values can be judged experientially first — the same
  idea already available for Migraine Mode's intensity.
- Export/Import Settings in the Settings window — back up everything (location, per-monitor
  setup, and preferences) to a file, or move it to another PC.
- Windows High Contrast mode is now respected: this app's own dark-theme styling steps aside
  and leaves system colors alone when High Contrast is on, rather than overriding them.
- Single-click the tray icon to toggle Migraine Mode instantly — no need to open the menu.
- "Help / About..." tray menu item reopens the welcome/onboarding screen at any time.
- "Open Logs Folder" tray menu item, for anyone filing a bug report.
- A collapsible "Why these colors?" section in Settings, next to the Day/Night Schedule and
  Migraine Mode sections, explaining what the underlying research does and doesn't show.
- A privacy note directly in Settings (previously only in the README).
- Exact numeric entry alongside every Day/Night/Migraine slider — type or paste a specific
  value instead of only dragging.
- Column headers above the per-monitor settings list, so the two number boxes per row are
  labeled instead of only explained by a hover tooltip.
- A startup warning if f.lux also appears to be running (it writes to the same underlying
  color-adjustment state this app does, and the two can visibly conflict).
- The app now refuses to run a second copy of itself at once, showing a message instead —
  previously two copies could run simultaneously and fight over the same monitors.
- A "Don't ask again" option directly on the post-Migraine-Mode rating prompt, so opting out
  doesn't require finding the same checkbox in Settings afterward.

### Fixed
- Settings window text (the privacy note, the "Why these colors?" evidence summaries, and
  every helper caption) is now larger and higher-contrast — much of it was small, low-contrast
  gray text that fell below accessible-contrast guidelines, which worked against the app's own
  purpose of reducing eye strain.
- The location search box and the world-map location picker now have proper labels for screen
  readers; previously the search box relied on a hover tooltip only, and the map had no
  accessible name at all.
- The welcome screen's "Migraine Mode gives instant relief" line has been softened to match
  the more careful language already used in the README and EVALUATION.md — describing what
  the tint is based on, not asserting a guaranteed clinical benefit.
- A rare internal error no longer silently leaves the app in an unrecovered state forever; it
  now attempts to recover automatically, and shows a visible warning if errors keep recurring.
- The HDR warning now actually works — a struct-layout mistake meant it likely never fired,
  even on an HDR-enabled display.
- Toggling "keep a local history" or "match ambient light" in Settings and then clicking
  Cancel now genuinely discards the change, matching what the window already told you it does.
- Excluding a monitor while the Settings window is open and a slider is being dragged no
  longer changes that monitor's color — it's now skipped entirely, as the checkbox describes.

## 0.1.0 — Initial release

- Day/night color temperature and brightness scheduling based on your location's real
  sunrise/sunset, with a third "deep night" bedtime-like phase.
- Migraine Mode: instant activation via hotkey or tray menu, with a research-backed muted
  green tint, contrast reduction, mild/full intensity presets, and an optional auto-off timer.
- Location entry by search, world-map click, or exact coordinates.
- Per-monitor overrides: exclude a monitor, keep one color-accurate, scale its dimming, or
  offset its color temperature independently of the rest.
- Pause the schedule for a fixed duration or until tomorrow.
- Saved profiles for quickly switching between preference sets.
- Portable — run the exe directly, with an in-app "Start with Windows" toggle that needs no
  separate installer.
- Dark mode for the Settings/onboarding windows, following the Windows app theme setting.
- No telemetry, no accounts, no network calls except an explicit, user-triggered location
  search.
