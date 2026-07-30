# Monitor Wellness — Technical & UX Gap Analysis

An independent pass across feature completeness, UX, technical robustness, personalization/
science, integration, documentation, and data/monitoring — reviewed directly against the
source (`src/MonitorWellness/`, `Core/*.cs`, XAML, tests, installer script), not just the
project's own prior write-ups. **EVALUATION.md** and **IMPLEMENTATION.md** are excellent and
largely correct about what they cover (installer never verified end-to-end, one hardware
config tested, no accessibility pass, honest science grading) — this document doesn't repeat
those findings in depth, it cross-references them and focuses on what a fresh read of the
actual code surfaces beyond them.

Each item states what's missing/suboptimal, why it matters, and what a fix would look like.
Ordered roughly by severity within each section.

---

## Status: closed out (see IMPLEMENTATION.md Weeks 11-12)

Every item below has been implemented, or — where implementation wasn't the right call —
deliberately not implemented with the reasoning stated plainly, matching this project's own
"verify against reality, don't assume" discipline rather than mechanically doing exactly what
this document originally proposed:

- **Implemented and verified live**: §1.1 (ambient-light-adaptive brightness), §1.3
  (fullscreen-exclusive-app heuristic), §1.5/§7.1 (local opt-in usage history), §1.4/§5.1 (f.lux
  conflict detection), §2.1-§2.6 (all UX items), §3.1 (single-instance guard), §3.2
  (crash-loop-aware exception handling), §4.1 (in-app evidence summary), §4.3 (A/B calibration
  preview), §5.3 (HDR detection), §6.2 (CHANGELOG.md), §6.3 (privacy statement), §7.2 (Open Logs
  Folder).
- **Documented rather than coded** (the existing behavior was already correct, or the code
  itself needed no change): §1.2 (overlay dimming's PWM-flicker advantage), §3.3 (gamma ramp
  ceiling), §4.2 (seizure-safety reasoning), §5.2 (GPU vendor conflict note).
- **Deliberately not implemented, with reasoning stated**: automated Windows Night Light
  detection (§1.4/§5.1) — the registry format it would need is undocumented and unverifiable on
  this dev machine (the key doesn't exist here), so a confident answer from it would be an
  unverified claim, not a real signal. f.lux detection shipped instead, since it's reliable.
- **The one item that remains genuinely open**: §3.4 (Remote Desktop/virtual displays, laptop
  hybrid-GPU switching) — this needs different hardware than this project has ever had access
  to; it's an acknowledged gap, not a skipped one.

Full details, what was verified live vs. by inspection, and why each documentation-only call was
made: see `IMPLEMENTATION.md`'s Week 11 and Week 12 sections.

---

## 1. Feature completeness

### 1.1 No ambient-light-adaptive brightness — the single best-evidenced feature that's missing

**Missing**: Brightness only ever follows a fixed day/night/deep-night schedule keyed to solar
elevation or clock time. There's no reading of actual ambient light in the room.

**Why it matters**: The one piece of ergonomics guidance that survives EVALUATION.md's own
scrutiny (§2, "Daytime dimming") is *matching screen brightness to ambient light* — a
long-standing photography/display ergonomics principle, not the (unsupported) "reduces eye
strain" framing. A fixed schedule is a poor proxy for this: a room with curtains closed at 2pm
and a room flooded with sun at 7am get treated identically. Windows exposes an Ambient Light
Sensor API on hardware that has one (`Windows.Devices.Sensors.LightSensor`), and even without
a sensor, a webcam-brightness heuristic is a well-known approximation used by other tools.

**What good looks like**: An opt-in "match ambient light" mode that reads `LightSensor` where
available and adjusts the day-phase brightness target continuously, falling back to the
existing fixed schedule where no sensor exists. This is the one addition that would move a
comfort feature into "evidence-aligned" territory, per the app's own honest science review.

### 1.2 No DDC/CI hardware brightness — but the roadmap item may reintroduce a real hazard

**Missing**: `IMPLEMENTATION.md` already lists DDC/CI (`Dxva2.dll`/`SetMonitorBrightness`) as
deferred to v1.1+. Fair — but there's a consideration missing from that roadmap note.

**Why it matters**: This app currently dims exclusively via a semi-transparent black overlay
window (`OverlayWindow`/`OverlayController`), never by touching the monitor's actual backlight.
That's not just a workaround for gamma ramp's factor-of-2 limit — it's arguably *better* for
this app's specific audience. Most monitor backlights (and virtually all laptop panels) dim via
PWM (pulse-width modulation): the backlight isn't actually dimmer, it's strobing on/off faster
than the eye resolves, and PWM flicker is a well-documented migraine/eye-strain trigger in its
own right, especially at low brightness where PWM frequency is often lowest. An overlay veil
has zero flicker. **This app's current architecture is accidentally already better than native
hardware dimming for its own target population, and nothing documents this.**

**What good looks like**: (a) Say this explicitly somewhere the user will see it — it's a real,
defensible differentiator, unlike some of the softened claims EVALUATION.md flagged. (b) If
DDC/CI dimming is added in v1.1, gate it behind an explicit warning that it may reintroduce
backlight PWM flicker on panels where the monitor's own dimming is PWM-based, and let the
overlay-only mode remain the default/recommended path for anyone who came to this app because
of light sensitivity specifically.

### 1.3 No fullscreen-exclusive-app handling — migraine mode's overlay can silently fail exactly when it's needed

**Missing**: `OverlayWindow` is a topmost (`HWND_TOPMOST`), click-through WPF window
(`Core/OverlayWindow.xaml.cs:36-49`). Standard Windows behavior: a true exclusive-fullscreen
surface (older games/media players using D3D exclusive fullscreen, not borderless-windowed)
bypasses the desktop compositor and topmost windows entirely — nothing, including this overlay,
draws over it.

**Why it matters**: This is the most safety-relevant gap in the whole app. Migraine mode exists
for the exact moment someone needs relief *right now*. If that moment happens to be during a
movie, a game, or any exclusive-fullscreen app, the overlay tint/dim silently doesn't render —
gamma ramp still applies (a much weaker effect) but the primary mechanism (overlay tint +
opacity) is invisible, with **no error, no warning, nothing in the debug log distinguishing
this from working correctly**. A user mid-aura pressing the hotkey and seeing no change would
reasonably conclude the app is broken.

**What good looks like**: At minimum, detect likely exclusive-fullscreen state (e.g., a
foreground window covering the full monitor bounds with no border) and surface a tray balloon
("Migraine Mode applied color/contrast, but a fullscreen app may be blocking the dim overlay —
Alt+Tab out or windowed mode will restore it") rather than failing invisibly. Longer-term,
investigate whether a lower-level compositor hook or `SetWindowDisplayAffinity`-style approach
can render over exclusive fullscreen; if not possible, this limitation belongs in the README
next to the other honest caveats.

### 1.4 No conflict detection with other gamma-ramp consumers

**Missing**: `SetDeviceGammaRamp` is global, per-device, last-write-wins state. Nothing in
`GammaRampController`/`GammaControllerManager` checks whether something else — Windows Night
Light, a still-installed f.lux (this app's own predecessor), GPU vendor color utilities
(NVIDIA/AMD "Night Mode" or custom gamma presets), or an ICC calibration loader — is also
writing to the same ramp.

**Why it matters**: The exact population likely to install Monitor Wellness (people already
managing eye strain/migraines) is disproportionately likely to already have Night Light or
f.lux configured. Two tools independently ticking every 30 seconds (or on their own schedule)
against the same gamma ramp will visibly fight — flickering or "randomly" reverting color temp
— and nothing in this app would explain why, since from its own perspective every
`ApplyColorTemperature` call is succeeding.

**What good looks like**: A one-time startup check (Windows Night Light: readable from
`HKCU\...\CloudStore\...\Windows.Data.Bluelight` or simply advise disabling it in onboarding;
f.lux: check for its known process name) with a plain-language warning: "Windows Night Light
also appears to be on — having two tools adjust your screen color at once can cause flickering.
Consider turning one off." Cheap to build, directly prevents a confusing first-run experience.

### 1.5 No personal, local usage diary — the app can't help a user learn what works for them

**Missing**: Zero usage tracking of any kind (by design — see §7). There's no optional, fully
local log of migraine-mode activations (time, duration, mild/full) or schedule pauses.

**Why it matters**: The review brief specifically asks whether the tool "provides insights to
help users understand what helps them specifically." Right now the answer is no, and it's an
easy no to fix without compromising the app's genuinely good privacy story (no telemetry, no
network calls except opt-in geocoding). A user can't currently answer "do I get migraines more
on days I forget to set a lower daytime brightness?" or "has migraine mode frequency gone down
since I started using deep-night mode?" — the app has all the data to answer this and throws it
away.

**What good looks like**: An opt-in, purely local (`%AppData%\MonitorWellness\history.jsonl` or
similar) log of migraine-mode activate/deactivate events and pause events, with a simple
"History" view in Settings (a list or basic chart: activations per week, average duration). No
network transmission, no change to the app's privacy stance — this is the same local-JSON
pattern `ProfileStore`/`SettingsStore` already use.

### 1.6 Other roadmap items already correctly identified, not re-litigated here

Auto-update checker, code signing, per-monitor contrast control, HDR display handling, and
laptop-panel brightness via WMI are all already tracked in `IMPLEMENTATION.md`'s "Deferred to
v1.1+" list and remain reasonable deferrals — flagged here only to confirm they were reviewed
and no additional urgency was found beyond what's already written down.

---

## 2. User experience

### 2.1 The only mouse-driven emergency activation path is a right-click through a growing menu

**Missing**: `NotifyIcon` in `App.xaml.cs` (`OnStartup`, ~line 62) wires only
`ContextMenuStrip` — there is no `Click`/`DoubleClick` handler on the tray icon itself. The
context menu itself has grown to roughly 14 entries (Toggle/Activate Full/Activate
Mild/Deactivate/Identify/Pause submenu/Resume/Start with Windows/Diagnostics/Settings/Exit).

**Why it matters**: The global hotkey is the documented primary path, but hotkeys can be
misremembered, remapped, or simply not come to mind under stress. The fallback today is:
right-click the tray icon, visually scan roughly a dozen items, find the right one. For a tool
whose signature feature is triggered "the moment a migraine or aura starts" — a state that can
include visual disturbance and reduced concentration — that fallback is worse than it needs to
be.

**What good looks like**: A single-click (or double-click) on the tray icon toggling migraine
mode directly, with the existing menu remaining available via right-click for everything else.
This is a small code change (one `Click` handler) with an outsized UX benefit for exactly the
moment this app is designed around.

### 2.2 In-app claim of "instant relief" — the exact overclaim EVALUATION.md fixed, but only in the README

**Missing**: `OnboardingWindow.xaml` (lines 24-26) still reads: *"Migraine Mode gives instant
relief with a research-backed muted green tint."* — stated as fact. EVALUATION.md documents
softening this same claim in the README ("this needed softening... 'immediate relief' is
asserted as fact"), but the identical claim, in the one screen every single user is guaranteed
to see on first launch, was missed.

**Why it matters**: This is the exact concern EVALUATION.md raised about the disclaimer gap —
"someone relying on it instead of appropriate care is a real, if small, risk for a free tool
touching a real medical condition" — reappearing in the one place with 100% first-run reach.

**What good looks like**: Match the README's already-softened language, e.g. *"Migraine Mode
switches to a muted green tint based on published research on light and migraine photophobia —
see EVALUATION.md for what that research does and doesn't show."* Should ship alongside a
first-run medical disclaimer, which EVALUATION.md already recommends and which doesn't exist
anywhere in-app today (only in repo markdown a typical user will never open).

### 2.3 Slider-only numeric entry removed the ability to type an exact value

**Missing**: Week 7 (per `IMPLEMENTATION.md`) deliberately replaced Kelvin/brightness/opacity
text boxes with sliders for live preview. That's a good call for discoverability, but it fully
removed the ability to type or paste a specific value — `DayKelvinSlider` etc. have no adjacent
editable numeric field.

**Why it matters**: Two real users are worse off: someone who wants to match a specific value
(a friend's recommended 3700K, a number from a forum post) now has to drag-and-eyeball a slider
snapped to 50K ticks; and sliders are generally harder to operate precisely for anyone with a
motor impairment or using assistive tech than a text field with spinner buttons; screen readers
also announce slider drags far less usefully than a labeled numeric field's value.

**What good looks like**: Keep the slider (it's genuinely good for live preview) but add a
small adjacent numeric field bound to the same value, so both entry methods work — this is a
common and cheap WPF pattern (`TextBox` bound two-way to the same dependency property as the
`Slider`).

### 2.4 No way to re-open onboarding/help content after first dismissal

**Missing**: `OnboardingWindow` shows exactly once, gated on `HasCompletedOnboarding`
(`App.xaml.cs:150-159`). There is no "Help," "About," or "Show onboarding again" entry anywhere
in the tray menu.

**Why it matters**: A user who dismissed onboarding without reading closely (a very normal
thing to do on first launch of any app) has no path back to "wait, what's the hotkey again?"
or "why is the migraine tint green and not the warm color I expected?" short of finding
IMPLEMENTATION.md/EVALUATION.md in a GitHub repo — not a realistic expectation for a
non-technical user.

**What good looks like**: A permanent "Help / About" tray menu item that reopens the same
onboarding content (or a superset including the hotkey and a one-line pointer to the
evidence-quality summary from §4 below).

### 2.5 Unlabeled per-monitor numeric fields, discoverable only via hover tooltip

**Missing**: `SettingsWindow.BuildMonitorRows()` (~line 356) lays out, per monitor: a label, two
checkboxes ("Exclude", "Color-accurate"), then **two bare `TextBox` controls with no visible
label** — dim multiplier and Kelvin offset — distinguished only by a `ToolTip` set on each box.

**Why it matters**: Tooltips require hover-and-wait; they're invisible to touch input, to
keyboard-only navigation, and to anyone who doesn't happen to pause over the right box. A user
looking at a settings row with two unlabeled number boxes has no way to know which is which
without trial and error or lucky hovering.

**What good looks like**: A one-line static header row above the monitor list ("Monitor |
Exclude | Color-accurate | Dim×| Kelvin±") or a small `TextBlock` caption above each column —
cheap, and fixes real discoverability for every user, not just the ones who read
IMPLEMENTATION.md's dev-log wording ("Last two boxes per row...") that currently substitutes
for a UI label.

### 2.6 No mnemonics/keyboard accelerators anywhere in the tray menu or windows

**Missing**: No `ToolStripMenuItem` text uses `&` mnemonic markers; no `AccessKey` set on any
XAML control (`AutomationProperties` isn't referenced anywhere in `src/`).

**Why it matters**: Directly relevant to EVALUATION.md's already-flagged "no accessibility
pass" — this is the concrete mechanism that pass would need to fix. Keyboard-only users
currently cannot navigate the tray context menu by mnemonic key, and screen readers have no
`AutomationProperties.Name` to announce for sliders, the map click surface, or icon-only
buttons.

**What good looks like**: Add mnemonics to tray menu text (`"&Settings..."`) and
`AutomationProperties.Name`/`HelpText` to the settings window's sliders and the map control at
minimum — a scoped, achievable first accessibility pass rather than the "larger undertaking"
framing EVALUATION.md gives the whole topic.

---

## 3. Technical robustness

### 3.1 No single-instance guard

**Missing**: Nothing in `App.xaml.cs.OnStartup` checks for (or creates) a named `Mutex` or
equivalent to prevent a second instance from running.

**Why it matters**: The app's own positioning is "portable — run the downloaded exe directly...
no installer needed" (README) *and* separately supports registering itself for auto-start
(`AutoStartManager`). Those two things combined make double-launch a realistic scenario: a user
auto-starts via Task Scheduler, then later double-clicks the same portable exe again (forgetting
it's already running), or copies the portable exe to a second location and runs both. The result
is two competing schedule timers, two tray icons, two overlay-window sets, and a second
`GlobalHotkey` registration that will silently fail (logged, not surfaced) — confusing, and a
plausible real occurrence given the app's own portable/auto-start design, not a hypothetical
edge case.

**What good looks like**: A named `Mutex` (e.g., `"Global\\MonitorWellness-SingleInstance"`)
checked at the top of `OnStartup`; if already held, activate the existing instance's tray
presence (or just show a balloon "Monitor Wellness is already running") and exit cleanly.

### 3.2 Unhandled exceptions are swallowed app-wide, indefinitely — flagged in the app's own comment as unresolved

**Missing**: `App.xaml.cs:44-48`:
```csharp
DispatcherUnhandledException += (_, args) =>
{
    DebugLog.Write($"UNHANDLED DISPATCHER EXCEPTION: {args.Exception}");
    args.Handled = true; // diagnostic build only — keep the app alive so the log is useful; revisit before v1 ships
};
```
The comment explicitly marks this as a pre-release decision that still needs to be made. It
was not revisited in Weeks 5-10 per `IMPLEMENTATION.md`'s own change log — it's still exactly
this, unconditionally, in the current source.

**Why it matters**: This is distinct from (and not covered by) EVALUATION.md's "no automated
tests for the Win32/WPF layer" finding — it's an explicit, self-identified decision item that
got left in its "diagnostic build only" state through ten weeks of further development. In
production, swallowing every unhandled exception forever means the app can silently limp along
in a broken state indefinitely (e.g., after an exception mid-`RunScheduleTick`, gamma ramp or
overlay could be left in a stale state with the timer still "successfully" ticking) — worse
than a controlled restart or a visible error, and the exact opposite of this project's stated
"verify against reality, don't assume" discipline.

**What good looks like**: A decision, not a default. At minimum: distinguish recoverable UI
exceptions (fine to swallow-and-log) from failures in the scheduling/gamma/overlay path (should
probably surface a tray warning and/or attempt a controlled re-init of the affected subsystem
rather than silently continuing).

### 3.3 Gamma-ramp-only color/contrast is inherently a narrow-range mechanism — architecturally sound, but worth stating the ceiling plainly

Already well-documented (Week 1 finding, `MinSafeChannelFactor`) — cross-referenced here only to
note the one thing not spelled out: **every future feature that touches color temperature or
contrast inherits this same ~3300K floor**, including any future "even warmer bedtime" tuning
or "warmer than deep night" request a user might make. Worth a one-line architecture note in
IMPLEMENTATION.md's locked decisions so a future contributor doesn't rediscover this by getting
a silently-rejected gamma call again.

### 3.4 Untested interaction surfaces beyond what EVALUATION.md already flags

EVALUATION.md is candid about one-machine testing and no HDR/laptop/other-GPU coverage. Two
additional specific surfaces worth naming explicitly, since they're common enough to hit in
practice rather than exotic:

- **Remote Desktop / virtual displays**: `GammaControllerManager` and `OverlayController` both
  degrade gracefully if a display rejects a device context (try/catch around
  `GammaRampController`'s constructor) — good — but this has apparently never been exercised
  against an actual RDP session's virtual display, which behaves differently from a physical
  monitor being unplugged.
- **Laptop hybrid-GPU switching** (Optimus/similar): undocking, external-monitor hot-plug, and
  discrete/integrated GPU handoff on a laptop are a different code path from the tested
  desktop's static 3-monitor Intel UHD setup, and are exactly the scenario a laptop user (a
  large fraction of any real userbase) will hit routinely, not as an edge case.

---

## 4. Personalization and evidence-based grounding

EVALUATION.md's science review (§2 there) is unusually rigorous and shouldn't be re-derived
here — its conclusions (green tint: best-supported; evening warming: solid mechanism, unproven
specific intervention; daytime dimming: contradicted by the 2023 Cochrane review as an
eye-strain fix, kept as a comfort feature) are sound and this review defers to them. Gaps this
pass adds on top:

### 4.1 The evidence-quality nuance lives only in a repo markdown file, invisible to any real user

**Missing**: `EVALUATION.md`'s careful "mechanism evidence vs. direct-intervention evidence"
table — the single most valuable piece of honesty in this whole project — is not linked,
summarized, or referenced anywhere inside the running app. A user sees the onboarding window's
(currently overclaiming, §2.2) one-liner and nothing else.

**Why it matters**: The whole point of that evaluation was informed consent-style honesty about
what's proven vs. plausible vs. comfort-only. Right now that honesty doesn't reach anyone who
doesn't clone the GitHub repo and open a markdown file.

**What good looks like**: A short "Why these colors?" link or expandable section in Settings
next to Migraine Mode and Day/Night Schedule, summarizing the same three-tier honesty
(green tint: replicated research on ambient light, applied here as a screen analogy; evening
warming: solid mechanism, unproven as a software intervention; daytime dimming: comfort feature,
not a proven eye-strain fix) in two or three sentences each.

### 4.2 No accommodation for the migraine/photosensitivity population's frequent comorbidities beyond sound

**Missing**: `PlaySoundOnMigraineToggle` already correctly defaults off because of phonophobia
comorbidity (a genuinely good existing decision, called out in IMPLEMENTATION.md Week 9). Two
adjacent comorbidities aren't considered anywhere: **motor coordination during aura** (relevant
to §2.1's hotkey-chord difficulty) and **photosensitive epilepsy** overlap with migraine with
aura in some patients, for which the *rate* of a visual change matters, not just its color.

**Why it matters**: The 20-second migraine deactivation fade (`MigraineModeController`,
`FadeDuration`) is well clear of any flicker/strobe concern, and activation is instant (a single
step change, not a strobe) — so there's no actual seizure-safety defect here. But this
reasoning has never been written down anywhere, unlike the sound-sensitivity reasoning, which
is documented thoroughly. Worth a one-line note in `MigraineModeController`'s doc comment
alongside the existing sound-sensitivity reasoning, both for future-contributor context and
because it's a legitimate design decision worth being explicit about, not just accidentally
correct.

**What good looks like**: Document it; no code change needed, since the current behavior is
already safe by these criteria.

### 4.3 No calibration/guidance path for individual sensitivity — everything is manual sliders

**Missing**: Every comfort dial (day/night Kelvin, brightness, migraine opacity/contrast) is a
raw slider with no guided starting point beyond the hardcoded defaults. There's no "which of
these feels better, A or B?" style calibration flow, despite eye-strain/photophobia sensitivity
varying enormously person to person (part of why EVALUATION.md is right that a universal
"reduces eye strain" claim doesn't hold — individual variance is large).

**Why it matters**: A non-technical user with no intuition for what "3400K" or "15% contrast
reduction" *feels like* has no on-ramp beyond trial and error across many separate sliders — a
real barrier for the "accessible to non-technical users" bar this review is asked to check
against.

**What good looks like**: Doesn't need to be sophisticated — even a simple two-or-three-option
A/B preview button ("Show me option A / option B, tell me which is more comfortable") for the
migraine tint specifically (where the research is strongest) would turn an abstract slider into
an experiential choice, matching how the app already treats live preview as a design principle
elsewhere.

---

## 5. Integration and compatibility

### 5.1 Windows Night Light / f.lux conflict — see §1.4 (feature gap) for the fix; noted here as the integration-specific framing

Restated briefly for completeness under this heading: this app has no awareness of the other
color-temperature tools it's most likely to be installed alongside, and the two other consumers
of the exact same OS-level ramp are the two most likely to be present given the source
population (this app's own README states it's a rebuild replacing an f.lux-based prototype —
f.lux itself may well still be installed from before).

### 5.2 GPU vendor color management software

**Missing**: No detection of NVIDIA/AMD/Intel driver-panel color adjustments (their own "Night
Mode"/color presets, or ICC-profile-loading utilities like DisplayCAL) that also write to
gamma-ramp-adjacent state.

**Why it matters**: Same failure mode as §1.4/§5.1 — competing writers to the same underlying
mechanism, invisible to this app's own logic, which only ever checks "did *my* call succeed,"
never "did something else's call happen after mine."

**What good looks like**: Lower priority than the Night Light/f.lux case (less universally
present), but worth a single README line under a "known conflicts" heading: "If your screen
color seems to fight or flicker, check for other color-management software (Night Light, f.lux,
GPU vendor night-mode utilities) and disable all but one."

### 5.3 No coordination with Windows' own per-app HDR/auto-color-management pipeline

Cross-referenced from EVALUATION.md's own "no HDR-enabled display tested... plausibly broken"
finding — confirmed from the source that `GammaRampController` uses the classic
`SetDeviceGammaRamp` path, which is documented Windows behavior to interact unpredictably with
HDR-enabled displays (where the OS applies its own tone-mapping pipeline on top). No code change
suggested here beyond what EVALUATION.md already flags — just confirming from source review
that there's no HDR-detection guard (`GetDisplayConfigBufferSizes`/`DisplayConfigGetDeviceInfo`
HDR state check) that could at least warn rather than silently apply a gamma ramp whose effect
on an HDR pipeline is unverified.

---

## 6. Documentation and onboarding

### 6.1 See §2.2 and §2.4 above (UX section) for the two concrete, actionable gaps here

The overclaiming onboarding text (§2.2) and the lack of any in-app path back to help content
(§2.4) are documentation gaps as much as UX ones — restated here only to avoid the analysis
reading as if documentation were otherwise fine. It's the *in-app* documentation specifically
that's thin; the *repository* documentation (README, EVALUATION, IMPLEMENTATION) is genuinely
excellent and unusually candid for a solo project.

### 6.2 No user-facing changelog

Already identified in EVALUATION.md §3 ("No user-facing changelog... `IMPLEMENTATION.md` is a
detailed, valuable dev log, but... not a user wondering what changed between versions") —
confirmed still true; ten more weeks of development since that finding have all gone into
`IMPLEMENTATION.md`'s dev-log format, not a `CHANGELOG.md`. Worth doing before the first public
release specifically because the feature set has grown large enough (profiles, bedtime mode,
mild/full presets, per-monitor Kelvin offsets) that a new version's release notes will need
somewhere to live that isn't a 855-line development diary.

### 6.3 No in-app privacy statement, despite a genuinely good privacy story

Also already flagged in EVALUATION.md §3 — confirmed from source review that the privacy story
really is as good as claimed (the only network call anywhere is `GeocodingService`, gated
behind an explicit user-initiated search; `DebugLog`/`SettingsStore`/`ProfileStore` all write
only to local `%AppData%`). This is worth surfacing in-app (even a single line in Settings:
"Nothing here is sent anywhere except an optional location search you trigger yourself") since
it's a genuine trust-builder currently locked inside a README a typical user won't read.

---

## 7. Data and monitoring

### 7.1 See §1.5 above — the one concrete, buildable gap in this dimension

Restated for completeness under this heading: the app currently tracks nothing, which is
correct for telemetry (no argument for changing that) but leaves zero path for a user to build
their own evidence about what's helping them, which §1.5 proposes a fully-local, opt-in fix for.

### 7.2 No way to export the debug log or settings for a bug report

**Missing**: `DebugLog` writes to `%AppData%\MonitorWellness\debug.log` (documented, capped at
2MB) but there's no in-app "Copy diagnostic info" / "Open logs folder" action — a user filing a
bug report has to be told the exact path and go find it manually in Explorer.

**Why it matters**: Minor, but directly undercuts the value `DebugLog` itself already proved
during development (IMPLEMENTATION.md credits it with catching several real bugs) — that same
value applies to a future real user's bug report only if the log is easy for them to actually
retrieve and attach.

**What good looks like**: A "Open Logs Folder" tray/settings menu item (`Process.Start` on the
`%AppData%\MonitorWellness\` directory) — a few lines of code for a real support-flow
improvement.

---

## Summary — highest-leverage items

If only a handful of these get picked up next, in order of impact-to-effort ratio:

1. **Fix the onboarding window's overclaiming text** (§2.2) — one XAML string, closes a real
   gap in an already-completed fix.
2. **Add a tray-icon single-click migraine toggle** (§2.1) — one event handler, directly
   improves the app's core safety-relevant interaction.
3. **Add a single-instance guard** (§3.1) — a few lines, prevents a real and plausible failure
   mode given the app's own portable + auto-start design.
4. **Label the per-monitor dim/Kelvin boxes and add text-entry fallback to sliders** (§2.3,
   §2.5) — small XAML changes, fixes two genuine discoverability/accessibility gaps.
5. **Warn about Night Light/f.lux conflicts** (§1.4/§5.1) — a startup registry/process check,
   prevents a confusing first-run experience for exactly this app's likely user base.
6. **Decide (not default) how the app should behave on an unhandled exception** (§3.2) — a
   decision already flagged in the app's own source comment, still open.
7. **Surface the evidence-quality summary in-app, not just in a repo markdown file** (§4.1) —
   the most values-aligned improvement, given how much rigor already went into writing that
   evaluation down once.
