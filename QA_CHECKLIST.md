# Monitor Wellness — Manual QA Checklist

This is the "test it on real hardware I don't have" gap called out when assessing
commercial readiness: this app has so far only been run and clicked through on one
development machine. Before charging money for it, run through this on at least a
handful of genuinely different machines — a free (your own time) but real requirement.

Track results as: machine description → date → pass/fail/notes. Any FAIL below is a
release blocker; anything under "Nice to confirm" is lower priority.

## Machines to cover (aim for as much variety as you have access to)

- [ ] An **Intel** integrated GPU machine
- [ ] An **AMD** GPU machine
- [ ] An **Nvidia** GPU machine
- [ ] At least one machine on **Windows 11**, one on **Windows 10** (1903+)
- [ ] A **laptop** (ideally one with a real ambient light sensor, to test that feature
      for real rather than relying on the "no sensor found" fallback path)
- [ ] A machine with an **HDR display turned on** (the app's own code and UI already
      flag this as untested — confirm what actually happens, good or bad)
- [ ] A genuine **multi-monitor** setup with mixed resolutions/DPI
- [ ] A machine with **f.lux or Windows Night Light** already running (conflict
      detection — confirm the balloon fires and the wording makes sense)

## Install / uninstall

- [ ] Fresh install via `MonitorWellness-Setup-X.Y.Z.exe` completes without error
- [ ] Desktop shortcut task (if selected) is created; app launches from it
- [ ] Auto-start is **not** registered by installation alone (check Task Scheduler for the
      absence of the "MonitorWellness" task)
- [ ] Explicitly enable **Start with Windows** from the tray menu, approve the UAC prompt,
      and verify the task targets the installed executable with limited rights
- [ ] **Reboot the machine** and confirm the app starts on logon only after that explicit
      opt-in (see the app's "Auto-start Diagnostics" panel for this distinction)
- [ ] Uninstall removes the Start Menu entries, shortcut, and scheduled task
- [ ] Uninstall does **not** delete `%AppData%\MonitorWellness\` (settings/history are
      user data, kept deliberately — confirm this is still the desired behavior)
- [ ] Reinstalling after uninstall picks up the previous settings

## First run

- [ ] Onboarding appears only on first run, walks through all 4 steps, both exit paths
      ("Skip for now" and "Set my location now") work and land in the right place
- [ ] HDR / f.lux / hotkey-conflict / auto-start-drift balloons (whichever apply on
      that machine) appear staggered, not all at once, and only after onboarding closes

## Settings window

- [ ] Opens from the tray menu without error; all four tabs are clickable
- [ ] Every slider in Schedule/Migraine Mode tabs live-previews on the real screen,
      smoothly, while dragging (not just after release)
- [ ] Choosing 0% Day or Night brightness shows the blackout confirmation **before** the
      screen darkens; confirm Escape, the 15-second timeout, and closing Settings all restore
      visibility, and that 0% cannot be saved
- [ ] For each detected DDC/CI display, run **Test HW**. Confirm the backlight dims only
      modestly, returns to its exact original value on every dialog exit path, and only then
      opt in with the adjacent HW checkbox and Save
- [ ] Confirm a previously approved hardware-brightness monitor is still correctly identified
      after reconnecting through a dock or different port; its approval must follow the physical
      monitor, not a `DISPLAYn` number
- [ ] Force or simulate one DDC/CI command failure. Confirm the monitor is quarantined, no
      further automatic hardware commands are attempted, and overlay dimming remains usable
- [ ] Kelvin safety warning appears/disappears correctly at the low end of the Day/Night
      sliders
- [ ] Hex-color and bedtime fields show inline warnings (not a popup) for invalid input
- [ ] Reset to Defaults, Cancel, and Save all behave as expected from every tab
- [ ] Status line at the top reflects reality (Day/Night/Deep Night/Migraine/Paused)
      and updates live while the window stays open
- [ ] With Windows set to dark mode: reopen Settings, Onboarding, About, and
      Troubleshooting — confirm readable text and correctly-colored selected tab in all
      four
- [ ] Flip Windows' light/dark theme **while a window is already open** — confirm it
      re-themes live rather than needing a reopen

## Migraine Mode

- [ ] Tray left-click, hotkey, and tray menu all trigger it; visual change is instant
- [ ] Deactivation always fades smoothly over ~20s, never abrupt
- [ ] Auto-revert timer (if set) actually fires
- [ ] Rating prompt appears after deactivation when both History and the rating prompt
      are enabled; "Don't ask again" actually stops future prompts

## Multi-monitor / topology changes

- [ ] Unplug/replug a monitor (or change display arrangement) while the app is running
      — confirm gamma/overlay rebuild without a crash and without leaving a stuck tint
      on any screen
- [ ] Sleep/resume: confirm color and dim state are correctly reapplied on wake
- [ ] If a DDC/CI monitor is opted in, unplug/replug it, use Emergency Restore Screen, and
      exit the app; confirm its original physical brightness is restored in each case
- [ ] While dimming is active, repeatedly open/close ordinary windows, notifications, Game
      Bar, and an always-on-top app; confirm the screen never flashes or pulses
- [ ] Confirm the normal schedule never dims the Windows primary display below its 20% safety
      floor, even with a per-monitor dim multiplier above 1.0

## Emergency recovery (release blocker)

- [ ] Press `Ctrl+Alt+Shift+R` while the schedule is dimming: all adjusted monitors become
      immediately usable, gamma returns to native, and the tray says the schedule is paused
      for one hour
- [ ] Trigger Emergency Restore Screen while Migraine Mode is active or fading: it must stop
      the effect immediately and it must not reappear during the pause
- [ ] Deliberately reserve `Ctrl+Alt+Shift+R` in another application before launching Monitor
      Wellness: confirm the startup warning appears and the tray-menu Emergency Restore Screen
      action still works
- [ ] On an HDR display and while playing protected/exclusive-fullscreen video, confirm the
      recovery action leaves the desktop usable afterward; document any overlay limitation

## Update checker (new — see Settings → Profiles & History → Updates)

- [ ] Off by default — confirm no network call happens unless explicitly turned on
      (check Task Manager / a network monitor, or just trust the code path if you've
      read it)
- [ ] With it on, confirm a balloon appears if a newer GitHub release exists, and that
      clicking it opens the release page in a browser
- [ ] Confirm it does **not** re-check more than once/day across repeated app restarts

## Nice to confirm (lower priority)

- [ ] Export/Import Settings round-trips correctly
- [ ] Export History (CSV) opens cleanly in Excel/a text editor
- [ ] Profiles: Save As / Load / Delete all work as expected
- [ ] Break reminder balloon fires on schedule and is skipped during Migraine Mode
