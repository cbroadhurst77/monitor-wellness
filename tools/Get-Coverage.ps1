<#
.SYNOPSIS
  Runs the tests with coverage and prints a short summary; fails below -Minimum percent line coverage.
  In CI the summary also goes to the job summary page.
#>
[CmdletBinding()]
param(
    [string]$TestProject = 'tests/MonitorWellness.Tests/MonitorWellness.Tests.csproj',
    [string]$ResultsDir = 'artifacts/coverage',
    [double]$Minimum = 50,
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
if (Test-Path $ResultsDir) { Remove-Item $ResultsDir -Recurse -Force }

$args = @('test', $TestProject, '--configuration', 'Release', '--nologo', '--collect:XPlat Code Coverage', '--results-directory', $ResultsDir)
if ($NoRestore) { $args += '--no-restore' }
dotnet @args
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

$report = Get-ChildItem $ResultsDir -Recurse -Filter coverage.cobertura.xml | Select-Object -First 1
if (-not $report) { throw 'No coverage report was produced.' }

[xml]$xml = Get-Content $report.FullName
$lineRate = [double]$xml.coverage.'line-rate' * 100
$files = foreach ($class in $xml.SelectNodes('//class')) {
    $lines = @($class.lines.line)
    if ($lines.Count -eq 0) { continue }
    [pscustomobject]@{ File = $class.filename; Lines = $lines.Count; Hit = @($lines | Where-Object { [int]$_.hits -gt 0 }).Count }
}
$byFile = $files | Group-Object File | ForEach-Object {
    $lines = ($_.Group | Measure-Object Lines -Sum).Sum
    $hit = ($_.Group | Measure-Object Hit -Sum).Sum
    [pscustomobject]@{ File = $_.Name; Lines = $lines; Percent = [math]::Round(100.0 * $hit / $lines, 1) }
}
$weakest = $byFile | Where-Object { $_.Lines -ge 40 } | Sort-Object Percent | Select-Object -First 8

$summary = @("### Line coverage: $([math]::Round($lineRate, 1))% (minimum $Minimum%)", '', 'Least covered files (40+ lines):', '', '| File | Lines | Covered |', '|---|---:|---:|')
$summary += $weakest | ForEach-Object { "| $($_.File) | $($_.Lines) | $($_.Percent)% |" }
$summary | Write-Host
if ($env:GITHUB_STEP_SUMMARY) { $summary | Add-Content $env:GITHUB_STEP_SUMMARY }

if ($lineRate -lt $Minimum) { throw "Line coverage $([math]::Round($lineRate, 1))% is below the minimum $Minimum%." }
