# D4Scanner

Capture your **live Diablo IV character** and track it against a **target build** — what's
done, what's missing, and your overall % completion. PC, no game-memory access, low ban risk.

Built from the research in `..\Diablo IV\Diablo IV\d4-character-capture-research.md`. The key
finding: there's no official D4 API and no working scraper, so capture is **screen-based** and
**split into two channels** — TTS for gear (exact text), vision for the cursor-based screens.

## See it work in 10 seconds (no game, no DLL, no API key)

```powershell
cd D:\Projects\D4Scanner
python run_demo.py
```

Runs the whole pipeline on bundled sample data and prints a report (sample = a partially-complete
Ball Lightning Sorcerer at **54%**). Then open `tracker\d4-tracker.html` and click **Load bundled
demo** to see the same thing in the UI.

## How it fits together

```
  ┌─ GEAR channel (TTS) ─────────────┐
  │ saapi64.dll shim → d4_tts.log     │   gear + affixes + ranges +
  │ d4_gear_capture.py  → gear.json   │   tempering count + masterwork
  └───────────────────────────────────┘
                                        ╲
  ┌─ VISION channel (Claude) ─────────┐  ╲   d4_build_merge.py        diff.js
  │ screenshots → d4_vision_capture.py │   ──►  build.json   ──►  d4-tracker.html
  │              → vision.json         │  ╱      (unified)        (target vs live, %)
  └───────────────────────────────────┘ ╱
    paragon boards · glyph levels · skills · key passives · aspects
```

`build.json` follows `schema/build.schema.json`; the target follows `schema/target.schema.json`.

```
D4Scanner/
  run_demo.py                 end-to-end demo on sample data (offline)
  d4_compare.py               one step: Maxroll URL + your capture -> report + tracker
  schema/   build.schema.json    unified live-build (capture output)
            target.schema.json   target build (what you aim at)
  csharp/   D4Scanner.App        LIVE WPF app — watches the log, shows per-slot have-vs-need (use this)
            D4Scanner.Core/Cli   parser + diff engine + log watcher (+ CLI)
  dll/      saapi64.cpp          SAAPI screen-reader shim (logs D4's voiced text)
            build-and-install.ps1  build + self-sign + install to a PATH dir (OFF the game folder)
            uninstall.ps1        remove the shim (all locations) + cert
  nvda-addon/ + setup-nvda.ps1   alternative capture route via genuine NVDA (no forged DLL)
  parser/   d4_gear_capture.py   TTS log → gear JSON          (verified)
            d4_vision_capture.py screenshots → skills/paragon/aspects (Claude API or --stub)
            d4_maxroll_import.py  Maxroll build URL → target.json  (verified on live Maxroll)
            d4_build_merge.py    gear + vision → unified build.json
  tracker/  diff.js              shared diff engine (browser + Node)
            d4-tracker.html      the tracker UI (load target + live, auto-diff, %)
            report.js            terminal report (node report.js target.json build.json)
            diff.test.js         25 unit tests (node diff.test.js)
  samples/  sample_*.{log,json}, necro_target.json (real Maxroll import), necro_live_demo.json
  .cache/   maxroll data.min.json + D4Companion affix map (downloaded by the importer)
```

## Compare a Maxroll build vs your in-game build (the whole point)

```powershell
# import any Maxroll build-guide or planner URL straight into the target side:
python parser\d4_maxroll_import.py "https://maxroll.gg/d4/build-guides/<build>-guide" --out target.json
#   (a build often has several profiles — pick one with --profile Endgame)

# then capture your live build (gear + vision, below) and compare in one step:
python d4_compare.py --maxroll "https://maxroll.gg/d4/build-guides/<build>-guide" --gear gear.json --vision vision.json
```

The importer pulls gear affixes (resolved to readable names), uniques/mythics, active skills, and
paragon boards + glyphs from Maxroll's own data, and writes a `target.json` the tracker diffs against
your live capture. Verified end-to-end on a live Maxroll guide (target → diff → per-category %).
Notes: Maxroll's internal glyph "level" is a sentinel, so glyphs are compared on *socketed* (not level);
key passives and some legendary aspects aren't in Maxroll's data and fall back to manual override.

## Real capture (your character)

**Prereqs:** Visual Studio 2022 + "Desktop development with C++" (detected at install), Python 3, Node (optional, for the terminal report). D4 at `D:\Games\Blizzard\Diablo IV`.

### 1. Gear (TTS) — two capture routes, **pick one**  ·  *current default: SAAPI shim*

Both routes write the same `%LOCALAPPDATA%\d4scanner\d4_tts.log`. They're mutually exclusive at
runtime: Tolk routes to NVDA when it's running, otherwise to the System Access (SAAPI) shim — so
switching is mostly about whether NVDA is running.

**Route A — SAAPI shim (default / current).** Simplest; no extra software. Installs a signed
`saapi64.dll` to a user PATH dir — **nothing in the Diablo IV folder, no admin**:
```powershell
cd D:\Projects\D4Scanner\dll
.\build-and-install.ps1     # -System32 if D4 restricts its DLL search path; -GameFolder for the old behavior
```
To use this route: just **don't run NVDA**.

**Route B — NVDA (alternative; no forged DLL, no cert).** Genuine NVDA + a logging add-on:
```powershell
cd D:\Projects\D4Scanner
.\setup-nvda.ps1            # then install d4scanner.nvda-addon into NVDA, and start NVDA BEFORE D4
```
Switch back to the shim anytime by **closing NVDA**. (`dll\uninstall.ps1` removes the shim entirely
if you want to commit to the NVDA route.)

Then in D4: Accessibility → **Use Screen Reader** + **Use 3rd Party Screen Reader** ON; Gameplay →
**Advanced Tooltip Information** ON; Game Language **English**.

**The live front-end is the C# app** — it tails the log and shows the diff continuously:
```powershell
dotnet run --project D:\Projects\D4Scanner\csharp\D4Scanner.App
```
(Or the old script flow: `python parser\d4_gear_capture.py --once --equipped-only --out gear.json`.)

### 2. Vision (paragon / glyphs / skills / passives / aspects)
Screenshot each paragon board, each socketed glyph (for its level), and the skill screen. Then:
```powershell
# needs ANTHROPIC_API_KEY:
python d4_vision_capture.py --images board1.png board2.png glyphs.png skills.png --out vision.json
# or offline, reuse the sample so the pipeline still runs:
python d4_vision_capture.py --stub ..\samples\sample_vision.json --out vision.json
```

### 3. Merge + track
```powershell
python d4_build_merge.py --gear gear.json --vision vision.json --out build.json
```
Open `tracker\d4-tracker.html`, drop your **target** JSON and `build.json` on the two zones.
Auto-diff shows ✓/✗ per requirement, per-category %, and overall %. Missing items get a **manual
override** checkbox (persisted) for anything the scanner can't see yet (e.g. weapon if you didn't
hover it). Terminal alternative: `node tracker\report.js target.json build.json`.

### Remove everything
```powershell
cd D:\Projects\D4Scanner\dll ; .\uninstall.ps1
```

## What's captured by which channel

| Data | Channel | Notes |
|---|---|---|
| Item name/type/slot/rarity/power, affixes + ranges | TTS | reliable; needs Advanced Tooltips |
| Masterwork rank, temper **count** | TTS | parsed (temper = count only, not which affixes) |
| Greater-affix flag | TTS | ⚠️ heuristic (no range → flagged); noisy, verify visually |
| Uniques / mythics | TTS | matched by name |
| Skills + ranks, key passives | Vision | from skill-screen screenshot |
| Paragon boards, glyphs + **levels** | Vision | glyph level only visible on hover |
| Aspect names | Vision | from Codex / item tooltips |
| Legendary aspect on a rare/legendary, by name | — | hard; TTS gives effect text not the name |

## Risk

Enabling the accessibility settings is officially supported (zero risk). Capture reads no game
memory and injects nothing — it only receives text D4 hands to the OS accessibility layer, via a
log file. Screenshots/vision touch nothing in the game.

- **SAAPI shim (default):** the one ToS-gray step is a self-signed `saapi64.dll`, now installed to a
  **user PATH dir — not the Diablo IV folder**. No documented bans for this tool class (d4lf has used
  the same mechanism publicly for years). `dll\uninstall.ps1` reverses it (DLL, PATH entry, cert).
- **NVDA route:** no forged DLL and no self-signed cert at all — just genuine NVDA + a logging add-on.
  Lowest footprint; nothing in the game folder.

Either way the module/text path is the *sanctioned* 3rd-party-screen-reader behaviour D4 invites; the
folder doesn't change the (low) exposure. Re-validate per season — 3rd-party readers broke once on the
S12 PTR.

## Tests & maintenance

```powershell
node tracker\diff.test.js                      # 25 assertions on the matcher + sample diff (JS)
dotnet run --project csharp\D4Scanner.Tests    # 31 assertions on the C# Core (diff + Do-Next guide)
```
Blizzard changes the voiced tooltip **format most seasons** (and broke DLL loading on the S12 PTR).
When the gear parser misreads, update the regexes in `parser/d4_gear_capture.py`; keep
`samples/sample_tts.log` current with a real captured block as a regression fixture.

## Status

- ✅ **Live C# app** (`csharp/D4Scanner.App`) — **the primary product** (see `csharp/README.md`). A WPF
  window that tails the log and shows a Diablo-IV-style **paper doll** beside an impact-ordered **"Do
  Next"** plan: real item icons from your own install, in-app Maxroll import, one-click capture-DLL
  install, vision capture, hover/pin compare with best-owned **substitutes**, **recommended activities**,
  **loot-filter export**, keyboard shortcuts + zoom, and a first-run setup checklist. Per-affix value
  thresholds flag under-rolled affixes (header slider). Shipped as a self-contained **single-file .exe**
  on each `v*` tag — https://github.com/defessler/D4Scanner/releases. (The Python/HTML pipeline below
  still works for the offline/report flow.)
- ✅ Gear parser — verified on **real live D4 gear**; hardened against menu/map noise + entities; equipped-only
- ✅ Capture — two routes, both **off the game folder**: SAAPI shim (default, confirmed loading in live D4)
  and the NVDA add-on (alternative, no forged DLL)
- ✅ Maxroll importer — **verified on a live guide** (affixes, uniques, skills, boards, glyphs → target.json)
- ✅ Diff engine — value-aware + thresholds; 25/25 tests pass (C# port output-verified identical)
- ✅ Vision channel — stub verified; real Claude API path implemented (needs key + screenshots)
- ⏳ Real vision call — implemented; run with your API key + screenshots
- ⏳ Validate the chosen capture route on the current season (3rd-party readers broke once on the S12 PTR)
