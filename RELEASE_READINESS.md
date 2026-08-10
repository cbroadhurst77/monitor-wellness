# Commercial release readiness

This document separates completed engineering controls from release gates that cannot be
truthfully completed by source-code changes alone. Do not mark a commercial release ready until
every applicable blocker below has an auditable owner and result.

## Completed in the product

- Settings stay local by default; optional update checks are off until a user enables them.
- Screen changes retain recovery paths: emergency restore, brightness floors, reversible
  hardware-brightness tests, and automatic quarantine after hardware-command failure.
- The default dimmer is a compositor overlay, avoiding untested direct backlight changes.
  Hardware brightness is only available after an explicit, monitor-specific confirmation.
- Break prompts are opt-in and idle-aware. The optional 20-second timer is user started,
  non-modal, and always dismissible.

## Release blockers requiring external action

### Authenticode signing and provenance

1. Obtain an organisation-owned code-signing certificate and store its private key in an
   approved hardware-backed or managed signing service.
2. Sign and timestamp both `MonitorWellness.exe` and the Inno Setup installer in the release
   workflow. Never put a certificate, password, or PFX file in this repository.
3. Run `tools/Verify-Release.ps1` against the final artifacts, retain the generated manifest,
   and publish SHA-256 checksums with the release.
4. Test SmartScreen reputation and the update path using the actual signed artifacts.

### Hardware and display compatibility

1. Execute every release-blocking row in [QA_CHECKLIST.md](QA_CHECKLIST.md) on documented
   Intel, AMD, Nvidia, Windows 10/11, HDR, laptop, mixed-DPI, multi-monitor, and DDC/CI rigs.
2. Record model, GPU driver, Windows build, display connection, outcome, and any rollback.
3. Treat an overlay flash/pulse, unrecoverable dim state, or a hardware-brightness restoration
   failure as a release blocker until reproduced, fixed, and regression-tested.

### Localisation and accessibility verification

The app currently ships English-only UI. Do not represent it as localised. Before supporting a
locale, move user-facing strings from XAML and C# into resource files, use culture-aware date,
time, number and plural formatting, and obtain professional translation plus in-context review.

For every supported locale, run keyboard-only, Narrator, high-contrast, 200% scaling, and
right-to-left checks where relevant. Pseudolocalisation should be part of CI or pre-release QA
to reveal clipped layouts and strings that bypass resources.

### Enterprise deployment and privacy review

Agree the customer requirements before implementing management features. At minimum, decide:

- installer format and distribution channel (Inno Setup, Intune, MSI/MSIX, or equivalent);
- whether organisation policy may manage update checks, auto-start, hardware brightness, or
  diagnostics, and how a user is told about that policy;
- log retention, diagnostic-bundle handling, incident response, and support ownership;
- legal review of the EULA, privacy notice, accessibility statement, and migraine-related
  non-medical claims for every sales territory.

No policy, telemetry, or remote control should be added speculatively: these change the privacy
and threat model and need an explicit customer and legal decision first.

## Evidence required for a release decision

- CI build, test, dependency scan, and a reviewed diff for the exact release commit.
- Signed application and installer, verification manifest, and published checksums.
- Completed hardware QA record with named testers and dated results.
- Approved supported-language list and accessibility test evidence.
- Security/privacy/legal sign-off and documented support/rollback procedure.
