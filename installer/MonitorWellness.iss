; Monitor Wellness installer.
;
; Auto-start is an explicit, post-install user choice from the tray UI. When enabled, the app
; registers it via Task Scheduler (schtasks), not a Registry Run key -- see the "Locked
; decisions" section of IMPLEMENTATION.md. It runs with standard (non-admin) rights.
;
; Source is the self-contained single-file publish output (see IMPLEMENTATION.md Week 4 for
; why: it avoids requiring end users to separately install the .NET runtime). Rebuild it
; before compiling this script:
;   dotnet publish src\MonitorWellness\MonitorWellness.csproj -c Release -r win-x64 ^
;     --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
;     -o publish

#define MyAppName "Monitor Wellness"
#define MyAppVersion "0.2.2"
#ifndef MyPublishDir
  #define MyPublishDir "..\publish"
#endif
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
; The default installation directory is Program Files, so installation needs elevation. The
; installed app itself runs as the standard user. Enabling its optional Task Scheduler
; auto-start entry later requires a separate, explicit UAC-approved user action.
PrivilegesRequired=admin

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#MyPublishDir}\MonitorWellness.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Remove the optional auto-start task if the user enabled it from the app.
Filename: "{sys}\schtasks.exe"; Parameters: "/delete /tn ""{#MyTaskName}"" /f"; Flags: runhidden; RunOnceId: "RemoveAutoStartTask"

[UninstallDelete]
; The app writes its own settings/log files under %AppData% (see SettingsStore.cs /
; DebugLog.cs) -- these are user data, not installed program files, so deliberately NOT
; removed by uninstall. If a full "remove everything" uninstall is wanted later, they live at
; %AppData%\MonitorWellness\.
