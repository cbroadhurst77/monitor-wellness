<#
.SYNOPSIS
  Publishes the production win-x64 artifact with the release options and validates it.

.DESCRIPTION
  Used by CI (and runnable locally) so packaging problems surface before a release rather than
  at it. Needs no signing credentials. With -CompileInstaller it also compiles the Inno Setup
  script against the fresh publish output and checks the installer's name matches the version.

  Fails (non-zero exit) when inputs are missing or inconsistent:
    - csproj <Version> and installer MyAppVersion disagree
    - the published single-file exe is missing, empty, or has the wrong file version
    - the publish directory contains a stray .NET runtime requirement (runtimeconfig/deps
      next to the exe would mean it was not published self-contained single-file)
    - the installer was requested but ISCC.exe is absent or the expected setup exe is not produced
#>
[CmdletBinding()]
param(
    [string]$ProjectPath = 'src/MonitorWellness/MonitorWellness.csproj',
    [string]$InstallerScript = 'installer/MonitorWellness.iss',
    [string]$OutputDir = 'artifacts/publish',
    [switch]$CompileInstaller
)

$ErrorActionPreference = 'Stop'

function Fail([string]$Message) {
    Write-Error "Release validation failed: $Message"
    exit 1
}

if (-not (Test-Path $ProjectPath)) { Fail "project not found: $ProjectPath" }
if (-not (Test-Path $InstallerScript)) { Fail "installer script not found: $InstallerScript" }

[xml]$csproj = Get-Content $ProjectPath -Raw
$projectVersion = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
if (-not $projectVersion) { Fail "no <Version> in $ProjectPath" }

$iss = Get-Content $InstallerScript -Raw
if ($iss -notmatch '#define\s+MyAppVersion\s+"([^"]+)"') { Fail "MyAppVersion not found in $InstallerScript" }
$installerVersion = $Matches[1]
if ($installerVersion -ne $projectVersion) {
    Fail "version mismatch: csproj=$projectVersion installer=$installerVersion (bump installer/MonitorWellness.iss by hand)"
}

if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }

# Keep these options identical to the release instructions in README.md / the .iss header.
dotnet publish $ProjectPath -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $OutputDir --nologo
if ($LASTEXITCODE -ne 0) { Fail "dotnet publish exited with $LASTEXITCODE" }

$exe = Join-Path $OutputDir 'MonitorWellness.exe'
if (-not (Test-Path $exe)) { Fail "expected output missing: $exe" }
if ((Get-Item $exe).Length -lt 10MB) { Fail "$exe is implausibly small for a self-contained single-file publish" }

$fileVersion = (Get-Item $exe).VersionInfo.FileVersion
if (-not $fileVersion -or -not $fileVersion.StartsWith($projectVersion)) {
    Fail "exe file version '$fileVersion' does not match intended version '$projectVersion'"
}

foreach ($unexpected in 'MonitorWellness.runtimeconfig.json', 'MonitorWellness.deps.json') {
    if (Test-Path (Join-Path $OutputDir $unexpected)) {
        Fail "$unexpected present: output is not a self-contained single-file publish"
    }
}
Write-Host "Publish OK: $exe ($fileVersion)"

if ($CompileInstaller) {
    $iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if (-not $iscc) {
        $candidate = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
        if (Test-Path $candidate) { $iscc = Get-Item $candidate }
    }
    if (-not $iscc) { Fail 'ISCC.exe (Inno Setup 6) not found but -CompileInstaller was requested' }

    $publishFull = (Resolve-Path $OutputDir).Path
    $isccPath = if ($iscc -is [System.Management.Automation.ApplicationInfo]) { $iscc.Source } else { $iscc.FullName }
    & $isccPath "/DMyPublishDir=$publishFull" $InstallerScript
    if ($LASTEXITCODE -ne 0) { Fail "ISCC exited with $LASTEXITCODE" }

    $setup = Join-Path (Split-Path $InstallerScript -Parent) "..\dist\MonitorWellness-Setup-$projectVersion.exe"
    if (-not (Test-Path $setup)) { Fail "installer not produced: $setup" }
    Write-Host "Installer OK: $setup"
}
