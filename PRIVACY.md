# Monitor Wellness — Privacy Policy

> **⚠ Draft template, not lawyer-reviewed.** Written directly from the app's actual code
> and behavior (verified, not assumed) — accurate today, but re-check it against the code
> whenever a feature that touches the network or stores data changes, and before relying
> on it for GDPR/UK-GDPR or similar compliance purposes. Replace `[placeholders]`.

**Effective date:** 31 July 2026
**Last updated:** 11 November 2026 — break reminder and call-awareness section added (nothing
new is sent over the network)
**Data controller:** Equinox Consulting Ltd, [registered address], [country]
**Contact:** [insert contact email]

## Summary

Monitor Wellness is a Windows desktop application. It does not have an account system,
does not run any analytics or telemetry, and does not display ads. Almost everything it
does happens entirely on your own computer. This policy covers the two exceptions.

## What stays on your computer

All of the following are stored only in `%AppData%\MonitorWellness\` on your own PC and
are never transmitted anywhere:

- your settings (location, color/brightness schedule, Migraine Mode configuration,
  per-monitor setup, saved profiles);
- diagnostic logs (`debug.log`), used only for your own troubleshooting;
- Migraine Mode / schedule-pause history, and helpfulness ratings — both entirely
  opt-in and off by default (Settings → Profiles & History).

Uninstalling the app does not delete this folder automatically, so you can reinstall
without losing your settings; delete it yourself if you want a clean removal.

## The two network calls this app ever makes

**1. Location search** (Settings → Schedule → "Find"). When you type a place name and
click Find, the text you typed is sent to OpenStreetMap's Nominatim geocoding service to
look up its coordinates. This only happens when you click the button — nothing is sent
automatically or in the background. See [Nominatim's own privacy
policy](https://operations.osmfoundation.org/policies/nominatim/) for how they handle
that request.

**2. Update check** (Settings → Profiles & History → Updates, **off by default**). If
you turn this on, roughly once a day the app asks GitHub's public API for this
repository's recent release list (including pre-releases, unless you turn that off) — a plain
request with no personal information, account identifiers, or usage data attached, just
"what's the newest version." If a newer
version exists you'll see a one-time notification linking to it; nothing downloads or
installs automatically.

Neither call includes your name, an identifier, or any of your settings/history data.

## Ambient light sensor (optional)

If you turn on "Nudge daytime brightness to match the room's actual light level" and
your device has one, the app reads a light-level (lux) value from your device's local
sensor to adjust screen brightness. This reading is used immediately and locally — it is
never stored or transmitted anywhere.

## Break reminders and call awareness (optional)

Break reminders are **off by default**. When you turn them on, the app does the following,
entirely on your computer, and stores or transmits none of what it learns:

- **Keyboard and mouse activity.** It asks Windows how long ago the last keyboard or mouse
  input was, so it can tell whether you are at the computer. It learns only that elapsed
  time — never which keys you pressed or what you clicked.
- **Full-screen apps.** It checks whether the window in front covers a whole monitor, so
  reminders wait during presentations, videos and games.
- **Calls** (on once reminders are on; turn it off under Settings → Monitors & Breaks). To
  hold a reminder until a call ends, it reads — read-only — the list Windows keeps of which
  apps are currently using the microphone or camera (the data behind Windows' own "recently
  used" privacy indicator, in your own user registry hive). It checks whether a camera is in
  use, or whether the microphone is in use by an app on a built-in list of calling apps (for
  example Teams, Zoom, Webex and web browsers). It **never opens the microphone or camera,
  never listens to or records anything**, and the app names it sees are compared with that
  list and then discarded — they are not logged, stored or sent anywhere.
- **Your choices** (interval, hours, style, sound, break length) are saved in your settings
  file like your other settings.

## Third-party services used

| Service | Purpose | Triggered by | Data sent |
|---|---|---|---|
| OpenStreetMap Nominatim | Location search | Clicking "Find" | The search text you typed |
| GitHub Releases API | Update check | Opt-in, ~daily | Nothing identifying — a plain version-check request |

## Children's privacy

Monitor Wellness is not directed at children, and we do not knowingly collect
information from children, consistent with the fact that we do not collect information
from anyone.

## Changes to this policy

If what the app sends over the network changes, this document will be updated alongside
that change, with the effective date revised.

## Contact

Questions about this policy: [insert contact email].
