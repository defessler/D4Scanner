# Capture (TTS gear channel)

How the app receives gear text from Diablo IV, how the in-app install works, the alternative routes,
risk, and per-season maintenance.

The mechanism: Diablo IV's **3rd-party screen reader** support hands item-tooltip text to the OS
accessibility layer. A tiny shim (`saapi64.dll`) receives that text and appends it to
`%LOCALAPPDATA%\d4scanner\d4_tts.log`, which the app tails and parses. No game memory is read and
nothing is injected.

Required in-game settings (any route): Accessibility → **Use Screen Reader** ON + **Use 3rd-Party
Screen Reader** ON; Gameplay → **Advanced Tooltip Information** ON; Game Language **English**.

## In-app install (what the button does)

`CaptureSetup.Install()` (`csharp/D4Scanner.App/CaptureSetup.cs`) — the shim DLL + its certificate are
**embedded in the exe** (csproj `<EmbeddedResource>`), so there's nothing to download:

1. Always extracts `saapi64.dll` + `d4scanner-tts.cer` to `%LOCALAPPDATA%\d4scanner\bin\`.
2. Trusts the cert into the **current-user Trusted Root** store.
3. Copies `saapi64.dll` into the **Diablo IV folder** (located via the registry / Steam / common paths,
   see `GameDir()`); if that write fails, it instead adds the `bin\` dir to the user **PATH**.
   The shim loads from the game folder, System32, or any PATH dir.

Refuses to run while Diablo IV is open (the DLL is locked). Manual-fallback steps for users are in the
root `README.md` ("If Install capture DLL doesn't work").

`GameDir()` install detection order: Uninstall registry (Battle.net) → Steam (`SteamPath` +
`libraryfolders.vdf`) → fixed paths → per-drive sweep. Covers Battle.net, Steam, and custom drives.

## Capture routes (dev / advanced)

Both write the same `d4_tts.log`. Tolk routes to NVDA when it's running, else to the SAAPI shim — so
switching is mostly about whether NVDA is running.

- **Route A — SAAPI shim (default).** The embedded signed `saapi64.dll`. Build from source:
  `cd dll ; .\build-and-install.ps1` (`-System32` if D4 restricts its DLL search; `-GameFolder` for the
  old in-folder behavior). To use this route: don't run NVDA. Remove with `dll\uninstall.ps1`.
- **Route B — NVDA (alternative; no forged DLL, no cert).** Genuine NVDA + a logging add-on:
  `.\setup-nvda.ps1`, install `d4scanner.nvda-addon` into NVDA, start NVDA before D4. Switch back to the
  shim by closing NVDA.

**Build prereqs (shim from source):** Visual Studio 2022 + "Desktop development with C++". The
`.github/workflows/release.yml` runner rebuilds + self-signs the shim fresh and embeds it in the exe,
so released builds always carry a current copy.

## Risk

Enabling the accessibility settings is officially supported (zero risk). Capture reads no game memory
and injects nothing — it only receives text D4 hands to the OS accessibility layer, via a log file.
Screenshots/vision touch nothing in the game.

- **SAAPI shim:** the one ToS-gray step is a self-signed `saapi64.dll`. No documented bans for this tool
  class (d4lf has used the same mechanism publicly for years). Reversible (DLL, PATH entry, cert).
- **NVDA route:** no forged DLL and no self-signed cert — just genuine NVDA + a logging add-on. Lowest
  footprint; nothing in the game folder.

The module/text path is the *sanctioned* 3rd-party-screen-reader behaviour D4 invites. **Re-validate per
season** — 3rd-party readers broke once on the S12 PTR.

## Per-season maintenance

Blizzard changes the **voiced tooltip format most seasons** (and once broke DLL loading on the S12 PTR).
When the parser misreads:

- Update the regexes in `csharp/D4Scanner.Core/GearParser.cs` (and the legacy `parser/d4_gear_capture.py`).
- Keep `samples/sample_tts.log` current with a real captured block — it's the regression fixture the C#
  test suite (`D4Scanner.Tests`, run by CI) asserts the parser against.
