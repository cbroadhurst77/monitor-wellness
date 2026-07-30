# Changelog

User-facing changes only — written for someone deciding whether to update, not a developer
picking the project back up. See [IMPLEMENTATION.md](IMPLEMENTATION.md) for the full
development log (architecture decisions, bugs found, why things work the way they do) and
[TECHNICAL_UX_REVIEW.md](TECHNICAL_UX_REVIEW.md) for the independent gap analysis driving
recent work.

## Unreleased

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

### Fixed
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
