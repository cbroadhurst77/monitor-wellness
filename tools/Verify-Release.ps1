[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$ApplicationPath,

    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$InstallerPath,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$ExpectedVersion,

    [string]$ManifestPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-ReleaseArtifact([string]$Path, [string]$Kind) {
    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
    $signature = Get-AuthenticodeSignature -LiteralPath $resolvedPath
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "$Kind artifact is not Authenticode-valid: $resolvedPath ($($signature.Status))."
    }

    $version = (Get-Item -LiteralPath $resolvedPath).VersionInfo.ProductVersion
    if ([string]::IsNullOrWhiteSpace($version) -or -not $version.StartsWith($ExpectedVersion, [System.StringComparison]::Ordinal)) {
        throw "$Kind artifact version '$version' does not match expected version '$ExpectedVersion'."
    }

    [PSCustomObject]@{
        Kind = $Kind
        FileName = [System.IO.Path]::GetFileName($resolvedPath)
        Sha256 = (Get-FileHash -LiteralPath $resolvedPath -Algorithm SHA256).Hash
        Version = $version
        Signer = $signature.SignerCertificate.Subject
        TimestampUtc = (Get-Date).ToUniversalTime().ToString('O')
    }
}

$artifacts = @(
    Get-ReleaseArtifact -Path $ApplicationPath -Kind 'Application'
    Get-ReleaseArtifact -Path $InstallerPath -Kind 'Installer'
)

if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $artifacts | Format-Table -AutoSize
    return
}

$manifestDirectory = Split-Path -Path $ManifestPath -Parent
if (-not [string]::IsNullOrWhiteSpace($manifestDirectory)) {
    New-Item -ItemType Directory -Path $manifestDirectory -Force | Out-Null
}

$artifacts | ConvertTo-Json | Set-Content -LiteralPath $ManifestPath -Encoding utf8NoBOM
Write-Output "Release manifest written to $ManifestPath"
