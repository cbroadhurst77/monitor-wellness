# Monitor Wellness — UX Audit Implementation Tracker

Tracks implementation of the findings in `MonitorWellness_UX_Accessibility_Audit.html` (v1.1, 2026-07-31).
Check items off as you land them. Update **Status** and add a commit/PR reference where noted —
this file is meant to be edited in place as work progresses, the same way `GAP_ANALYSIS_2026-07.md`
and `CHANGELOG.md` already are in this repo.

**Status legend:** `Not started` · `In progress` · `Done` · `Blocked`

**2026-07-31 update:** All P0/P1/P2 items and all 12 microcopy rewrites below are now implemented
and the solution **builds cleanly** (`dotnet build`, 0 errors/warnings) in this environment, which
does have a Windows/MSBuild toolchain available (`dotnet 10.0.400-preview`). The build was also
launched live (debug build, existing `settings.json`, tray icon present, no unhandled exceptions
in `debug.log` over several minutes of runtime) — see the QA checklist below for what that did
and didn't cover.

---

## Code done — needs build + QA

- [x] **Settings window regrouped into 4 tabs** (Schedule / Migraine Mode / Monitors & Breaks /
      Profiles & History) instead of one 9-section scroll. File: `SettingsWindow.xaml`. All
      `x:Name`s preserved. See audit §7.3 for the wireframe.
- [x] **Dark theme extended to `TabControl`/`TabItem`** so the new tab strip matches the rest of
      the window in dark mode. File: `Theme/DarkTheme.xaml`.

**QA checklist:**

- [x] Solution builds with no XAML/compile errors (`dotnet build MonitorWellness.csproj -c Debug`
      → Build succeeded, 0 Warning(s), 0 Error(s)).
- [x] App launches, tray icon appears, no unhandled exceptions in `debug.log` across startup and
      several minutes of runtime (confirmed live in this session).
- [ ] Settings window opens from the tray menu and all four tabs are clickable — **not yet
      interactively verified in this session** (GUI click-through wasn't automated to avoid
      driving the real mouse/keyboard on the live desktop). Please click through once:
      **Schedule**, **Migraine Mode**, **Monitors & Breaks**, **Profiles & History**, the new
      status line at the top, Reset/Cancel/Save, and dark mode.
- [ ] Switching tabs while a slider preview is active doesn't leave a stuck tint/dim on screen.
- [ ] Tab strip and controls are reachable and operable by keyboard alone.
- [ ] Live theme switching: with Settings or Onboarding open, flip Windows' dark/light mode and
      confirm the window updates without needing to be reopened (new in this pass — see P1 below).

- [ ] **All of the above pass** → mark this whole section `Done`.

---

## P0 — Must fix

- [x] **Rewrite developer-facing jargon out of user-facing copy.** Removed "gamma ramp" and
      "confirmed directly on this hardware" phrasing and all in-app references to
      `EVALUATION.md`/`IMPLEMENTATION.md`/`TECHNICAL_UX_REVIEW.md` from user-visible text. Kept
      the evidence detail available in the "Why these colors?" expanders (now plain-language,
      no file references), with a one-line plain-language summary promoted outside each expander.
  - Files touched: `OnboardingWindow.xaml`, `SettingsWindow.xaml` (all sections),
    `SettingsWindow.xaml.cs` (`TryParseAll`, `UpdateSliderLabels`), `App.xaml.cs` (balloon text),
    `MigraineRatingWindow.xaml`.
  - Status: **Done** — code comments (dev-facing, not shown to users) still reference the
    internal review docs where useful for future maintainers; only user-visible strings changed.

- [x] **Add a persistent, glanceable status indicator.** Added a status line
      (`CurrentStatusText`) pinned above the tabs in `SettingsWindow.xaml`, reading current mode
      in plain words ("Currently: Day mode." / "Currently: Migraine relief ON — turns off
      automatically at 3:45 PM." / "Currently: Schedule paused until 3:00 PM."). Refreshed on
      load and every 2 seconds while the window stays open (`App.ComputeStatusText`,
      `SettingsWindow`'s `_statusRefreshTimer`), using state that already existed
      (`MigraineModeController.IsActive`/`AutoRevertAtUtc`, `App._pauseUntilUtc`).
  - Files: `SettingsWindow.xaml`/`.xaml.cs`, `App.xaml.cs` (`ComputeStatusText`).
  - Status: **Done**.

- [x] **Raise size/contrast of informational and safety text in Settings.** Helper text raised
      from 13px (~9.75pt) to 14px (~10.5pt); safety-relevant text (privacy note, evidence
      one-liners, Kelvin/hex/bedtime warnings) raised to 15px (~11.25pt) with a darker
      `#FF444444` foreground instead of `DimGray` where contrast mattered most.
  - Files: `SettingsWindow.xaml` (all helper `TextBlock`s).
  - Status: **Done**.

- [x] **Throttle the live slider preview in Settings** to ~10 updates/second. Slider/color
      changes now queue a preview kind (`day`/`night`/`migraine`) and a shared `DispatcherTimer`
      flushes pending previews every 100ms, reading the sliders' live values — so the last value
      before a drag stops always applies, but a fast drag can't write faster than the throttle.
      The timer starts on first change and stops itself once nothing is pending.
  - Files: `SettingsWindow.xaml.cs` — `QueuePreview`/`FlushPendingPreviews`, wired into
    `DaySlider_ValueChanged`, `NightSlider_ValueChanged`, `MigrainePreview_Changed`.
  - Status: **Done**.

- [x] **Sequence startup balloon notifications.** HDR/f.lux/auto-start-drift/hotkey-conflict
      balloons are now queued (`App._pendingStartupBalloons`) instead of shown immediately. On a
      first run, the queue only starts draining (one balloon every 6 seconds) after the
      onboarding window closes; on a normal run, it starts draining right after `OnStartup`
      finishes.
  - Files: `App.xaml.cs` — `OnStartup`, `QueueStartupBalloon`, `StartDrainingStartupBalloons`,
    `ShowNextStartupBalloon`, `RebuildHotkey(isStartup:)`.
  - Status: **Done**.

---

## P1 — Should fix

- [x] **Group Settings into tabs/sections** (Schedule / Migraine Mode / Monitors & Breaks /
      Profiles & History). Build-verified this session (see QA checklist above).

- [x] **Replace blocking `MessageBox` validation errors with inline warning text** for hex-color
      (Migraine overlay, Deep-night overlay) and bedtime fields, matching the existing
      `KelvinSafetyWarning` pattern. Each field gets its own warning `TextBlock`
      (`MigraineColorWarning`, `DeepNightColorWarning`, `BedtimeWarning`), refreshed live on
      `TextChanged`/`UpdateSliderLabels` and re-checked (without a dialog) at Save time.
      Latitude/longitude and per-monitor numeric fields still use `MessageBox` — not in the
      audit's scope for this item.
  - Files: `SettingsWindow.xaml` (new warning `TextBlock`s), `SettingsWindow.xaml.cs` —
    `UpdateHexColorWarning`, `UpdateBedtimeWarning`, `TryParseAll`, `Save_Click`.
  - Status: **Done**.

- [x] **Re-apply dark/light theme live** if Windows' theme changes while a window is open.
      `ThemeDetector.ApplyDarkThemeIfNeeded` is now idempotent (removes any previously-merged
      dark dictionary and resets Background/Foreground before re-deciding), and a new
      `ThemeDetector.EnableLiveThemeUpdates(window)` hooks `SystemEvents.UserPreferenceChanged`,
      unsubscribing on `Closed`.
  - Files: `Core/ThemeDetector.cs`; called from `SettingsWindow`, `OnboardingWindow`,
    `AboutWindow`, `TroubleshootingWindow` constructors.
  - Status: **Done**.

- [x] **Add screen-reader live-region behavior to dynamic warning text.**
      `AutomationProperties.LiveSetting="Assertive"` added to `KelvinSafetyWarning`,
      `MigraineColorWarning`, `DeepNightColorWarning`, and `BedtimeWarning`; the new persistent
      status line uses `LiveSetting="Polite"` (informational, not urgent).
  - Files: `SettingsWindow.xaml`.
  - Status: **Done**.

- [x] **Convert onboarding from one dense wall of text into short sequential steps.** Now four
      steps (what it does → Migraine Mode & hotkey → safety note → location prompt), one idea
      per screen, with Back/Next navigation and a step counter. Buttons on the final step renamed
      to "Set my location now" / "Skip for now (uses London times)". The safety disclaimer now
      gets a proper bordered "note" treatment instead of italic gray text.
  - Files: `OnboardingWindow.xaml`, `OnboardingWindow.xaml.cs` (full rewrite).
  - Status: **Done**.

- [x] **Split "Help / About" into a distinct "About" panel and a "Troubleshooting" panel.**
      New `AboutWindow` (what the app does, safety disclaimer, privacy note) and
      `TroubleshootingWindow` (the four conflict cases the app already detects: fullscreen
      blocking the overlay, f.lux conflict, hotkey conflict, HDR present, plus a pointer to
      Diagnostics/logs) replace the old "Help / About..." item that reopened the full onboarding
      wizard. Onboarding itself (`ShowFirstRunOnboarding`) is now only ever shown once, on first
      run.
  - Files: new `AboutWindow.xaml`/`.xaml.cs`, `TroubleshootingWindow.xaml`/`.xaml.cs`;
    `App.xaml.cs` — tray menu construction, `ShowAboutWindow`, `ShowTroubleshootingWindow`.
  - Status: **Done**.

---

## P2 — Nice to have

- [x] **Rename "Got it, maybe later"** → "Skip for now (uses London times)" on the onboarding
      final step. File: `OnboardingWindow.xaml`.
  - Status: **Done**.

- [x] **Nest diagnostic-only tray items** (Auto-start Diagnostics, Open Logs Folder) under a
      single "Diagnostics" submenu. File: `App.xaml.cs` — tray `ContextMenuStrip` construction.
  - Status: **Done**.

- [x] **Add a short, visible "why we don't flash" note** near the Migraine Mode settings.
  - Status: **Already done pre-audit** — this text already existed in `SettingsWindow.xaml`
    ("We turn Migraine Mode on instantly, and always fade it back off slowly over about 20
    seconds…"); this pass only raised its font size/contrast to match the other safety text.

- [x] **Promote a one-line evidence summary out of the collapsed "Why these colors?" expanders.**
      Added a visible one-liner above each expander (Day/Night: "Backed by circadian-light
      research on evening warming…"; Migraine: "Backed by 2016 migraine-light research…"), with
      the full citation detail still available on expand (now with the `EVALUATION.md` reference
      removed per the P0 jargon item).
  - Files: `SettingsWindow.xaml`.
  - Status: **Done**.

---

## Microcopy rewrites (audit §5)

- [x] #1 — Onboarding intro paragraph
- [x] #2 — Onboarding "Got it, maybe later" button
- [x] #3 — `KelvinSafetyWarning` inline text
- [x] #4 — Save-time Kelvin validation error
- [x] #5 — Migraine Rating window subtitle
- [x] #6 — Clear History confirmation
- [x] #7 — Reset to Defaults confirmation
- [x] #8 — Fullscreen conflict balloon
- [x] #9 — f.lux conflict balloon
- [x] #10 — Crash-loop balloon
- [x] #11 — Monitors table header help text *(already matched the "after" text pre-audit)*
- [x] #12 — Deep Night section helper text

---

## Notes for whoever picks this up

- Everything above is implemented and the solution builds cleanly. What's **not** yet done: a
  full interactive click-through of every control (see the unchecked QA boxes above) — this
  session confirmed the build compiles and the app runs/starts without exceptions, but did not
  drive the actual UI (tray icon clicks, tab switching, slider drags) to avoid taking over the
  live desktop's mouse/keyboard mid-session.
- New files this pass: `AboutWindow.xaml`/`.xaml.cs`, `TroubleshootingWindow.xaml`/`.xaml.cs`.
- Source of truth for full context/reasoning on every item: `MonitorWellness_UX_Accessibility_Audit.html`.
