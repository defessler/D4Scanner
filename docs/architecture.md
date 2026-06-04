# Architecture

How D4Scanner is put together. The **C# WPF app (`csharp/D4Scanner.App`) is the primary product**;
the Python/HTML pipeline (see [offline-pipeline.md](offline-pipeline.md)) shares the same JSON formats
and remains for the offline/report flow.

Key constraint (from `d4-character-capture-research.md`): there's no official D4 API and no working
scraper, so capture is **screen-based** and split into **two channels** — TTS for gear (exact text),
vision for the cursor-based screens.

## Data flow

```
  ┌─ GEAR channel (TTS) ─────────────┐
  │ saapi64.dll shim → d4_tts.log     │   gear + affixes + ranges +
  │ (parsed live by the app)          │   tempering count + masterwork
  └───────────────────────────────────┘
                                        ╲
  ┌─ VISION channel (Claude) ─────────┐  ╲   merge → DiffEngine → paper doll + "Do Next"
  │ screenshots → vision.json          │   ──►  target vs live, per-category %, guidance
  └───────────────────────────────────┘ ╱
    paragon boards · glyph levels · skills · key passives · aspects
```

- **Target** = the build you aim at (imported from a Maxroll build-guide/planner URL).
- **Live** = your character: gear from the TTS log, the rest from vision screenshots.
- The app tails `%LOCALAPPDATA%\d4scanner\d4_tts.log` and re-diffs continuously.

## What's captured by which channel

| Data | Channel | Notes |
|---|---|---|
| Item name/type/slot/rarity/power, affixes + ranges | TTS | reliable; needs Advanced Tooltips |
| Masterwork rank, temper **count** | TTS | temper = count only, not which affixes |
| Greater-affix flag | TTS | ⚠️ heuristic (no range → flagged); verify visually |
| Uniques / mythics | TTS | matched by name |
| Skills + ranks, key passives | Vision | from the skill-screen screenshot |
| Paragon boards, glyphs + **levels** | Vision | glyph level only visible on hover |
| Aspect names | Vision | from Codex / item tooltips |
| Legendary aspect on a rare/legendary, by name | — | hard; TTS gives effect text, not the name |

## Project layout

```
D4Scanner/
  csharp/   D4Scanner.App        LIVE WPF app — paper doll + "Do Next" (the product)
            D4Scanner.Core       parser + diff engine + guide + log watcher (headless-testable)
            D4Scanner.Cli        console verify/report (headless --watch)
            D4Scanner.Tests      dependency-free Core test console (run by CI)
            tools/IconIndexGen   regenerates the bundled handle→atlas icon map
  dll/      saapi64.cpp          SAAPI screen-reader shim source (logs D4's voiced text)
            build-and-install.ps1 / uninstall.ps1   build+sign+install / remove (dev)
  schema/   build.schema.json    unified live-build (capture output)
            target.schema.json   target build (what you aim at)
  parser/ tracker/ *.py *.js     legacy Python/HTML offline pipeline (see offline-pipeline.md)
  nvda-addon/ + setup-nvda.ps1   alternative capture route via genuine NVDA
  samples/  sample_*.{log,json}  fixtures (sample_tts.log is the parser regression fixture)
  .cache/   maxroll data.min.json + D4Companion affix map (downloaded by the importer)
  docs/     architecture.md · capture.md · offline-pipeline.md
```

The app reads/writes the same `target.schema.json` / `build.schema.json` shapes as the pipeline.
Maxroll quirks the importer handles: glyph "level" is a sentinel (compared on *socketed*, not level);
key passives and some legendary aspects aren't in Maxroll's data (manual override / vision).

## Where the real icons come from

Item icons are extracted from the user's **own local D4 install** (CASC + BCn decode), keyed by
Maxroll's image handle, with a bundled handle→atlas map. See the project memory
`d4scanner-game-icon-extraction` and `csharp/D4Scanner.Core/GameDataIcons.cs`.
