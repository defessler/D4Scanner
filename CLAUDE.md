# CLAUDE.md — D4Scanner contributor guide

D4Scanner is a live Diablo IV build tracker: a WPF desktop app (.NET 8, C#) that reads
the game's screen-reader (TTS) output and optionally OCRs the game window, comparing the
player's equipped gear against a target build imported from Maxroll.

## Project-specific slash commands

These live in `.claude/commands/` and are available in any Claude Code session in this repo:

| Command | What it does |
|---|---|
| `/build` | Build the app (`--no-incremental -c Release`) and report errors / unexpected warnings |
| `/test` | Run the Core test suite (~624 assertions); report pass/fail and any failing names |
| `/ship` | Full release workflow: build → test → bump version → commit → tag → push → CI → release notes |
| `/parse-check` | Feed raw TTS tooltip lines through GearParser and show the parsed Item fields |
| `/diff-report` | Run the CLI diff against the current target + live log without launching the WPF app |
| `/add-setting` | Step-by-step guide to adding a new persisted setting (field + load + save + UI toggle) |
| `/add-activity` | Step-by-step guide to adding a new recommended activity to the guidance system |

## Build & test — run these after every change

```powershell
# Build (must be error-free before shipping)
dotnet build csharp/D4Scanner.App --no-incremental -c Release

# Tests (~624 assertions; must all pass)
dotnet run --project csharp/D4Scanner.Tests

# Publish local exe (use to smoke-test before a release commit)
dotnet publish csharp/D4Scanner.App -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true \
  -p:PublishReadyToRun=true -o publish
```

The build should be **warning-free**. The vendored CascLib emits `SYSLIB0014`
(obsolete `WebRequest`); that's suppressed via `csharp/third_party/Directory.Build.props`
(scoped to `third_party/` only, so our own code still flags the obsolete API). If you see
any warning from `D4Scanner.*`, fix it — don't suppress it.

Releases are built and published automatically by GitHub Actions on every `v*` tag push:
```powershell
git tag v0.X.Y && git push origin v0.X.Y   # CI does the rest
```
Always bump `<Version>` in `csharp/D4Scanner.App/D4Scanner.App.csproj` before tagging.

## Repository layout

```
csharp/
  D4Scanner.Core/         business logic (no UI — fully headless-testable)
    Models.cs               Item, Affix, LiveBuild, TargetBuild, UiContext, ItemSource
    GearParser.cs           TTS log lines → Item objects; also ParseTooltipLines() for OCR
    LogWatcher.cs           tails d4_tts.log; emits LiveBuild; Diagnose() capture-health report
    LiveGearResolver.cs     live-gear merge (Tts>Ocr) + inventory dedup/merge + weapon de-dup
    Tombstones.cs           account-wide name|slot record of items the player cleared (resurrect-proof)
    DiffEngine.cs           HAVE-vs-NEED per slot, value-aware scoring; Greater-Affix helpers
    UpgradeScorer.cs        scores owned items vs the build (IsUpgrade/RawUpgrade/GA/salvage)
    Verdicts.cs             per-item verdict: Equip/Fixable/KeepSalvage/KeepDupe/Stash/Junk + action
    UpgradePath.cs          per-slot ordered crafting plan (temper→enchant→masterwork→socket→imprint)
    BuildGuide.cs           prioritised "Do Next" steps (FIND/GET/IMPROVE/TEMPER/…)
    Substitutes.cs          best-owned stand-ins + Now/Better/Best ladder
    Activities.cs           farming/crafting recommendations (reads SeasonPack)
    InfernalHordesAdvisor.cs  Hordes-spoil heuristic based on build gaps (reads SeasonPack)
    SeasonPack.cs           season-volatile guidance data (Assets/season_pack.json + local override)
    LootFilter.cs           export: markdown checklist + D4Companion JSON
    GameDataIcons.cs        real item icons from the local D4 install (CASC + BCn decode)
    IconResolver.cs         multi-source icon resolver (game-data → GitHub CDN → silhouette)
    MaxrollImporter.cs      Maxroll planner URL → TargetBuild
    Updater.cs              GitHub release check + in-place staged-update mechanism
  D4Scanner.App/            WPF front-end
    MainWindow.xaml(.cs)    the entire UI (paper doll, detail panel, modals, settings)
    Theme.xaml              palette + control styles (colour names: Bg, Card, Ink, Gold, …)
    App.xaml(.cs)           startup; applies staged update before the window opens
    CaptureSetup.cs         TTS shim install/uninstall (saapi64.dll + cert + PATH)
    Capture/
      WindowsGraphicsCapture.cs  WGC frame grab (exclusive fullscreen + borderless)
      OcrCaptureEngine.cs        periodic OCR scan → LiveBuild; also auto-saves portrait
  D4Scanner.Cli/            console verify/watch (headless)
  D4Scanner.Tests/          dependency-free assertion console (run with dotnet run)
dll/                        saapi64.cpp — the TTS shim source + build scripts
```

## Architecture decisions

### Two capture channels — TTS and OCR, independently toggled
- **TTS** (`LogWatcher`): reads `%LOCALAPPDATA%\d4scanner\d4_tts.log`, stamped
  `ItemSource.Tts`. Most accurate (exact text); requires the DLL shim + D4 accessibility settings.
- **OCR** (`OcrCaptureEngine`): grabs the game window via `WindowsGraphicsCapture`
  (falls back to `PrintWindow`), runs `Windows.Media.Ocr`, feeds tooltip blocks through
  `GearParser.ParseTooltipLines`. No DLL, no API key, no network. Stamped `ItemSource.Ocr`.
- **Merge rule** in `MergeGear()`: Tts wins over Ocr per slot-basename. TTS data for a
  slot is never replaced by OCR data for the same slot.
- **No Vision LLM / no Anthropic API** — the app is fully offline for all capture.
  `VisionCapture.cs` was deleted in v0.8.0; do not re-add it.

### Core is UI-free
`D4Scanner.Core` has no WPF dependency. Keep it that way — all UI lives in `D4Scanner.App`.
New parsing, diff, or guidance logic goes in Core so it can be tested headlessly.

### Data flow
```
d4_tts.log → LogWatcher.Feed → Item (Source=Tts) → Updated event
game window → OcrCaptureEngine.ScanCoreAsync → Item (Source=Ocr) → Updated event
                     ↓
         MainWindow.OnLiveUpdate → MergeGear → _live (LiveBuild) → Render()
```

### Settings persistence
`app.json` in `%LOCALAPPDATA%\d4scanner\`. Flat `Dictionary<string, string>` serialized
as JSON. Booleans use `"1"` / `"0"`; doubles use `InvariantCulture`. Pattern to add a
new persisted setting (copy the `_debugMode` pattern in `LoadSettings` / `SaveSettings`):
```csharp
// LoadSettings: add
if (s.TryGetValue("myKey", out var v)) _myField = v == "1";

// SaveSettings: add to the dict
["myKey"] = _myField ? "1" : "0"
```

### Adding a Settings toggle (UI pattern)
Copy the debug-toggle block in `ShowSettings()` (~line 1410). It's a `DockPanel` with a
`CheckBox` (docked left) + a `StackPanel` of `TBs(title)` / `TB(description)`. The
`Checked`/`Unchecked` handlers set the field, call `SaveSettings()`, then `Render()` or
`StartWatching()` as appropriate.

### Render cycle
`Render()` rebuilds the entire `Body` StackPanel from scratch on every call. It is
idempotent and cheap enough to call after any state change. Do not cache partial renders.

### Text display rules
- **Never clip text with `TextTrimming.CharacterEllipsis` or `MaxHeight`** unless the
  element is inside a deliberate scroll container or has specific design treatment.
- Use `TextWrapping = TextWrapping.Wrap` and let cell height expand naturally.
- Reduce font size if something needs to fit a fixed space; never truncate.

### Colours
Use the named brushes from `Theme.xaml` wherever possible (`Gold`, `Card`, `Ink`, `Soft`,
`Faint`, `Edge`, `Green`, `Red`, …). The palette is **cool dark near-black with selective
amber accent** — do not add warm brown or heavy gold fills to large areas.

## Key files for common tasks

| Task | File |
|---|---|
| Parse a new item field from TTS | `GearParser.cs` — add regex + `ParseBlock` branch |
| Add a new affix / slot type | `GearParser.cs` — `TypeSlot`, `Rarities`, `LooksLikeItem` |
| Change diff scoring | `DiffEngine.cs` — `ScoreSlot`, `AffixMet`, `WeaponTypeMatch` |
| Add a "Do Next" step verb | `BuildGuide.cs` — `Steps()` |
| Add a recommended activity | `Activities.cs` — `Recommend()` |
| Add a UI panel / modal | `MainWindow.xaml.cs` — follow `ToggleHelp()` or `ShowSettings()` pattern |
| Add a settings key | `LoadSettings()` + `SaveSettings()` + a backing field |
| Add a test assertion | `csharp/D4Scanner.Tests/Program.cs` — add `Check()` / `Eq()` calls |

## Season-specific notes

- **Game-mechanics ground truth lives in `docs/d4-gearing-knowledge.md`** (researched June 2026,
  Season 13 / Lord of Hatred expansion). Read it before touching guidance logic (`BuildGuide`,
  `Activities`, `InfernalHordesAdvisor`, `Substitutes`, scoring) — its §8 gap table lists which
  encoded assumptions are stale, and its tripwire list (IP 750/800 caps, Torment I–IV, 12-rank
  masterworking, 2 temper slots, fixed unique affixes, …) flags pre-2026 data that must not be
  (re-)introduced.
- Item types `seal`/`charm`/`rune` in `GearParser.TypeSlot` were added for Season 8 but still
  apply: seals/charms are now the permanent **Talisman** system's items (Lord of Hatred), runes
  are permanent since Vessel of Hatred. These pass `LooksLikeItem` without an ItemPower line.
  The Item Quality score (`ReQuality`, `"50 +50/25 Quality"` / `"50 (+30/25) Quality"`) is the
  masterworking **Quality 0–25** system (Season 11 rework).
- TTS format changes each season — the `GearParser` test against `sample_tts.log` is the
  primary regression net. Update the fixture when the live format changes.

## Gotchas

- `LogWatcher.Poll` and `BuildFromFile` clear accumulated gear on a `=== d4scanner tts shim
  attached` marker so a prior session's loadout doesn't linger on the HAVE side after a relaunch.
- `LatestPerSlot` deduplicates by `(name, SlotPosition)` for character-panel items and by
  `name` only for bag hovers. Multi-ring / multi-weapon slots require `SlotPosition > 0`.
- `StartWatching()` is idempotent — it disposes and recreates all watchers. Re-call it
  when any capture setting changes (same pattern as `PickLog()`).
- The D3D/WGC capture path requires `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in
  `D4Scanner.App.csproj` and `net8.0-windows10.0.19041.0` as the TFM. `IsBorderRequired`
  is Win11-only — it's set via reflection so the SDK 19041 TFM compiles.
- `InstalledShimVersion()` in `CaptureSetup` byte-scans the PE for `"SA_GetVersion"`.
  If the scan succeeds but `LoadLibraryExW` fails, it returns `CurrentShimVersion` to
  avoid a false upgrade prompt.
- When updating the version: bump `<Version>` in `csharp/D4Scanner.App/D4Scanner.App.csproj`,
  commit, tag, push. CI injects the tag into the published assembly.
