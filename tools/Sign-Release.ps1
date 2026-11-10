<#
.SYNOPSIS
  Authenticode-signs release artifacts (the published exe and the installer) so that
  tools/Verify-Release.ps1 passes and Windows SmartScreen has something to trust.

.DESCRIPTION
  Needs a code-signing certificate, which this project does not have yet, so nothing calls this
  script automatically. When one exists, run it between "build" and "verify":

    ./tools/Test-ReleasePackage.ps1 -CompileInstaller
    ./tools/Sign-Release.ps1 -Path artifacts/publish/MonitorWellness.exe, dist/MonitorWellness-Setup-0.2.6.exe -PfxPath cert.pfx
    ./tools/Verify-Release.ps1 -ApplicationPath ... -InstallerPath ... -ExpectedVersion 0.2.6

  The certificate comes from -PfxPath with -PfxPassword, or, for CI, from the environment
  variables SIGNING_PFX_BASE64 (the .pfx, base64-encoded) and SIGNING_PFX_PASSWORD. Both are
  secrets: never commit them or write them to logs.

  Signing the installer after the exe is inside it is fine: sign the exe first, rebuild the
  installer from the signed exe, then sign the installer. Release builds must be timestamped, so
  that the signature stays valid after the certificate expires; -NoTimestamp exists only for
  offline testing.

  Fails (non-zero exit) if any file ends up without a valid signature from the supplied
  certificate.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string[]]$Path,

    [string]$PfxPath,
    [string]$PfxPassword = $env:SIGNING_PFX_PASSWORD,
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [switch]$NoTimestamp,

    # For testing the script itself with a self-signed certificate, which Windows will never
    # call "Valid". Never use this for a real release.
    [switch]$AllowUntrusted
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-SigningCertificate {
    $flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::DefaultKeySet
    if ($PfxPath) {
        if (-not (Test-Path -LiteralPath $PfxPath -PathType Leaf)) { throw "PFX not found: $PfxPath" }
        return [System.Security.Cryptography.X509Certificates.X509Certificate2]::new((Resolve-Path -LiteralPath $PfxPath).Path, $PfxPassword, $flags)
    }
    if ($env:SIGNING_PFX_BASE64) {
        $bytes = [Convert]::FromBase64String($env:SIGNING_PFX_BASE64)
        return [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($bytes, $PfxPassword, $flags)
    }
    throw 'No certificate: pass -PfxPath, or set SIGNING_PFX_BASE64 (and SIGNING_PFX_PASSWORD).'
}

$certificate = Get-SigningCertificate
if (-not $certificate.HasPrivateKey) { throw 'The certificate has no private key, so it cannot sign.' }
if ($certificate.NotAfter -lt (Get-Date)) { throw "The certificate expired on $($certificate.NotAfter)." }

$failures = @()
foreach ($file in $Path) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "File not found: $file" }
    $resolved = (Resolve-Path -LiteralPath $file).Path

    $arguments = @{
        FilePath      = $resolved
        Certificate   = $certificate
        HashAlgorithm = 'SHA256'
    }
    if (-not $NoTimestamp) { $arguments.TimestampServer = $TimestampUrl }

    $result = Set-AuthenticodeSignature @arguments
    $signedByUs = $result.SignerCertificate -and $result.SignerCertificate.Thumbprint -eq $certificate.Thumbprint
    $acceptable = $result.Status -eq [System.Management.Automation.SignatureStatus]::Valid -or
                  ($AllowUntrusted -and $signedByUs)

    if ($acceptable) {
        Write-Host "Signed: $resolved ($($result.Status))"
    } else {
        $failures += "$resolved ($($result.Status): $($result.StatusMessage))"
    }
}

if ($failures.Count -gt 0) {
    throw "Signing failed for: $($failures -join '; ')"
}
