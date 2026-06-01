#Requires -Version 5.1
<#
.SYNOPSIS
  Authenticode-sign the published D4Scanner.exe.

.DESCRIPTION
  With -Pfx, signs using your real code-signing certificate (this is what actually
  matters for distribution). Without -Pfx, falls back to a self-signed cert.

  IMPORTANT: a self-signed cert does NOT remove the Windows SmartScreen "unrecognized
  app" warning — that requires an OV/EV code-signing certificate from a CA (e.g.
  DigiCert/Sectigo) that builds reputation. Self-signing only sets a publisher name.
  For the GitHub release build, add CODESIGN_PFX_BASE64 / CODESIGN_PFX_PASSWORD secrets
  and the release workflow signs automatically.

.EXAMPLE
  .\sign.ps1 -Pfx C:\certs\mycert.pfx -Password 'secret'
.EXAMPLE
  .\sign.ps1     # self-signed fallback
#>
[CmdletBinding()]
param(
    [string]$Exe = "$PSScriptRoot\..\csharp\D4Scanner.App\bin\Release\net8.0-windows\win-x64\publish\D4Scanner.exe",
    [string]$Pfx,
    [string]$Password,
    [string]$TimestampServer = 'http://timestamp.digicert.com'
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Exe)) { throw "exe not found: $Exe  (publish it first: dotnet publish …)" }

if ($Pfx) {
    if (-not (Test-Path $Pfx)) { throw "pfx not found: $Pfx" }
    $cert = if ($Password) {
        [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($Pfx, $Password)
    } else { Get-PfxCertificate -FilePath $Pfx }
    Write-Host "Signing with certificate: $($cert.Subject)"
} else {
    $subject = 'CN=D4Scanner Self-Signed'
    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $subject } | Select-Object -First 1
    if (-not $cert) {
        $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject `
            -CertStoreLocation Cert:\CurrentUser\My -NotAfter ((Get-Date).AddYears(5))
    }
    Write-Host "Using a SELF-SIGNED cert — this sets a publisher name but does NOT remove" -ForegroundColor Yellow
    Write-Host "the SmartScreen warning (that needs an OV/EV cert from a CA)." -ForegroundColor Yellow
}

$sig = Set-AuthenticodeSignature -FilePath $Exe -Certificate $cert -HashAlgorithm SHA256 -TimestampServer $TimestampServer
Write-Host "signature: $($sig.Status)  ($($sig.StatusMessage))"
if ($sig.Status -ne 'Valid') { exit 1 }
