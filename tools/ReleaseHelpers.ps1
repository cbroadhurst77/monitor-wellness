<#
.SYNOPSIS
  Functions shared by tools/Release.ps1 and the CI release job (dot-source this file).
  Kept free of git, network and build calls so tests can run them against a temporary copy of the repo.
#>

function Assert-ReleaseVersion([string]$Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3, not '$Version'." }
}

function Get-ProjectVersion([string]$Root) {
    $csproj = Get-Content (Join-Path $Root 'src/MonitorWellness/MonitorWellness.csproj') -Raw
    if ($csproj -notmatch '<Version>([^<]+)</Version>') { throw 'No <Version> in MonitorWellness.csproj.' }
    return $Matches[1]
}

function Update-TextFile([string]$Path, [scriptblock]$Transform) {
    # Preserves the file's existing line endings and encoding (no BOM is added).
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $text = [System.Text.UTF8Encoding]::new($false).GetString($bytes, $(if ($hasBom) { 3 } else { 0 }), $bytes.Length - $(if ($hasBom) { 3 } else { 0 }))
    $new = & $Transform $text
    [System.IO.File]::WriteAllText($Path, $new, [System.Text.UTF8Encoding]::new($hasBom))
}

<#
  Bumps the version in the project file and the installer script, and turns the "Unreleased" changelog
  section into a dated release heading (leaving a fresh empty "Unreleased" above it).
#>
function Set-ReleaseVersion([string]$Root, [string]$Version, [string]$Title) {
    Assert-ReleaseVersion $Version
    if (-not $Title) { throw 'A release title is required, e.g. "Your comfort plan is remembered".' }

    $current = Get-ProjectVersion $Root
    if ([version]$Version -le [version]$current) { throw "New version $Version must be higher than the current $current." }

    $changelog = Join-Path $Root 'CHANGELOG.md'
    $text = Get-Content $changelog -Raw
    if ($text -notmatch '(?ms)^## Unreleased\s*\r?\n(.*?)(?=^## )') { throw 'CHANGELOG.md has no "## Unreleased" section.' }
    if (-not $Matches[1].Trim()) { throw 'The Unreleased section of CHANGELOG.md is empty; there is nothing to release.' }

    Update-TextFile (Join-Path $Root 'src/MonitorWellness/MonitorWellness.csproj') {
        param($t) $t -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>"
    }
    Update-TextFile (Join-Path $Root 'installer/MonitorWellness.iss') {
        param($t) $t -replace '(#define\s+MyAppVersion\s+")[^"]+(")', "`${1}$Version`${2}"
    }
    Update-TextFile $changelog {
        param($t) [regex]::Replace($t, '(?m)^## Unreleased', "## Unreleased`r`n`r`n## $Version — $Title", 1)
    }
}

<# The changelog text for one release (without its heading), for the GitHub release notes. #>
function Get-ReleaseNotes([string]$ChangelogPath, [string]$Version) {
    $text = Get-Content $ChangelogPath -Raw
    $pattern = '(?ms)^## ' + [regex]::Escape($Version) + '\b[^\r\n]*\r?\n(.*?)(?=^## |\z)'
    if ($text -notmatch $pattern) { throw "CHANGELOG.md has no section for $Version." }
    $notes = $Matches[1].Trim()
    if (-not $notes) { throw "The CHANGELOG.md section for $Version is empty." }
    return $notes
}

<# Throws unless the tag (v1.2.3 or 1.2.3) matches the version the project and installer were built with. #>
function Assert-TagMatchesVersion([string]$Root, [string]$Tag) {
    $tagVersion = $Tag -replace '^v', ''
    Assert-ReleaseVersion $tagVersion
    $project = Get-ProjectVersion $Root
    if ($project -ne $tagVersion) { throw "Tag $Tag does not match the project version $project." }
    $iss = Get-Content (Join-Path $Root 'installer/MonitorWellness.iss') -Raw
    if ($iss -notmatch '#define\s+MyAppVersion\s+"([^"]+)"' -or $Matches[1] -ne $tagVersion) { throw "Tag $Tag does not match the installer version." }
}
