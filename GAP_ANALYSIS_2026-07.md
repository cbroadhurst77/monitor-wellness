# Monitor Wellness — Gap Analysis (July 2026 pass)

A fourth read of this project, after `TECHNICAL_UX_REVIEW.md` (pass 1, closed out),
`INDEPENDENT_REAUDIT.md` (pass 2, three defects found and fixed), and the `Unreleased`
section of `CHANGELOG.md` (which shipped a large batch of work — break reminders, migraine
rating prompts, A/B schedule presets, settings export/import, High Contrast support — after
pass 2 was written, so none of it has had independent eyes on it until now).

**Scope of this document**: rather than re-litigate the first two passes' closed items, this
pass (a) verifies the newest, not-yet-audited features against source, (b) looks specifically
for the kind of thing those features' own authors would be least likely to notice (scope
boundaries, silent feature gaps, things that look complete but aren't quite), and (c)
restates — briefly, not duplicated in full — the items still open after three passes, so this
document is useful on its own without requiring the other three open at the same time.

Every item states what's missing/suboptimal, why it matters, and what a fix would look like,
per the review brief. Ordered by severity within each section.

---

## 1. Feature completeness

### 1.1 The deep-night/bedtime stage is the one schedule feature a user can't see, tune, save, or carry across profiles

**Missing**: `AppSettings.DeepNightBrightness` (default 0.7) and `DeepNightOverlayColorHex`
(default `#190C04`, a warm near-black brown) drive real, live behavior — `ScheduleCurve`
blends toward them as solar elevation drops past nautical twilight, and `App.ComputeScheduleTarget`
applies the blended result every 30-second tick. But neither field is referenced anywhere in
`SettingsWindow.xaml.cs`: not in `LoadPreferencesFrom` (which populates every other slider from
`AppSettings`), not in `ApplyImportedSettings`'s explicit field list, and therefore not in
Export/Import Settings, not in saved Profiles (`ProfileStore` round-trips full `AppSettings`
JSON, but `SettingsWindow` only ever reads the same "preferences" subset back out of it via
`LoadPreferencesFrom`), and not in the Reset flow. It is the only remaining hardcoded pair of
numbers in a settings surface where every comparable value (day/night Kelvin, brightness,
migraine tint/opacity/contrast) is user-adjustable, previewable, and persisted per-profile.

**Why it matters**: This is a real, nightly-active feature — not a deferred roadmap item — so
its invisibility is a genuine personalization gap, not a documentation one. A user who finds
the deep-night warmth too aggressive (or not warm enough) currently has no in-app way to change
it, and — more subtly — a saved "evening" profile silently omits it: switching profiles changes
Kelvin/brightness/migraine settings but leaves whatever deep-night behavior is currently active
untouched, which isn't obviously how "profiles" would be expected to behave once someone
discovers this stage exists (e.g., from the `CHANGELOG.md` release notes, which do mention it).

**What good looks like**: Add a slider (brightness) and a color picker or hex field
(`DeepNightOverlayColorHex`) to the Settings window, in `LoadPreferencesFrom`,
`ApplyImportedSettings`, and the profile snapshot path — the same four places every other
preference already goes through. If the deliberate choice is to keep this one global and
non-personalizable (a defensible call — it's a subtle blend, not a headline feature), that
should be a stated decision in `AppSettings.cs`'s doc comment, not a silent omission a future
contributor (or this document, a third time) has to rediscover by grepping.

### 1.2 The break reminder has no fullscreen/presentation suppression, despite the exact heuristic it needs already existing in the codebase

**Missing**: `RebuildBreakReminderTimer` (`App.xaml.cs`) suppresses the reminder balloon only
while `_migraine?.IsActive == true`. It has no awareness of `FullscreenDetector.IsForegroundWindowLikelyFullscreen`
— the same heuristic already wired into `MigraineModeController` to warn about the overlay not
rendering over exclusive-fullscreen apps.

**Why it matters**: A "look away for 20 seconds" balloon popping up mid-movie, mid-game, or
mid-screen-share is a genuine, plausible annoyance for a feature that's opt-in specifically to
avoid being intrusive — and unlike the migraine-mode fullscreen case (a rendering limitation
with no easy fix), this one has a one-line fix sitting right next to it in the same file.

**What good looks like**: `if (_migraine?.IsActive != true && !FullscreenDetector.IsForegroundWindowLikelyFullscreen()) { ...show balloon... }`
— reuses existing, already-tested infrastructure; no new detection logic needed.

### 1.3 No raw history export — only an aggregate summary, which caps how much insight a user can get from their own data

**Missing**: `HistoryStore` persists a full local JSONL event log
(`%AppData%\MonitorWellness\history.jsonl`), but the only UI surface for it is
`RefreshHistorySummary`'s aggregate text (`HistorySummaryText`: total/7-day/30-day activation
counts, mild/full split, average duration, average rating). There's a Clear History button but
no Export/Copy/"Open history file" action.

**Why it matters**: The review brief specifically asks whether the tool "provides insights to
help users understand what helps them specifically." The aggregate view answers "how often and
how well, on average" — it can't answer "did activations cluster on days I skipped the evening
schedule" or "does my rating trend up since I turned on ambient-light matching," the kind of
question someone motivated enough to keep a personal migraine diary (a real, common practice)
would want to cross-reference against. The data already exists locally; only the retrieval path
is missing.

**What good looks like**: An "Export History (CSV)..." button next to "Clear History," writing
the same `HistoryEvent` records `HistoryStore.Load()` already returns to a flat CSV — cheap to
build (a few lines, same `SaveFileDialog` pattern `ExportSettingsButton_Click` already uses),
and it turns a private local log into something a user can actually open in a spreadsheet
alongside whatever else they track.

### 1.4 Still open after three review passes — restated briefly, not re-argued

Unchanged since `EVALUATION.md`/`IMPLEMENTATION.md`'s own deferred-items list, confirmed still
true from source/installer script this pass: no DDC/CI hardware brightness (a deliberate,
well-reasoned deferral — see README's PWM-flicker argument for why overlay-only may be the
*better* default, not just the current one), no auto-update mechanism, no code signing
certificate, no per-monitor migraine contrast control. None of these are newly discovered;
listed here only so this document is a complete picture without requiring the other three open
alongside it.

---

## 2. User experience

### 2.1 Profiles and Export/Import silently share a narrower scope than "settings" implies

**Missing**: Both "Load Profile" and "Import Settings" route through the same
`LoadPreferencesFrom` subset (Day/Night Kelvin+brightness, migraine tint/opacity/contrast/
auto-revert/hotkey, bedtime). Neither carries `BreakReminderEnabled`/`Interval`,
`MatchAmbientLight`, `HistoryTrackingEnabled`, `PromptForMigraineRating`, or per-monitor
overrides (Import does copy per-monitor overrides explicitly; Profiles do not, since
`ProfileStore` is only ever read back through `LoadPreferencesFrom`).

**Why it matters**: Nothing in the Settings window's UI states this scope boundary — the
"Save as Profile" / "Load Profile" controls sit in the same window as everything else, with no
visual separation suggesting some settings won't travel with the profile. A user building a
"daytime work" vs. "evening" profile pair, expecting each to also carry its own break-reminder
or ambient-light preference, would get a silent partial result with no error or explanation.

**What good looks like**: Either (a) a one-line caption under the Profile controls — "Profiles
save color, brightness, and migraine settings only" — so the boundary is stated rather than
discovered, or (b) if the intent has genuinely grown past the original "Day/Night/migraine"
scope described in `ProfileStore`'s doc comment (plausible, given how much has been added since
that comment was written), widen `LoadPreferencesFrom`'s subset to match what a "profile" now
reasonably implies.

### 2.2 Everything else re-checked this pass matches its own documentation

Re-verified directly against source, not just re-read from prior docs: tray mnemonics remain
non-colliding; the single-click migraine toggle is still correctly left-button-only; sliders
still pair with editable numeric fields; the onboarding disclaimer and "Why these colors?"
section are present and accurately worded; High Contrast mode is correctly detected and left
alone (`ThemeDetector.ApplyDarkThemeIfNeeded` — see §5.1 below for the one nuance worth adding);
the migraine rating prompt (`MigraineRatingWindow`) is non-modal, auto-dismissing, and can't
double-fire between a button click and its own timeout. No new UX defects found beyond §2.1.

---

## 3. Technical robustness

### 3.1 `MigraineModeController` — the single most safety-relevant class in the app — has zero automated test coverage, and not because its logic is untestable

**Missing**: Every other pure-logic class in this codebase has a matching test file
(`ScheduleCurveTests`, `ColorTemperatureTests`, `SolarCalculatorTests`, `AmbientLightAdapterTests`,
`CrashLoopDetectorTests`, `HistorySummarizerTests`, `SchedulePauseTests`). `MigraineModeController`
— which owns the fade-curve math (`Lerp`/`LerpColor` toward a live schedule target), the mild-
intensity multiplier, and auto-revert timer arming — has none, despite that math being just as
pure as `ScheduleCurve`'s.

**Why it matters**: This isn't an inherent Win32/WPF-coverage gap like the rest of this
project's documented, accepted testing boundary (gamma ramp calls, overlay windows) — the
reason it's untestable today is that the constructor takes concrete `GammaControllerManager`
and `OverlayController` dependencies with no seam to substitute a fake, unlike
`_computeScheduleTarget`, which is already an injectable `Func<>` for exactly this reason. The
class governing the app's emergency migraine-relief feature — instant activation, fade timing,
mild-vs-full intensity — is the one place a timing or interpolation regression would matter
most, and it's currently caught by nothing but manual testing.

**What good looks like**: Extract a small interface (e.g. `IColorTemperatureTarget`/
`IOverlayTarget`, or even simpler, two `Action<...>` delegates matching the shape already used
for `_computeScheduleTarget`) for the two "push a value to hardware" calls
(`ApplyColorTemperatureWithContrast`, `Apply`). No behavior change in production — `App.xaml.cs`
passes the same real objects — but it opens the door to a `MigraineModeControllerTests.cs` that
asserts fade timing, mild-intensity scaling, and auto-revert arming with fakes, the same pattern
`_computeScheduleTarget` already proves works well in this codebase.

### 3.2 `DebugLog`'s own exception handling is narrower than its stated purpose, flagged once already and still unfixed

**Missing**: `DebugLog.Write`'s rotation path (`RotateIfTooLarge`) touches `FileInfo.Length` and
opens a `FileStream` before `Write`'s single `catch (IOException)` takes effect — both can throw
`UnauthorizedAccessException` under permission conditions IOException doesn't cover. Confirmed
unchanged from `INDEPENDENT_REAUDIT.md`'s finding, which explicitly deferred it ("worth a
one-line broadening of the catch clause next time this file is touched") — the file has not
been touched since, so this is now a known gap surviving a second independent pass.

**Why it matters**: Small blast radius, but pointed: this is the one class whose entire purpose
is "never be the reason the app crashes," and it's the one class in the codebase with a
narrower catch clause than its own job requires. Two consecutive audits noting the same
one-line fix without it landing is itself worth flagging as a process gap, not just a code one.

**What good looks like**: `catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)` —
matches the pattern already used consistently elsewhere in this codebase (`ProfileStore.Load`,
`HistoryStore.Load`). Genuinely a one-line change.

### 3.3 Unchanged, unverifiable-in-this-environment risks — restated for completeness

`INDEPENDENT_REAUDIT.md`'s named hypothesis (`SystemEvents.PowerModeChanged`/
`DisplaySettingsChanged` potentially firing off the UI thread, guarded by a `Dispatcher.CheckAccess()`
check that's never actually been exercised by a real sleep/resume or hot-plug event) remains
open and remains untestable from a non-interactive environment. Remote Desktop/virtual displays
and laptop hybrid-GPU switching remain untested, as they have across all three passes, for the
same reason (no access to that hardware). Not re-derived here — restated only so a reader of
this document alone has the full risk picture.

---

## 4. Personalization and evidence-based grounding

`EVALUATION.md`'s science-grading table and the in-app "Why these colors?" summaries were
spot-checked again this pass and remain accurate — no overclaiming found anywhere in current
source, XAML, or markdown. One addition worth naming:

### 4.1 A grayscale/desaturation option for migraine mode isn't present, and belongs at a clearly lower evidence tier than the current green tint if ever added

**Observation, not a defect**: Some migraine-with-aura sufferers report that *any* color overlay
feels wrong during an active aura, preferring full desaturation instead — this is a common
anecdotal preference in migraine communities, not a claim with the same research backing as the
green tint (Noseda & Burstein and its replications, per `EVALUATION.md`). Given that document's
own point that individual sensitivity varies enormously, and given the app already treats "let
the user pick what feels better" as a design principle (the A/B preset buttons, the mild/full
intensity split), a third option — "Grayscale" alongside the default green tint — would extend
that same principle to the one population subgroup the current single-tint design doesn't serve.

**What good looks like, if pursued**: A `MigraineOverlayMode` enum (Tint / Grayscale) rather than
only a configurable hex color, with the in-app evidence summary explicit that grayscale is
offered as a comfort option based on user-reported preference, not the same replicated-research
tier as the green tint — consistent with this project's own standard of not overstating evidence
behind any given default.

---

## 5. Integration and compatibility

### 5.1 High Contrast detection is correct but static — no re-check if the user changes it mid-session

**Missing**: `ThemeDetector.ApplyDarkThemeIfNeeded` checks `SystemParameters.HighContrast` once,
at window construction. The class's own doc comment already states dark/light theme has this
same limitation ("no live re-theming if the user flips the Windows setting while a window is
already open") — High Contrast inherits the identical limitation, just not stated alongside it.

**Why it matters**: Minor — Settings/onboarding windows are typically short-lived and reopened
often — but High Contrast is specifically an accessibility affordance, and the population
toggling it mid-session (testing whether it helps, or an assistive-tech setup routine) is more
likely than average to actually exercise this edge case.

**What good looks like**: No code change necessarily required — but the doc comment's existing
"no live re-theming" caveat should explicitly say it also covers High Contrast, so a future
contributor evaluating an accessibility bug report doesn't have to rediscover this by testing.

### 5.2 Nominatim rate-limit/usage-policy compliance beyond the User-Agent header isn't addressed

**Missing**: `GeocodingService` correctly sets a descriptive `User-Agent` per Nominatim's usage
policy, but the policy also asks for no more than ~1 request/second and no batch/automated use.
Nothing prevents rapid repeated searches (e.g., a user typing and re-triggering search quickly)
from exceeding that informally.

**Why it matters**: Low real-world risk given this is a manual, one-off, user-triggered search
box, not a batch process — but worth a minimal debounce (e.g., disable the search button for
~1 second after each request) so this app's single network call stays unambiguously
policy-compliant rather than "probably fine in practice."

**What good looks like**: Disable the search button/Enter binding for 1 second after a request
completes — a few lines, closes the gap between "compliant in practice" and "compliant by
design."

---

## 6. Documentation and onboarding

No new gaps found this pass. The in-app documentation added since `INDEPENDENT_REAUDIT.md`
(the "Why these colors?" section, the privacy note, the medical disclaimer, "Help/About")
was cross-read against actual behavior and found accurate. One structural observation:

### 6.1 Three review documents plus a changelog is now a lot of places for a future contributor to reconcile

**Observation, not a defect**: `TECHNICAL_UX_REVIEW.md`, `INDEPENDENT_REAUDIT.md`,
`EVALUATION.md`, `IMPLEMENTATION.md`, and now this document all carry overlapping context, each
with its own "status" framing (closed-out, re-audited, graded, dev-logged). This is a genuine
strength for auditability — the project's discipline of writing findings down plainly is
unusual and worth preserving — but a fifth pass would benefit from a short index (even a single
`REVIEWS.md` with one line per document: what it covers, its date, its status) so a newcomer
doesn't have to open all of them to learn which one is current.

---

## 7. Data and monitoring

Privacy story re-confirmed unchanged and still accurate: `GeocodingService.SearchAsync` remains
the only network call anywhere in `src/`, gated behind an explicit search action; `SettingsStore`,
`ProfileStore`, `HistoryStore`, and `DebugLog` all write only to `%AppData%\MonitorWellness\`.
The one gap in this dimension is §1.3 above (no raw history export) — restated here for
completeness under this heading, since it's squarely a data/monitoring finding: the app collects
good data locally and currently under-serves the user's ability to get it back out.

---

## Summary — highest-leverage items from this pass

1. **Reuse `FullscreenDetector` to suppress the break reminder during fullscreen apps** (§1.2)
   — smallest possible change, reuses existing tested code, fixes a real annoyance.
2. **Broaden `DebugLog.Write`'s catch clause** (§3.2) — one line, flagged twice now without
   being fixed.
3. **Add a CSV export for the local history log** (§1.3) — small, directly answers the review
   brief's "insights to help users understand what helps them" question better than the
   aggregate view alone can.
4. **Expose `DeepNightBrightness`/`DeepNightOverlayColorHex` in Settings, or explicitly document
   why they're intentionally global** (§1.1) — closes the one remaining unpersonalizable,
   un-exportable, per-profile-invisible corner of an otherwise fully-adjustable schedule.
5. **State the Profile/Import scope boundary explicitly in the UI** (§2.1) — a one-line caption
   prevents a silent, surprising gap between what a user expects "save as profile" to capture
   and what it actually does.
6. **Give `MigraineModeController` a seam for testing** (§3.1) — the highest-value testing
   investment left in the codebase, given this is the one class whose correctness matters most
   under stress (an active migraine) and the one core-logic class with no coverage at all.
7. **Debounce the geocoding search action** (§5.2) — minor, but closes the gap between
   "compliant in practice" and "compliant by design" for this app's only network call.

None of the items above were found in, or contradict, `TECHNICAL_UX_REVIEW.md`,
`INDEPENDENT_REAUDIT.md`, or `EVALUATION.md` — this pass is additive. The project's overall
trajectory across three review cycles remains what `INDEPENDENT_REAUDIT.md` already
concluded: real defects get found and fixed because of a genuine test-on-hardware and
verify-from-source discipline, not despite it, and that pattern held up again this pass — every
new item above was confirmed by reading the actual current code, not inferred from what the
prior documents said should be true.
