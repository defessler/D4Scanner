#Requires -Version 5.1
<#
.SYNOPSIS
  Set up the NVDA capture route — no file in the Diablo IV folder, no custom signed DLL.

.DESCRIPTION
  1) Ensures a per-user PATH dir exists (so D4 can load NVDA's controller client from it).
  2) Finds NVDA's genuine nvdaControllerClient64.dll and copies it there (D4/Tolk loads it by
     bare name off the search path -> routes D4's screen-reader text to NVDA).
  3) Builds the D4Scanner NVDA add-on (.nvda-addon) which logs spoken text to the same
     d4_tts.log the app already reads.
  Nothing is written to the Diablo IV install folder.
#>
[CmdletBinding()]
param(
    [string]$PathDir = "$env:LOCALAPPDATA\d4scanner\bin"
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "[1/3] Ensuring PATH dir + user PATH..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $PathDir | Out-Null
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if (($userPath -split ';') -notcontains $PathDir) {
    [Environment]::SetEnvironmentVariable('Path', ($userPath.TrimEnd(';') + ';' + $PathDir), 'User')
    Write-Host "      added to USER PATH: $PathDir" -ForegroundColor Green
} else { Write-Host "      already on USER PATH: $PathDir" }

Write-Host "[2/3] Locating NVDA's nvdaControllerClient64.dll..." -ForegroundColor Cyan
$searchRoots = @("${env:ProgramFiles(x86)}\NVDA", "$env:ProgramFiles\NVDA", "$env:ProgramData\NVDA") |
    Where-Object { Test-Path $_ }
$dll = $null
foreach ($root in $searchRoots) {
    $dll = Get-ChildItem -Path $root -Filter 'nvdaControllerClient64.dll' -Recurse -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($dll) { break }
}
if ($dll) {
    Copy-Item $dll.FullName (Join-Path $PathDir 'nvdaControllerClient64.dll') -Force
    Write-Host "      copied $($dll.FullName)" -ForegroundColor Green
    Write-Host "         -> $PathDir\nvdaControllerClient64.dll"
} else {
    Write-Host "      nvdaControllerClient64.dll NOT found in an NVDA install." -ForegroundColor Yellow
    Write-Host "      Install NVDA (https://www.nvaccess.org/download/), or download" -ForegroundColor Yellow
    Write-Host "      controllerClient.zip from https://download.nvaccess.org/releases/stable/" -ForegroundColor Yellow
    Write-Host "      and copy x64\nvdaControllerClient64.dll into: $PathDir" -ForegroundColor Yellow
}

Write-Host "[3/3] Building the NVDA add-on (d4scanner.nvda-addon)..." -ForegroundColor Cyan
$addonSrc = Join-Path $here 'nvda-addon'
$out = Join-Path $here 'd4scanner.nvda-addon'
if (Test-Path $out) { Remove-Item $out -Force }
# stage only the needed files (never __pycache__/*.pyc) so the package is clean
$stage = Join-Path $env:TEMP 'd4scanner-addon-stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'globalPlugins') | Out-Null
Copy-Item (Join-Path $addonSrc 'manifest.ini') $stage -Force
Copy-Item (Join-Path $addonSrc 'globalPlugins\d4scanner.py') (Join-Path $stage 'globalPlugins') -Force
$zip = Join-Path $env:TEMP 'd4scanner-addon.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
Move-Item $zip $out -Force
Remove-Item $stage -Recurse -Force
Write-Host "      built: $out" -ForegroundColor Green

Write-Host ""
Write-Host "NEXT STEPS (no Diablo IV folder touched):" -ForegroundColor Yellow
Write-Host "  1) Install NVDA if you haven't:  https://www.nvaccess.org/download/"
Write-Host "  2) Double-click  d4scanner.nvda-addon  to install it into NVDA (NVDA will prompt)."
Write-Host "  3) (Optional, to stay silent) NVDA menu > Preferences > Settings > Speech >"
Write-Host "     Synthesizer > 'No speech'.  Text is still logged."
Write-Host "  4) START NVDA, then launch Diablo IV (so D4 inherits the PATH + finds NVDA first)."
Write-Host "  5) In D4: Accessibility > Use Screen Reader ON + Use 3rd Party Screen Reader ON;"
Write-Host "     Gameplay > Advanced Tooltip Information ON; Language English."
Write-Host "  6) Hover an item, then verify:"
Write-Host "       Get-Content `"$env:LOCALAPPDATA\d4scanner\d4_tts.log`" -Tail 5"
Write-Host "  7) Open the live app:  dotnet run --project $here\csharp\D4Scanner.App"
Write-Host ""
Write-Host "  This route uses genuine NVDA — no forged DLL, no self-signed cert, nothing in the game folder." -ForegroundColor DarkGray
Write-Host "  If you previously installed the saapi64 shim, remove it:  dll\uninstall.ps1" -ForegroundColor DarkGray
