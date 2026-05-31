#Requires -Version 5.1
<#
.SYNOPSIS
  Remove the D4Scanner TTS shim (from every install location) and its self-signed cert.

.DESCRIPTION
  Deletes saapi64.dll from the user PATH dir, the game folder, and System32; removes the
  PATH entry; restores any backed-up original; and removes the 'D4Scanner TTS Shim' cert.
  Use -Machine if you trusted the cert in LocalMachine (requires elevation).
#>
[CmdletBinding()]
param(
    [string]$GamePath = "D:\Games\Blizzard\Diablo IV",
    [string]$PathDir  = "$env:LOCALAPPDATA\d4scanner\bin",
    [switch]$Machine
)

$ErrorActionPreference = 'Stop'
$certSubject = 'CN=D4Scanner TTS Shim'

# 1) remove the DLL from every location it may have been installed to
$locations = @(
    (Join-Path $PathDir 'saapi64.dll'),
    (Join-Path $GamePath 'saapi64.dll'),
    (Join-Path "$env:WINDIR\System32" 'saapi64.dll')
)
foreach ($t in $locations) {
    if (Test-Path $t) {
        try {
            Remove-Item $t -Force
            Write-Host "removed $t"
            $bak = "$t.d4scanner-bak"
            if (Test-Path $bak) { Move-Item $bak $t -Force; Write-Host "  restored previous saapi64.dll from backup" }
        } catch { Write-Host "could not remove $t ($($_.Exception.Message)) — is Diablo IV running?" -ForegroundColor Yellow }
    }
}

# 2) remove our dir from the USER PATH
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if ($userPath -and ($userPath -split ';') -contains $PathDir) {
    $new = ($userPath -split ';' | Where-Object { $_ -and $_ -ne $PathDir }) -join ';'
    [Environment]::SetEnvironmentVariable('Path', $new, 'User')
    Write-Host "removed $PathDir from your USER PATH"
}

# 3) remove the self-signed cert
$stores = @('Cert:\CurrentUser\My', 'Cert:\CurrentUser\Root')
if ($Machine) { $stores += 'Cert:\LocalMachine\Root' }
foreach ($s in $stores) {
    Get-ChildItem $s -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -eq $certSubject } |
        ForEach-Object {
            Remove-Item $_.PSPath -Force -ErrorAction SilentlyContinue
            Write-Host "removed cert from $s"
        }
}
Write-Host "done."
