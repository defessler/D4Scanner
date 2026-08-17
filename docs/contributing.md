# Contributing: the Core map and the misleading files

Reference material moved out of `CLAUDE.md` on 2026-08-16 to keep the standing
load small. `CLAUDE.md` keeps the commands, the architecture rules, the gotchas,
and the measured conventions. This page holds the parts you look up rather than
need on every turn. If a claim here stops being true, fix it here.

## What the Core files are for

Most of the interesting code lives in `D4Scanner.Core`. Several of these filenames
don't give away what's inside.

- `Models.cs` - `Item`, `Affix`, `LiveBuild`, `TargetBuild`, `UiContext`, `ItemSource`.
- `GearParser.cs` - TTS log lines to `Item` objects, plus `ParseTooltipLines` for the
  OCR blocks.
- `LogWatcher.cs` - tails `d4_tts.log`, classifies every hover, emits a `LiveBuild`.
- `LiveGearResolver.cs` - folds a fresh capture batch into persisted live state.
- `PanelOracle.cs` - the OCR to TTS panel handoff (v0.79.0).
- `Tombstones.cs` - account-wide `name|slot` record of items the player cleared from
  the All Items list. Without it a per-item delete resurrects on the next poll, because
  the watcher re-emits its whole accumulated inventory and the merge reads that as truth.
- `DiffEngine.cs` - HAVE vs NEED per slot, with value-aware scoring.
- `UpgradeScorer.cs` - scores an owned, non-equipped item against the target build.
- `Verdicts.cs` - the one-word answer for an owned item (Equip, Fixable, KeepSalvage,
  KeepDupe, Stash, Junk) plus the reason and the concrete next action.
- `UpgradePath.cs` - the ordered per-slot crafting plan: temper, enchant, masterwork,
  socket, imprint.
- `BuildGuide.cs` - the prioritised "Do Next" steps, ordered by impact.
- `Substitutes.cs` - core vs flexible affixes, the best stand-in you already own, and
  the Now / Better / Best ladder.
- `Activities.cs` and `InfernalHordesAdvisor.cs` - farming and Hordes-spoil
  recommendations. Both read `SeasonPack`.
- `SeasonPack.cs` - the season-volatile guidance data, `Assets/season_pack.json` with a
  user-local override. A season update should be a data edit, not a code change.
- `LootFilter.cs` - export as a markdown checklist plus a D4Companion-shaped affix
  preset JSON.
- `GameDataIcons.cs` - real item icons pulled out of the local D4 install, CASC read
  plus BCn decode in pure managed C#.
- `IconResolver.cs` - the icon ladder: local game data first, then a configured
  template source, then the slot silhouette.
- `MaxrollImporter.cs` - planner URL to `TargetBuild`.
- `Updater.cs` - GitHub release check, staged download, applied by a rename on the next
  launch.
- `LogStore.cs` - log rotation, retention pruning, and the session index behind "load a
  previous session".
- `GearList.cs` - the build-agnostic All Items table, with by-affix filtering and a
  stable per-item fingerprint.
- `TalismanView.cs` - the captured seals, charms, and runes. Maxroll exports no talisman
  targets, which makes this one purely "what do I own".

## The two capture channels

- TTS (`LogWatcher`) tails `d4_tts.log` and stamps `ItemSource.Tts`. Exact text, so
  it's the accurate channel. Needs the shim plus D4's screen-reader setting.
- OCR (`OcrCaptureEngine`) grabs the window via `WindowsGraphicsCapture` (falling back
  to `PrintWindow`), runs `Windows.Media.Ocr`, and feeds blocks to
  `GearParser.ParseTooltipLines`. Stamped `ItemSource.Ocr`.
- `LiveGearResolver.Merge` folds a fresh batch into persisted live state. Tts wins over
  Ocr per slot base name, which means TTS data for a slot is never replaced by OCR data
  for that same slot. Its doc comment asks you not to "tidy" the comparisons, because
  the casing is already normalised.
- `PanelOracle` (v0.79.0) is the fusion point. OCR writes which panel it saw open, and
  `LogWatcher.ClassifyContext` reads that to rescue a worn item whose Character marker
  aged out of the rolling window.
- No Vision LLM and no Anthropic API anywhere under `csharp/`. `VisionCapture.cs` was
  deleted in v0.8.0. Re-adding that path would break the offline guarantee.

## Things in the tree that will mislead you

- `docs/architecture.md` is the doc a newcomer opens first. It's also the one most
  likely to mislead. Its data-flow diagram still draws a live "VISION channel (Claude)"
  feeding paragon boards, glyph levels, skills, key passives, and aspects. The what's-captured
  table below it still routes those same rows to Vision. That channel died with
  `VisionCapture.cs` in v0.8.0. For the accurate picture read `docs/capture.md` on the
  TTS channel and `docs/ocr-tts-fusion-research.md` on the OCR side.
- `README.md:26` still says the Paragon/Skills feature needs an `ANTHROPIC_API_KEY`.
  `parser/d4_vision_capture.py` still ships. Both contradict the C# app, which has no
  Anthropic path. If you reconcile them, fix the README rather than restore an API
  path.
- `README.md` and `csharp/README.md` disagree about where the shim installs.
  `CaptureSetup.cs` does both, a `bin` dir under `%LOCALAPPDATA%\d4scanner` plus a
  game-folder copy, with a System32 lookup between them.
- `docs/d4-gearing-knowledge.md` is the game-mechanics ground truth (researched
  2026-06-10, Season 13 "Season of Reckoning", Lord of Hatred, patch 3.0.3). Read it
  before touching `BuildGuide`, `Activities`, `InfernalHordesAdvisor`, `Substitutes`,
  or scoring. Its §8 gap table lists what the app still gets wrong. Its tripwire list
  flags pre-2026 data that must not come back (IP 750/800 caps, Torment I to IV,
  12-rank masterworking, 2 temper slots, fixed unique affixes). Line 273 sets its own
  expiry, "Re-verify at S14 (~June 30)". At time of writing it's past that date.
- Three TTS fixtures are tracked and wired into `D4Scanner.Tests.csproj`:
  `samples/sample_tts.log`, `samples/sample_tts_rogue_s8.log`, and
  `samples/sample_tts_s13.log`. The S13 one is the current-season regression net. The
  TTS format shifts each season.
- `parser/`, `tracker/`, `schema/`, `nvda-addon/`, `run_demo.py`, and `d4_compare.py`
  are a dormant legacy Python/JS pipeline. `tracker/diff.test.js` is intentionally
  excluded from CI because it needs `samples/sample_build.json`, which `run_demo.py`
  generates and `.gitignore` excludes. It can't run on a clean checkout. That makes it
  neither broken nor worth fixing.
