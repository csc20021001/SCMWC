# Signs the built binaries with an Authenticode certificate.
#
# Keep this file pure ASCII: PowerShell 5.1 reads BOM-less .ps1 as system ANSI.
#
# Usage:
#   .\sign.ps1 -Thumbprint <cert thumbprint in CurrentUser\My>
#   .\sign.ps1 -PfxPath C:\path\cert.pfx -PfxPassword (Read-Host -AsSecureString)
#   .\sign.ps1 -Thumbprint <tp> -WhatIfSelfSigned      # pipeline smoke test only
#
# Timestamping is mandatory, not optional: without it every signature stops
# validating the day the certificate expires. With it, signatures stay valid
# for the life of the timestamp authority's certificate.

[CmdletBinding()]
param(
    [string]$Thumbprint,
    [string]$PfxPath,
    [System.Security.SecureString]$PfxPassword,
    [string]$TimestampServer = 'http://timestamp.digicert.com',
    [string[]]$Files
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

if (-not $Files -or $Files.Count -eq 0) {
    # Order matters: the inner exes must be signed BEFORE the installer is built,
    # because the installer embeds them as resources. Run build.ps1 in between.
    $Files = @(
        (Join-Path $root 'bin\ScreenWatch.exe'),
        (Join-Path $root 'bin\ProgressDemo.exe'),
        (Join-Path $root 'dist\ScreenWatch-Setup.exe')
    )
}

# --- pick the certificate -------------------------------------------------
$cert = $null
if ($PfxPath) {
    if (-not (Test-Path $PfxPath)) { throw "PFX not found: $PfxPath" }
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 `
        $PfxPath, $PfxPassword, 'Exportable,PersistKeySet'
} elseif ($Thumbprint) {
    $tp = $Thumbprint -replace '\s', ''
    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Thumbprint -eq $tp }
    if (-not $cert) { $cert = Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Thumbprint -eq $tp } }
    if (-not $cert) { throw "No certificate with thumbprint $tp in CurrentUser\My or LocalMachine\My" }
} else {
    throw 'Pass -Thumbprint or -PfxPath. Run: Get-ChildItem Cert:\CurrentUser\My | Format-List Subject,Thumbprint,NotAfter'
}

"certificate : $($cert.Subject)"
"thumbprint  : $($cert.Thumbprint)"
"expires     : $($cert.NotAfter)"
if ($cert.NotAfter -lt (Get-Date)) { throw 'Certificate has expired.' }
""

# --- sign -----------------------------------------------------------------
foreach ($f in $Files) {
    if (-not (Test-Path $f)) { "skip (missing) : $f"; continue }

    $r = Set-AuthenticodeSignature -FilePath $f -Certificate $cert `
        -HashAlgorithm SHA256 -TimestampServer $TimestampServer `
        -IncludeChain NotRoot

    $name = Split-Path $f -Leaf
    if ($r.Status -eq 'Valid') {
        "signed  {0,-24} {1}" -f $name, $r.StatusMessage
    } else {
        # UnknownError with a self-signed test cert is expected: the chain is
        # not trusted. Anything else on a real cert means the signature is bad.
        "WARNING {0,-24} status={1} : {2}" -f $name, $r.Status, $r.StatusMessage
    }
}

""
"--- verification ---"
foreach ($f in $Files) {
    if (-not (Test-Path $f)) { continue }
    $sig = Get-AuthenticodeSignature -FilePath $f
    $stamp = if ($sig.TimeStamperCertificate) { $sig.TimeStamperCertificate.Subject } else { 'NOT TIMESTAMPED' }
    "{0,-24} {1,-14} {2}" -f (Split-Path $f -Leaf), $sig.Status, $stamp
}
