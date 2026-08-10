# Monitor Wellness user guide

Monitor Wellness is a local Windows comfort tool. It changes the appearance of your desktop;
it is not a medical device and does not diagnose, treat, or prevent a condition.

## Start here

1. Right-click the tray icon near the clock and choose **Settings**.
2. In **Schedule**, set a location. The app uses its sunrise and sunset to move gradually
   between your day, night, and optional deep-night settings.
3. Use the live controls to judge the result on your own displays, then choose **Save** only
   when it is comfortable. **Cancel** abandons unsaved settings-window changes.
4. Use **Pause Schedule** from the tray menu whenever you need native colour for colour-critical
   work. You can resume it from the same menu.

## If the screen is difficult to use

Press **Ctrl+Alt+Shift+R** at any time, or choose **Emergency Restore Screen** from the tray
menu. It clears the app's tint and dimming, restores native gamma and any approved hardware
brightness, clears any temporary comfort plan, then pauses the schedule for one hour. This is
the quickest recovery route.

Normal scheduling keeps the primary display at a visible floor. A deliberate 0% live preview
is different: it asks first, lasts at most 15 seconds, and returns to 20% automatically. Press
**Escape** while Settings is open to restore that preview immediately.

## How dimming works

By default, Monitor Wellness uses a translucent desktop overlay rather than reducing the
monitor's physical backlight. This keeps the monitor's configured backlight level unchanged.
It may avoid problems that some displays introduce when their backlight is lowered, but the app
cannot measure panel PWM, temporal dithering, spectrum, or medical suitability.

The overlay refreshes only after a short quiet period when Windows reports display-topology
changes. Ordinary app and window switching does not reapply display controls unless you enter
or leave a configured app-aware rule. If you still notice repeated flashes, turn off other
colour-management tools (Windows Night Light, f.lux, or GPU vendor night modes) so only one
tool changes colour. For exclusive-fullscreen, protected video, or HDR content, a normal desktop
overlay may not be visible; use windowed mode or Emergency Restore Screen.

## Display Capability Passport

Open **tray icon → Diagnostics → Display Capability Passport**. The report is local and
read-only. It shows the active displays Windows exposes to the app, stable hardware identity,
DDC/CI eligibility, HDR state, ambient-light-sensor availability, and the recommended brightness
backend.

Use it as an explanation of what this PC can expose—not a display-health or medical report.
An `unknown` result for PWM, dithering, spectral output, or medical suitability is intentional.

## Optional hardware brightness

External monitors that expose DDC/CI may offer physical brightness control in
**Settings → Monitors & Breaks**. It is off by default.

1. Check the Passport or the monitor row for availability.
2. Choose **Test hardware brightness** for that specific monitor.
3. Confirm that the small, temporary change is safe and that it restores correctly.
4. Tick **HW** and choose **Save** to opt in.

Approval follows the monitor's stable physical identity, not a temporary display number. If a
hardware command fails, the app quarantines that monitor and returns to the overlay fallback.

## Comfort plans and profiles

In **Settings → Profiles & History**, use **Built-in comfort plans** to preview Balanced,
Reading, Colour-critical, Early sensitivity, or Recovery settings. These are editable starting
points, not treatment recommendations. Previewing does not save; use **Save** only after you
have adjusted and accepted the result.

For a quick, non-persistent change, open **tray icon → Temporary Comfort Plan** and select a
plan. It stays active until you choose **Return to saved schedule**, select a different plan,
or use Emergency Restore Screen. A matching application rule set to **Restore native display**
still takes precedence for colour-critical work.

For work that needs an actually unmodified display, use **Restore native display** in an
application rule or **Pause Schedule**. The Colour-critical plan is a least-adjusted comfort
starting point, not a substitute for native colour management.

Profiles save colour, brightness, and Migraine Mode choices for quick switching. Location,
monitor setup, and break reminders stay outside profiles so a profile cannot silently change
your display identity or reminder behaviour.

## App-aware comfort rules

In **Settings → Profiles & History**, add an executable name such as `photoshop.exe` or
`powerpnt`. For each rule, choose either **Restore native display** (useful for colour-critical
work) or **Use comfort plan** (for example, Reading while Word is active). A comfort plan is
temporary: it does not alter saved settings, and the normal schedule resumes after you switch
away.

An optional window-title phrase narrows a rule to a named meeting, presentation, or document.
For the same executable, a matching title-specific rule takes precedence over the general rule.
Window titles are used locally for matching only; they are not included in diagnostic bundles.

## Migraine Mode and breaks

Left-click the tray icon, use the tray menu, or use the configured hotkey to toggle Migraine
Mode. It activates immediately and fades out gradually when deactivated to avoid an abrupt
brightness transition. Choose Gentle or Strong as a personal comfort preference; neither is a
medical treatment.

The optional 20-20-20 reminder is off by default. When enabled, it stays quiet during Migraine
Mode, full-screen work, and time away from the PC. The tray menu also provides a dismissible
20-second focus timer.

## Privacy, support, and accessibility

Settings, optional local history, and logs remain on this PC. Location search happens only when
you ask it to; update checks are opt-in. See [PRIVACY.md](PRIVACY.md) for the full policy.

When local history is enabled, the Settings summary compares activations in the last seven days
with the preceding seven days. It is a private activity record to help you notice your own use,
not a symptom tracker, diagnosis, or medical insight.

The app supports keyboard operation, Windows contrast themes, colour filters, and scaling, but
it currently ships in English only. For a problem report, use **tray icon → Diagnostics → Export
Diagnostic Bundle**, review the bundle before sharing it, and include your Windows, monitor, and
GPU details.
