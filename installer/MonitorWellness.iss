; Monitor Wellness installer.
;
; Auto-start is registered via Task Scheduler (schtasks), not a Registry Run key -- see the
; "Locked decisions" section of IMPLEMENTATION.md. /rl limited runs the task with standard
; (non-admin) rights, matching what the app actually needs.
;
; Source is the self-contained single-file publish output (see IMPLEMENTATION.md Week 4 for
; why: it avoids requiring end users to separately install the .NET runtime). Rebuild it
; before compiling this script:
;   dotnet publish src\MonitorWellness\MonitorWellness.csproj -c Release -r win-x64 ^
;     --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
;     -o publish

#define MyAppName "Monitor Wellness"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "Monitor Wellness"
#define MyAppExeName "MonitorWellness.exe"
#define MyTaskName "MonitorWellness"

[Setup]
AppId={{B3B6D1D6-6C7B-4A9A-9B1E-3C6F7B9A2E10}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=MonitorWellness-Setup-{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Registering an onlogon-triggered scheduled task requires elevation -- confirmed directly:
; `schtasks /create /sc onlogon` fails with "Access is denied" under a standard (non-admin)
; token, while the same command with /sc once succeeds fine. PrivilegesRequired=lowest was
; tried first specifically to avoid a UAC prompt, but that's incompatible with registering
; this kind of trigger, so the installer needs admin after all (standard for an installer
; that also writes to Program Files). /rl limited below still makes the *app itself* run
; with standard rights once the task fires -- only registering it needs elevation, not
; running it.
PrivilegesRequired=admin

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\publish\MonitorWellness.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Register auto-start at logon. /f overwrites a pre-existing task from a previous install.
Filename: "{sys}\schtasks.exe"; \
  Parameters: "/create /tn ""{#MyTaskName}"" /tr ""\""{app}\{#MyAppExeName}\"""" /sc onlogon /rl limited /f"; \
  Flags: runhidden

Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\schtasks.exe"; Parameters: "/delete /tn ""{#MyTaskName}"" /f"; Flags: runhidden; RunOnceId: "RemoveAutoStartTask"

[UninstallDelete]
; The app writes its own settings/log files under %AppData% (see SettingsStore.cs /
; DebugLog.cs) -- these are user data, not installed program files, so deliberately NOT
; removed by uninstall. If a full "remove everything" uninstall is wanted later, they live at
; %AppData%\MonitorWellness\.
