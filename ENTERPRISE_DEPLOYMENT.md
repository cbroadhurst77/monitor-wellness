# Enterprise deployment

Monitor Wellness is a local-first Windows accessibility utility. It does not require an
account, service, administrator rights while running, or health/usage telemetry. This guide
provides a deployable baseline; each organisation remains responsible for its own accessibility,
privacy, security, and accommodation decisions.

## Prerequisites

- Use a release with a valid Authenticode signature and retained SHA-256 manifest.
- Test the exact release on representative monitor, GPU, dock, HDR, and multi-display hardware.
- Use `Display Capability Passport` after deployment to establish which monitors are eligible
  for optional DDC/CI hardware brightness. Hardware brightness is off unless a user has
  completed its reversible test.
- Review [RELEASE_READINESS.md](RELEASE_READINESS.md) and [QA_CHECKLIST.md](QA_CHECKLIST.md).

## Silent installation

The Inno Setup package supports a standard unattended command line:

```powershell
Start-Process -FilePath .\MonitorWellness-Setup-0.2.5.exe -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait
if ($LASTEXITCODE -ne 0) { throw "Monitor Wellness installer failed with exit code $LASTEXITCODE." }
```

The installer deliberately does not enable auto-start or hardware brightness. Those remain
explicit user choices because they change behaviour at sign-in and on physical displays.

## Intune Win32 package

1. Put only the signed installer in a staging folder.
2. Use Microsoft’s Win32 Content Prep Tool to create a `.intunewin` package.
3. Configure the install command above and an uninstall command using the installed product’s
   Inno Setup uninstaller with `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`.
4. Use an explicit file/version detection rule for `MonitorWellness.exe` in the Program Files
   installation directory.
5. Assign first to a pilot group with a documented support/rollback contact.

Do not deploy application configuration through a world-writable file or an environment
variable. A managed configuration channel must have an organisation-owned trust boundary
(for example an MDM-delivered, ACL-protected and signed policy) before it can override an
individual’s comfort settings.

## Break reminders, call awareness and command-line switches

- Break reminders are off by default and entirely local; no network access is involved. With
  them on, and "Wait while my microphone or camera is in use" ticked (the default), the app
  reads, read-only, the Windows privacy records under
  `HKCU\Software\Microsoft\Windows\CapabilityAccessManager\ConsentStore\microphone` and
  `...\webcam` (which apps are using those devices) so a reminder waits until a call ends. It never
  opens the devices and does not store or log the app names. The option is
  `BreakReminderHoldDuringCalls` in settings.json.
- `MonitorWellness.exe --restore` puts the display back to normal and exits (it refuses while
  another instance is running). `--restart-wait <pid>` is used by the tray "Restart" item: the new
  process waits up to 20 seconds for the old one to exit before taking over; it is not meant to be
  run by hand.
- On sign-out or shutdown the app hands the displays back immediately and writes
  "Windows is ending the session" and "App exit complete" to `debug.log`, which helps when
  investigating an unclean shutdown.

## Privacy and support

- Diagnostic bundles are manually exported and exclude settings, location, history, and
  profiles. Users should review the bundled log before sharing it.
- Do not collect migraine activation history, ratings, window titles, or display capability
  data centrally without documented purpose, minimisation, retention, access, and consent/
  lawful-basis review.
- Do not use the app to infer health conditions or employment performance.
- Keep an emergency recovery instruction available: `Ctrl+Alt+Shift+R` immediately restores
  the display and pauses the schedule for one hour.
