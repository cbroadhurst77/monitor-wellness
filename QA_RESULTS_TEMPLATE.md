# Monitor Wellness — Device Certification Record

Create one completed copy of this file for every release candidate and physical machine.
A failing item in the **Release blockers** section prevents commercial release until resolved
or explicitly waived with a documented engineering decision.

## Test environment

- Release/version and Git commit:
- Date and tester:
- Windows version/build:
- Device model:
- GPU and driver version:
- Display topology, resolution, and DPI:
- Internal panel / external monitor make and model:
- HDR state:
- Accessibility technology used (Narrator, magnifier, keyboard-only):

## Release blockers

| Check | Pass / fail / not applicable | Notes / evidence |
|---|---|---|
| Schedule dimming has no flashing while opening, switching, or closing windows |  |  |
| Emergency Restore Screen restores visible output during schedule and Migraine Mode |  |  |
| 0% preview confirmation, Escape, timeout, and close all restore visible output |  |  |
| Primary display remains at the 20% normal-schedule recovery floor |  |  |
| HDR / exclusive-fullscreen / protected-video recovery leaves desktop usable |  |  |
| Keyboard-only navigation and Narrator announcements work in Settings |  |  |
| Display hot-plug and sleep/resume do not leave stale gamma, overlay, or hardware brightness |  |  |

## Optional hardware brightness

| Display | DDC/CI detected | Reversible test observed | Original brightness restored | Scheduled opt-in tested | PWM/flicker acceptable | Notes |
|---|---:|---:|---:|---:|---:|---|
|  |  |  |  |  |  |  |

Hardware brightness must remain disabled for a monitor unless its reversible test and exact
restoration are both recorded as passing. Test laptop/internal-panel WMI support separately
before enabling a future WMI backend.

## Artifacts

- Installer SHA-256:
- Installer Authenticode signature result:
- Diagnostic bundle retained locally (do not commit user logs):
- Screenshots/video or issue links:
