#Requires -Version 5.1
<#
.SYNOPSIS
  Build, self-sign, trust, and install the D4Scanner TTS shim (saapi64.dll).

.DESCRIPTION
  Compiles saapi64.cpp with the MSVC toolchain (located via vswhere), creates a
  self-signed code-signing certificate, trusts it, signs the DLL (Diablo IV will
  not load an unsigned DLL), and copies it into the game folder.

  By default the cert is trusted in the CurrentUser root store (no admin needed).
  If Diablo IV still refuses to load the DLL, re-run elevated with -Machine.

.EXAMPLE
  .\build-and-install.ps1
.EXAMPLE
  .\build-and-install.ps1 -GamePath "D:\Games\Blizzard\Diablo IV"
.EXAMPLE
  .\build-and-install.ps1 -NoInstall        # build + sign only
#>
[CmdletBinding()]
param(
    [string]$GamePath = "D:\Games\Blizzard\Diablo IV",
    [string]$PathDir  = "$env:LOCALAPPDATA\d4scanner\bin",  # default install: a user PATH dir (NO game-folder file)
    [switch]$GameFolder, # install INTO the Diablo IV folder instead (old behavior)
    [switch]$System32,   # install into C:\Windows\System32 (most robust vs search-path hardening; needs admin)
    [switch]$Machine,    # trust cert in LocalMachine\Root (elevation) instead of CurrentUser\Root
    [switch]$NoInstall   # build + sign only; do not install anywhere
)

$ErrorActionPreference = 'Stop'
$here        = Split-Path -Parent $MyInvocation.MyCommand.Path
$src         = Join-Path $here 'saapi64.cpp'
$dll         = Join-Path $here 'saapi64.dll'
$certSubject = 'CN=D4Scanner TTS Shim'

Write-Host "[1/5] Locating MSVC toolchain..." -ForegroundColor Cyan
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) {
    throw "vswhere not found. Install Visual Studio 2022 with the 'Desktop development with C++' workload."
}
$vsPath = & $vswhere -latest -products * `
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vsPath) { $vsPath = & $vswhere -latest -property installationPath }
$devcmd = Join-Path $vsPath 'Common7\Tools\VsDevCmd.bat'
if (-not (Test-Path $devcmd)) { throw "VsDevCmd.bat not found under '$vsPath'." }

Write-Host "[2/5] Compiling saapi64.dll (x64)..." -ForegroundColor Cyan
Push-Location $here
try {
    if (Test-Path $dll) { Remove-Item $dll -Force }
    $cl = "call `"$devcmd`" -arch=x64 -host_arch=x64 >nul 2>&1 && " +
          "cl /nologo /O2 /EHsc /LD /DUNICODE /D_UNICODE `"$src`" /Fe:`"$dll`""
    cmd /c $cl
    if (-not (Test-Path $dll)) { throw "Compile failed — saapi64.dll was not produced." }

    $dump = (cmd /c "call `"$devcmd`" -arch=x64 >nul 2>&1 && dumpbin /exports `"$dll`"") | Out-String
    foreach ($fn in 'SA_SayW', 'SA_BrlShowTextW', 'SA_StopAudio', 'SA_IsRunning') {
        if ($dump -notmatch [regex]::Escape($fn)) { throw "Export '$fn' missing from the DLL." }
    }
    Write-Host "      exports OK: SA_SayW, SA_BrlShowTextW, SA_StopAudio, SA_IsRunning"
    Remove-Item (Join-Path $here '*.obj'), (Join-Path $here '*.exp'),
                (Join-Path $here '*.lib') -Force -ErrorAction SilentlyContinue
}
finally { Pop-Location }

Write-Host "[3/5] Creating / trusting code-signing cert..." -ForegroundColor Cyan
if ($Machine) {
    $admin = ([Security.Principal.WindowsPrincipal] `
        [Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)
    if (-not $admin) { throw "-Machine requires an elevated (Run as Administrator) PowerShell." }
}
$cert = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Subject -eq $certSubject } | Select-Object -First 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $certSubject `
        -CertStoreLocation Cert:\CurrentUser\My -KeyUsage DigitalSignature `
        -KeyExportPolicy Exportable -NotAfter ((Get-Date).AddYears(5))
}
$rootStore = if ($Machine) { 'Cert:\LocalMachine\Root' } else { 'Cert:\CurrentUser\Root' }
$cer = Join-Path $here 'd4scanner-tts.cer'
Export-Certificate -Cert $cert -FilePath $cer -Force | Out-Null
Import-Certificate -FilePath $cer -CertStoreLocation $rootStore | Out-Null
Write-Host "      trusted in $rootStore"

Write-Host "[4/5] Signing the DLL..." -ForegroundColor Cyan
$sig = Set-AuthenticodeSignature -FilePath $dll -Certificate $cert -HashAlgorithm SHA256
if ($sig.Status -ne 'Valid') {
    throw "Signing failed: $($sig.Status) — $($sig.StatusMessage)"
}
Write-Host "      signature: Valid"

if ($NoInstall) {
    Write-Host "Done (build + sign only). DLL at: $dll" -ForegroundColor Green
    return
}

Write-Host "[5/5] Installing the shim..." -ForegroundColor Cyan

# Choose install location. Default = a user PATH dir, so NOTHING goes in the game folder.
if ($System32)      { $destDir = "$env:WINDIR\System32"; $mode = "System32" }
elseif ($GameFolder) {
    if (-not (Test-Path (Join-Path $GamePath 'Diablo IV.exe'))) {
        throw "'Diablo IV.exe' not found in '$GamePath'. Pass -GamePath '<your D4 folder>'."
    }
    $destDir = $GamePath; $mode = "GameFolder"
}
else { $destDir = $PathDir; $mode = "PathDir" }
$target = Join-Path $destDir 'saapi64.dll'

# A running Diablo IV holds the loaded saapi64.dll open, so the copy would fail with a lock.
if (Get-Process -Name 'Diablo IV', 'Diablo IV Launcher' -ErrorAction SilentlyContinue) {
    throw "Diablo IV is running, which locks saapi64.dll. Fully quit Diablo IV (and the Battle.net launcher if needed), then re-run. The signed DLL is ready at '$dll'."
}

New-Item -ItemType Directory -Force -Path $destDir | Out-Null
Copy-Item $dll $target -Force
Write-Host "      installed: $target  ($mode)" -ForegroundColor Green

# The game folder is searched BEFORE System32/PATH, so any copy there would shadow ours.
if ($mode -ne 'GameFolder') {
    $shadow = Join-Path $GamePath 'saapi64.dll'
    if (Test-Path $shadow) {
        Remove-Item $shadow -Force -ErrorAction SilentlyContinue
        Write-Host "      removed shadowing copy from the game folder: $shadow" -ForegroundColor Yellow
    }
}
if ($mode -eq 'PathDir') {
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    if (($userPath -split ';') -notcontains $destDir) {
        [Environment]::SetEnvironmentVariable('Path', ($userPath.TrimEnd(';') + ';' + $destDir), 'User')
        Write-Host "      added to your USER PATH (no admin): $destDir" -ForegroundColor Green
        Write-Host "      IMPORTANT: launch Diablo IV from a NEW login/session so it inherits the new PATH." -ForegroundColor Yellow
    } else { Write-Host "      already on your USER PATH: $destDir" }
}

Write-Host ""
Write-Host "NEXT STEPS:" -ForegroundColor Yellow
Write-Host "  1) In Diablo IV > Settings:"
Write-Host "       Accessibility > 'Use Screen Reader' = ON"
Write-Host "       Accessibility > 'Use 3rd Party Screen Reader' = ON"
Write-Host "       Gameplay > 'Advanced Tooltip Information' = ON  (Game Language = English)"
Write-Host "  2) Launch Diablo IV, hover an equipped item, and confirm capture is working:"
Write-Host "       Get-Content `"$env:LOCALAPPDATA\d4scanner\d4_tts.log`" -Tail 5"
Write-Host "     You should see '=== d4scanner tts shim attached ==='. If the log stays EMPTY"
Write-Host "     after hovering (PathDir mode only), D4 restricted its DLL search path —"
Write-Host "     re-run with -System32 (admin) which is immune to that." -ForegroundColor DarkGray
Write-Host "  3) Open the live app:  dotnet run --project ..\csharp\D4Scanner.App"
Write-Host ""
Write-Host "  To remove everything later: .\uninstall.ps1" -ForegroundColor DarkGray
