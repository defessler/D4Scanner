# D4Scanner Contributor Guide

D4Scanner is a Windows-only WPF desktop app (.NET 8, C#) that tracks a Diablo IV
character's equipped gear against a target build imported from Maxroll, then renders
it as a paper doll plus a prioritised "do this next" plan. It never reads game memory.
Gear arrives two ways: a signed TTS shim (`dll/saapi64.cpp`) that makes D4's screen
reader append tooltips to `%LOCALAPPDATA%\d4scanner\d4_tts.log`, and an optional local
`Windows.Media.Ocr` scan of the game window.

## Commands

There is no `.sln`. Every command targets a project directory. All of them run from
the repo root.

- Build - `dotnet build csharp/D4Scanner.App --no-incremental -c Release`. Verified:
  0 warnings, 0 errors. Treat any warning out of a `D4Scanner.*` project as a failure.
  CascLib's `SYSLIB0014` is already suppressed by
  `csharp/third_party/Directory.Build.props`, scoped to `third_party/` only.
- Test - `dotnet run --project csharp/D4Scanner.Tests`. Verified: 1029 assertions pass,
  exit 0. CI runs the same command with `-c Release`.
- Headless diff - `dotnet run --project csharp/D4Scanner.Cli -- --target <t.json>
  [--log <l>] [--watch] [--all] [--maxroll <url>] [--profile <n>] [--save <out.json>]
  [--live <live.json>]`. Switch names come from the arg loop in `Cli/Program.cs`.
- Publish locally - copy the exact command from the "Publish single-file self-contained
  exe" step of `.github/workflows/release.yml`. Leaving off
  `-p:IncludeNativeLibrariesForSelfExtract=true` gives you a different exe than ships.
- Release - `git tag v0.X.Y && git push origin v0.X.Y`. release.yml fires on any `v*`
  tag, rebuilds and self-signs the shim, publishes, and creates the GitHub release.

Bump `<Version>` in `csharp/D4Scanner.App/D4Scanner.App.csproj` before tagging. CI
overrides it with `-p:Version` from the tag. The csproj value only affects local
builds. Keeping the two in sync is still what everyone reads.

Seven project slash commands live in `.claude/commands/`: `/build`, `/test`, `/ship`,
`/parse-check`, `/diff-report`, `/add-setting`, and `/add-activity`. `/ship` is where
the release workflow is actually written down. `/parse-check` feeds raw TTS lines
through `GearParser` so you can see the parsed fields.

## How work lands

Changes go on a side branch and merge into `main` with a `Merge: <summary> (vX.Y.Z)`
commit. `CHANGELOG.md` is the release record and gets its own `docs:` commit. The root
`RELEASE_NOTES_v0.*.md` files stop at v0.81.0 and are abandoned. Please don't start a
new one. `AUDIT-2026-06.md` (5-lens, file:line grounded) and `TODO.md` are the live
backlog, worth checking before proposing work. `docs/contributing.md` lists the
files in the tree that will mislead you, `docs/architecture.md` first among them.

## Architecture

- `D4Scanner.Core` targets plain `net8.0` and has no WPF reference. Keep it that way.
  New parsing, diff, scoring, or guidance logic belongs there so the assertion console
  can test it headlessly.
- `MainWindow.xaml.cs` is 6,033 lines holding the whole UI by design. That's accepted
  here, not debt waiting to be split.
- `D4Scanner.Cli` is the headless verifier, `D4Scanner.Tests` the assertion console.
  `csharp/tools/{IconIndexGen,MakeIcon}` are one-off consoles. `csharp/third_party/CascLib`
  is vendored. Leave it alone.
- The real design rationale lives in `/// <summary>` blocks in Core, not in the .md
  files. `LogWatcher.ClassifyContext`, `LiveGearResolver`, `Tombstones`, `PanelOracle`,
  `UpgradePath`, `LogWatcher.LatestPerSlot`, and `CaptureSetup.InstalledShimVersion`
  each record which live bug forced their shape. `ClassifyContext` is the most
  bug-forced of them. Its summary carries a bias note you'll want before you touch it.
  Read the summary before changing the method.
- `Core/AppPaths.cs` is the single source of truth for on-disk locations
  (`%LOCALAPPDATA%\d4scanner` and its `cache`). Go through it rather than hand-rolling
  `Environment.GetFolderPath`.
- Two palettes exist. `Theme.xaml` x:Keys (Bg, Ink, Soft, Edge, Green, Red, Accent,
  Amber, Surface, Primary, plus rarity keys) drive `{StaticResource}` in
  `MainWindow.xaml`. The `static readonly Brush` fields at `MainWindow.xaml.cs:18-32`
  drive everything built in code. `Card` and `Gold` exist only in the C# set. Almost
  all UI is code-built, which makes those fields the ones you usually want.

Per-file map of `D4Scanner.Core` and the TTS/OCR capture-channel walkthrough:
`docs/contributing.md`. Short version: no Vision LLM and no Anthropic API anywhere
under `csharp/`, and `LiveGearResolver.Merge` lets Tts win over Ocr per slot.

### Where a common change goes

- Parse a new TTS field - `GearParser.cs`, a regex plus a branch in `ParseBlock`
  (`:291`).
- Add an item type or slot - `GearParser.TypeSlot`, `Rarities`, `LooksLikeItem`. Read
  the charm gotcha below first.
- Change diff scoring - `DiffEngine.ScoreSlot` (`:154`), with `AffixMet` (`:337`) and
  `WeaponTypeMatch` (`:354`) alongside it.
- Add a "Do Next" verb - `BuildGuide.Steps()` (`:26`).
- Add a recommended activity - `Activities.Recommend()` (`:18`). The copy usually
  belongs in `Assets/season_pack.json` rather than in code.
- Add a settings key - four sites, listed in the Settings gotchas below.
- Add a test assertion - `csharp/D4Scanner.Tests/Program.cs`, under a
  `// ---- Section ----` banner.

## Gotchas

Each of these cost someone a debugging session.

- `ClassifyContext` treats a bare standalone `EQUIPPED` voice line as no evidence at
  all that an item is worn (`Core/LogWatcher.cs:373-378`). D4 voices that line
  immediately before the name of nearly every comparison-enabled bag, stash, or vendor
  hover, because it labels the comparison overlay rather than the hovered item. Worn
  needs a slot header with a positive Character panel, the Character panel itself, an
  `Unequip` action tail, or the `PanelOracle` rescue. With nothing corroborating, the
  default is not-equipped. A missed genuine item self-corrects on the next
  character-panel hover. A vendor item stamped as worn silently replaces real gear and
  persists, which is the v0.37 leak the v0.38 rewrite fixed. Items whose tooltip ends at
  a poll-chunk edge wait in `LogWatcher._pending` (`:36-42`, `:258-280`) until the
  action tail arrives, or until a couple of quiet polls force the safe default.
- Bare item-type words (Helm, Ring, Boots) are deliberately absent from `PanelMarkers`.
  The Purveyor of Curiosities voices exactly those as gamble categories, which used to
  flip the panel Vendor to Character and stamp vendor hovers as worn. That's the other
  half of the same v0.38 fix. Pinned by a regression test at
  `csharp/D4Scanner.Tests/Program.cs:3069` onward.
- Settings are defer-everything since v0.43.0. Every control in `ShowSettings()`
  (`MainWindow.xaml.cs:2384`) edits a `SettingsDraft`. Nothing applies until Save.
  Revert re-seeds the draft from live state. X, backdrop, and Esc all discard it. A
  new draftable field also has to be registered in `NewDraft()` at `:2375`, whose
  comment records that a missed site already left Revert stale once.
- Persistence is a separate contract from that draft. Values live in `app.json` under
  `%LOCALAPPDATA%\d4scanner` as a flat `Dictionary<string, string>` serialized to JSON.
  Bools go in as `"1"` or `"0"`. Doubles go through `CultureInfo.InvariantCulture` on
  both the write and the parse, which keeps a comma-decimal locale from corrupting
  `zoom` or `winW`. So a new setting is four edits: a backing field, a `TryGetValue`
  read in `LoadSettings` (`:505-546`), a key in the `SaveSettings` dictionary (`:565`),
  and a `NewDraft()` entry if it's user-editable.
- `SaveSettings` writes `SettingsPath + ".tmp"` then `File.Move(..., overwrite: true)`
  (`:566-568`). That's atomic on purpose. Don't reduce it to a direct `WriteAllText`.
- No text clipping anywhere in the UI. That's an app-wide rule from v0.8.2. The tree
  still honours it: `TextTrimming` appears nowhere under `csharp/`. Use
  `TextWrapping.Wrap` and let the height grow. Reduce the font size when something has
  to fit a fixed space. Don't truncate. A bare `MaxHeight` outside a deliberate
  scroll container counts as clipping too. The last literal violation was the status
  bar, fixed in v0.45.0 (`AUDIT-2026-06.md`, item C3).
- `Render()` (`:1478`) sets `_hoverPopup.IsOpen = false` first, ahead of every early
  return. A background TTS or OCR update mid-hover otherwise strands the popup pinned
  to a detached cell. `ShowSettings()` carries the mirror constraint at `:2386`, since
  the hover card is a transparent always-on-top Popup HWND that would float over the
  modal and swallow the close click.
- `LatestPerSlot`'s tiebreak is recency, never alphabet. `Core/LogWatcher.cs:516`
  records the live failure: with an alphabetical tiebreak, one misclassified
  "Adventurer's ..." owned a one-cap slot for a whole session because the genuine item,
  re-hovered later, still lost the A-vs-C compare. Per-slot caps are ring 2, weapon 4,
  charm 6, everything else 1.
- Charms are detected by the anchored `ReCharmType` regex, not a `TypeSlot` key.
  `Core/GearParser.cs:27` and `:110` explain why: Horadric Cube lines like "1x Set
  Charm" would otherwise manufacture a phantom charm out of a crafting panel. Re-adding
  "Set Charm" to `TypeSlot` reopens that bug. `seal`, `charm`, and `rune` all pass
  `LooksLikeItem` without an ItemPower line.
- `PanelOracle.PanelAt` is fail-closed. A non-positive tick, or anything outside the
  ~25s tolerance, returns null and never the most recent panel. One oracle per
  `StartWatching()` call, shared by that call's TTS and OCR watchers, never static, so
  a stale `Observe` from a disposed engine can't reach the new watcher.
- The TTS watcher doesn't replay the whole log by default. Once any character profile
  exists it starts at `LogWatcher.LastSessionStartPos(_log)`
  (`MainWindow.xaml.cs:858`), because logs reach 18MB and a full replay measured about
  4s of parse. `_replayFromZero` and `_logSkipToPos` are the overrides.
- `LogWatcher` clears all accumulated gear on a `=== d4scanner tts shim attached`
  marker, on the live path and in `BuildFromLines`. A prior session's loadout can't
  linger on the HAVE side that way.
- `CaptureSetup.ShimPaths()` is ordered game dir, System32, PATH bin dir, matching D4's
  own load order. `InstalledShimVersion()` reports the first copy it finds. Reordering
  that list silently changes which shim the upgrade banner sees. When the PE byte-scan
  for `SA_GetVersion` succeeds but `LoadLibraryExW` fails, it returns
  `CurrentShimVersion` rather than 0, which would re-trigger the banner falsely.
- `dll/saapi64.dll` and `dll/d4scanner-tts.cer` are committed binaries and load-bearing.
  The App csproj embeds them as resources. release.yml falls back to `git checkout --`
  on both when the runner-side rebuild fails.
- `.gitignore` has a blanket `*.log` with one `!samples/*.log` escape. A new TTS fixture
  placed anywhere but `samples/` is silently untracked. CI then passes against a fixture
  that isn't in the repo.
- A Transfigured item is a terminal state. Temper, enchant, masterwork, socket, and
  imprint are all impossible there. `UpgradePath.ForSlot` returns one LOCKED note
  rather than an empty list, because empty reads as "already perfect".
- Rare must not equal the amber UI accent `#D4A730`. `RRare` is `#ECE07C` so that loot
  reads apart from chrome (`MainWindow.xaml.cs:34`).

## Conventions, as measured

No `.editorconfig` and no linter, so these come from the tree itself.

- Indentation - 4 spaces, zero tabs. `rg '^\t' -g '*.cs' csharp/` returns nothing.
- Line endings - LF, on a Windows checkout with no `.gitattributes`. Keep new files LF.
- Naming - PascalCase for types and members, leading-underscore camelCase for private
  instance fields (`_watcher`, `_pending`, `_settingsDraft`).
- Line length - no cap, and the long tail is deliberate. Median 51, p95 121, max 4,353.
  `MainWindow.xaml.cs:565` packs the entire settings dictionary onto one line on
  purpose. Aim under roughly 120 for new code. Don't reflow existing long lines.
- All four projects set `Nullable`, `ImplicitUsings`, and `LangVersion latest`. Only
  `D4Scanner.App` sets `AllowUnsafeBlocks` and the `net8.0-windows10.0.19041.0` TFM,
  both required by the D3D/WGC capture path. `IsBorderRequired` is Win11-only and gets
  set through reflection so that SDK 19041 TFM still compiles.
- Tests are top-level statements with two local helpers, `Check(name, cond)` and
  `Eq<T>(name, expected, actual)`. No xUnit, no NUnit, no attributes. Append new
  assertions to the same file under a `// ---- Section ----` banner.
