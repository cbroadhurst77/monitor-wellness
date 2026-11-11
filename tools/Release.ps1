<#
.SYNOPSIS
  Prepares a release locally: bumps the version everywhere, folds the changelog, builds and validates the package.

.DESCRIPTION
  Does everything up to the commit, and nothing outward-facing: it does not commit, tag or push.
  After it succeeds, review `git diff`, commit, merge to master, then tag and push:

      git tag v0.2.17
      git push origin master v0.2.17

  Pushing the tag makes CI build the installer from that exact commit and publish the GitHub release
  (notes taken from CHANGELOG.md, with SHA256SUMS). Use -SkipBuild to only bump files.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Title,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'ReleaseHelpers.ps1')

Push-Location $root
try {
    if (git status --porcelain) { throw 'The working tree has uncommitted changes. Commit or stash them first so the release diff is only the version bump.' }

    Set-ReleaseVersion -Root $root -Version $Version -Title $Title
    Write-Host "Version bumped to $Version; changelog folded."

    if (-not $SkipBuild) {
        dotnet test tests/MonitorWellness.Tests/MonitorWellness.Tests.csproj --configuration Release --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
        & (Join-Path $PSScriptRoot 'Test-ReleasePackage.ps1') -CompileInstaller
        if ($LASTEXITCODE -ne 0) { throw 'Release package validation failed.' }
    }

    Write-Host ''
    Write-Host "Ready. Review the diff, commit, then: git tag v$Version; git push origin master v$Version"
}
finally {
    Pop-Location
}
