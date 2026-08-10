[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ProjectPath
)

$ErrorActionPreference = 'Stop'

# `dotnet list package --vulnerable` reports vulnerable dependencies but does not fail its
# process. Consume its JSON output so CI fails closed when NuGet has an advisory to report.
[string]$packageJson = & dotnet list $ProjectPath package --vulnerable --include-transitive --format json
if ($LASTEXITCODE -ne 0) {
    throw "dotnet list package failed with exit code $LASTEXITCODE."
}

$report = $packageJson | ConvertFrom-Json
$findings = [System.Collections.Generic.List[object]]::new()

function Find-Vulnerability {
    param([object]$Value)

    if ($null -eq $Value -or $Value -is [string]) {
        return
    }

    if ($Value -is [System.Collections.IEnumerable]) {
        foreach ($item in $Value) {
            Find-Vulnerability $item
        }
        return
    }

    $properties = $Value.PSObject.Properties
    $vulnerabilityProperty = $properties['vulnerabilities']
    if ($null -ne $vulnerabilityProperty) {
        foreach ($vulnerability in @($vulnerabilityProperty.Value)) {
            if ($null -ne $vulnerability) {
                $findings.Add([PSCustomObject]@{
                    Package = $properties['id'].Value
                    Version = $properties['resolvedVersion'].Value
                    Severity = $vulnerability.severity
                    Advisory = $vulnerability.advisoryurl
                })
            }
        }
    }

    foreach ($property in $properties) {
        if ($property.Name -ne 'vulnerabilities') {
            Find-Vulnerability $property.Value
        }
    }
}

Find-Vulnerability $report

if ($findings.Count -gt 0) {
    $findings | Format-Table -AutoSize | Out-String | Write-Error
    throw "Found $($findings.Count) vulnerable NuGet package advisory/advisories."
}

Write-Host 'No vulnerable NuGet packages reported.'
