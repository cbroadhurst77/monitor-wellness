# Monitor Wellness — Independent Re-Audit

A second, from-scratch pass across the same seven dimensions as `TECHNICAL_UX_REVIEW.md`
(feature completeness, UX, technical robustness, personalization/science, integration,
documentation, data/monitoring), performed after that document's findings were implemented, to
check whether the implementation work introduced any new defects and whether any gaps remain.

**Methodology, stated plainly because it matters for how much to trust this document**: the
codebase was read fresh by an independent reviewer with no memory of writing the code (a
separate agent invocation), instructed to form its own judgments from source before checking
any of this project's own prior write-ups, then only afterward cross-check its findings against
those documents' claims. Its three most significant findings were then independently
re-verified by direct measurement (not just re-reading) before being accepted, and all three
have since been fixed and covered by new regression tests. This two-step process — independent
finding, then independent verification of the finding itself — is deliberate: a codebase's own
author (including an AI session with full memory of writing it) is the worst-positioned party to
catch its own mistakes by re-reading, since expectation colors observation.

**Baseline**: `dotnet build` succeeds with 0 warnings/errors. `dotnet test` passes 115/115 (up
from 107 before this pass — 8 new regression tests added as part of fixing the findings below).

---

## Defects found and fixed this pass

### 1. `HdrDetector`'s struct layout was wrong, and its own "verified live" claim couldn't have caught it

**What was wrong**: `DISPLAYCONFIG_PATH_TARGET_INFO` represented the native
`DISPLAYCONFIG_RATIONAL refreshRate` field (two adjacent `UINT32`s, needing only 4-byte
alignment) as a single C# `ulong` (needing 8-byte alignment). Measured directly with
`Marshal.SizeOf`/`Marshal.OffsetOf` before the fix: the struct computed as 56 bytes instead of
the native 48, and — because raising one field's alignment requirement raises the whole
containing struct's alignment — `DISPLAYCONFIG_PATH_INFO.targetInfo` (the exact field the code
reads to get each display's adapter/target ID) was offset at byte 24 instead of the correct 20.

**Why the earlier verification missed it**: `IMPLEMENTATION.md`'s Week 12 entry describes
testing this live and observing "no crash, all three displays correctly report advanced color
as unsupported/disabled" as confirmation. That observation is consistent with *both* a correct
read *and* a silently-corrupted one, since every non-HDR display should report "disabled"
either way, and a garbage adapter/target ID would just make `DisplayConfigGetDeviceInfo` fail
and get skipped — no exception, no visible difference. "The app didn't crash and returned a
plausible answer" is not sufficient verification for raw struct marshaling; only checking the
actual computed layout against the native one is.

**Fix**: replaced the `ulong` with two explicit `uint` fields
(`refreshRateNumerator`/`refreshRateDenominator`), matching the native layout exactly. Verified
by direct measurement this time, not inference: `Marshal.SizeOf`/`OffsetOf` on the corrected
struct produce exactly 48/72/20/44 (all four values that were wrong before are now confirmed
right), and eight new tests in `tests/HdrDetectorStructLayoutTests.cs` assert this permanently,
using reflection to check the private nested structs' real computed layout — so if this class of
mistake is ever reintroduced (in this struct or the others `HdrDetector` defines), the test
suite catches it immediately rather than requiring another independent audit to notice.

### 2. Two Settings-window checkboxes bypassed Cancel entirely

**What was wrong**: `HistoryTrackingCheckBox_Changed` and `MatchAmbientLightCheckBox_Changed`
(both added in the prior implementation pass) mutated the live `AppSettings` object and called
`SettingsStore.Save()` immediately on toggle — not deferred to the Save button like every other
control in the window. The window's own UI text states plainly: *"nothing is saved until you
click Save, and Cancel leaves everything untouched"* — for these two specific controls, that
was false. Toggling either checkbox and then clicking Cancel left the change already applied
and already on disk.

**Fix**: both settings are now committed only inside `TryParseAll`'s existing commit block,
alongside every other setting, exactly like the rest of the window. `HistoryTrackingCheckBox`
keeps a `Changed` handler purely to refresh the live summary preview text (which reads the
checkbox's own UI state directly, not the settings object, so no mutation is needed for that).
`MatchAmbientLightCheckBox` needed no live-reaction handler at all, so its event wiring was
removed rather than left as an empty stub.

This is a regression I introduced in the prior implementation pass, not a pre-existing issue —
worth stating plainly rather than glossing over.

### 3. A pre-existing bug, not introduced by recent work: excluding a monitor during live preview didn't stop its color from changing

**What was wrong**: `SettingsWindow.ApplySchedulePreview` checked `_colorExcludeBoxes` before
applying a color-temperature preview to a monitor, but never checked `_excludeBoxes` — so
checking "Exclude" on a monitor and then dragging the Day/Night Kelvin slider still changed
that monitor's gamma ramp during the live preview. Separately, `App.RunScheduleTick` (the real,
non-preview path) correctly skips excluded monitors for color, but does so via a bare
`continue` with no `ResetToIdentity()` call — meaning a monitor whose gamma was altered by the
preview bug, then excluded, would stay stuck at that altered color for the rest of the app
session (until restart), contradicting the "Exclude" checkbox's own tooltip: *"Skip this
monitor entirely — no color or brightness adjustment."*

**Confirmed pre-existing**: checked via `git show` against the very first implementation
commit — this exact code, with this exact gap, was present before any of the review-driven work
in this session began. It's a genuine defect, just not a new one.

**Fix**: `ApplySchedulePreview` now checks `_excludeBoxes` first and skips the monitor entirely
if checked, matching `RunScheduleTick`'s real-path semantics exactly (skip, don't touch — as
opposed to Color-accurate exclude, which actively resets to native). No new automated test was
added for this one: the fix is UI-callback logic tightly coupled to live `GammaRampController`
calls, the same category of Win32/WPF-dependent code this project has never had automated
coverage for (per `EVALUATION.md`'s own long-standing, explicitly acknowledged gap).

---

## 1. Feature completeness

No new gaps found beyond what `TECHNICAL_UX_REVIEW.md` already identified and the prior pass
closed out. Confirmed present and working as designed: sunrise/sunset scheduling with deep-night
and bedtime phases, Migraine Mode (instant activate, graduated fade, mild/full presets,
auto-revert, hotkey/tray/single-click activation), per-monitor overrides (exclude, color-exclude,
dim multiplier, Kelvin offset), opt-in ambient-light adjustment, opt-in local usage history,
fullscreen-heuristic and f.lux conflict warnings, HDR warning (now actually trustworthy — see
defect #1), schedule pause, saved profiles, portable auto-start.

One item worth restating plainly rather than letting it fade: **break-reminder functionality
(e.g., a 20-20-20-style nudge) is still entirely absent**, and arguably has a stronger evidence
base than several features already built (the app's own `EVALUATION.md` cites the American
Academy of Ophthalmology's position that blue-light filtering doesn't demonstrably reduce eye
strain — a periodic break reminder is a more directly-evidenced ergonomics intervention that
this app has all the scheduling/timer infrastructure to add cheaply, and doesn't have yet).

## 2. User experience

Re-confirmed independently: tray menu mnemonics are complete and non-colliding across all ~14
top-level items; the tray icon's single left-click toggle is correctly scoped to the left mouse
button only (via `MouseClick`, not `Click`), so it doesn't double-fire with the right-click
context menu; Settings window sliders are correctly paired with editable numeric fields that
commit on blur/Enter and revert on invalid input; dark mode applies consistently;
`AutomationProperties.Name` is present on sliders, per-monitor row controls, and numeric inputs.
The onboarding window's text was re-read specifically looking for overclaiming and found none —
the earlier "instant relief" issue is genuinely fixed, with an explicit medical disclaimer
present.

The two concrete defects found this pass (#2 and #3 above) both live in this dimension — both
now fixed. No further UX defects were found on this pass beyond a minor, non-bug observation:
`LoadPreferencesFrom` (used by Reset and Load Profile) fires several live-preview recalculations
in quick succession as it sets each bound control in turn, which is slightly wasteful but
converges to the correct final state — not worth changing given it has no observable effect on
correctness.

## 3. Technical robustness

This is where the pass earned its keep. All raw Win32 struct definitions in the codebase were
checked field-by-field against their documented native layouts: `GammaRampController.RAMP`,
`MonitorEnumerator.DISPLAY_DEVICE`, and `FullscreenDetector.RECT` are all correct. `HdrDetector`'s
structs were not, and now are (defect #1) — the new `HdrDetectorStructLayoutTests.cs` closes
this specific class of risk with a permanent, automated check, which is arguably the single most
valuable output of this pass: this project has several other raw-struct P/Invoke blocks, and now
has a demonstrated, reusable pattern (`Marshal.SizeOf`/`OffsetOf` against hand-derived native
values) for verifying any of them without needing another external audit.

One plausible-but-unconfirmed risk surfaced by the independent reviewer, not tested here:
`SystemEvents.PowerModeChanged`/`DisplaySettingsChanged` handlers in `App.xaml.cs`,
`GammaControllerManager`, and `OverlayController` touch WPF `Window` objects with no explicit
`Dispatcher` marshaling. `SystemEvents` can, depending on runtime specifics, raise these events
from a thread other than the one that subscribed — if that happens here, a real sleep/resume or
monitor hot-plug could throw a WPF cross-thread `InvalidOperationException`. This is consistent
with (not contradicting) `IMPLEMENTATION.md`'s own long-standing admission that this path "was
only tested via `SystemEvents` firing correctly, not an actual physical sleep/resume cycle or
hot-plug." Stated here as a named, concrete, testable hypothesis rather than left as a vague
"needs more testing" — worth an explicit sleep/resume test on real hardware before treating this
as settled, since it was not exercised in this pass either (this session's environment doesn't
allow triggering a real sleep/resume cycle to observe firsthand).

A narrower gap also worth naming: `DebugLog.Write` only catches `IOException` around its
rotation logic, while `FileInfo.Length`/opening a `FileStream` can also throw
`UnauthorizedAccessException` under unusual permission conditions — ironic for a utility whose
whole purpose is "never be the thing that crashes the app." Low-severity (narrow trigger
condition), not fixed in this pass, worth a one-line broadening of the catch clause next time
this file is touched.

## 4. Personalization and evidence-based grounding

No overclaiming found anywhere in code comments, XAML text, or README on this pass — the
Settings window's "Why these colors?" summaries correctly distinguish mechanism evidence from
direct-intervention evidence and accurately cite the 2023 Cochrane review's negative finding on
blue-light filtering for eye strain specifically. `AppSettings.MigraineOverlayColorHex`'s doc
comment accurately represents the Noseda & Burstein (2016) finding. This dimension is in good
shape and unchanged from the prior pass's assessment.

## 5. Integration and compatibility

f.lux detection is a real, correctly-scoped, tested process-name check. Windows Night Light
detection remains deliberately unimplemented, for a reason re-confirmed as sound on this pass:
the registry format it would need is undocumented and was unverifiable on this dev machine (the
key doesn't exist here). HDR detection is now actually trustworthy (defect #1 fixed) rather than
a plausible-looking no-op. Remote Desktop/virtual displays and laptop hybrid-GPU switching
remain untested — acknowledged by the project's own docs, and still untestable from this
environment; this pass didn't move that needle and isn't claiming to.

## 6. Documentation and onboarding

In-app documentation (onboarding, Settings tooltips and help text) was re-read side-by-side with
the actual code behavior and found accurate, with one exception that's now fixed: the Settings
window's own stated Save/Cancel contract (defect #2). `README.md`, `EVALUATION.md`,
`IMPLEMENTATION.md`, `CHANGELOG.md`, and `TECHNICAL_UX_REVIEW.md` were cross-checked against
independent source reading and found accurate on every claim except the one specifically
addressed in defect #1's writeup above (the HDR verification claim) — everything else (the
science-grading table, "installer never verified end-to-end," "one machine tested," test counts,
changelog contents) matched direct observation.

## 7. Data and monitoring

Privacy story re-confirmed from source: `GeocodingService.SearchAsync` (gated behind an explicit
button click) remains the only network call anywhere in `src/`. `SettingsStore`, `ProfileStore`,
`HistoryStore`, and `DebugLog` all write only to `%AppData%\MonitorWellness\`, all inspectable
plain text/JSON/JSONL. History tracking and ambient-light matching are opt-in and default off —
modulo defect #2, which meant the Settings window's own "nothing takes effect until Save" framing
wasn't quite true for these two opt-in toggles specifically; now fixed, so "opt-in" means what
the UI says it means.

---

## Summary

Three real defects found, all now fixed and covered where automated coverage is feasible (struct
layout: yes, with 8 new tests; the two Settings-window logic bugs: no, consistent with this
project's long-standing, explicit non-coverage of Win32/WPF-dependent code). One was a
regression from the prior implementation pass (the Cancel-bypass), one was newly-introduced
paired with a verification gap in the same pass (the HDR struct layout — the code was new, but
so was the flawed verification that missed it), and one predates this session entirely (the
Exclude/preview bug). Test count: 107 → 115. No new feature gaps were identified beyond the one
explicitly restated above (break reminders) and the two already-acknowledged, still-unverifiable
hardware surfaces (sleep/resume thread-safety, RDP/laptop GPU switching) that this environment
cannot test.
