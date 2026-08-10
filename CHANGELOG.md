# Changelog

User-facing changes only — written for someone deciding whether to update, not a developer
picking the project back up. See [IMPLEMENTATION.md](IMPLEMENTATION.md) for the full
development log (architecture decisions, bugs found, why things work the way they do) and
[TECHNICAL_UX_REVIEW.md](TECHNICAL_UX_REVIEW.md) for the independent gap analysis driving
recent work.

## 0.2.4 — Presentation and support update

### Added
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

### Improved
- Switching between unrelated apps or windows no longer re-applies gamma and overlay state.
  This reduces unnecessary display churn while preserving immediate changes when an
  application-aware rule begins or ends.

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
