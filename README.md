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
  dll/      saapi64.cpp          fake screen-reader DLL (logs D4's voiced text)
            build-and-install.ps1  compile (VS2022) + self-sign + install into the game
            uninstall.ps1        remove DLL + cert
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

### 1. Gear (TTS)
```powershell
cd D:\Projects\D4Scanner\dll
.\build-and-install.ps1            # add -Machine if D4 refuses to load the DLL
```
Then in D4: Accessibility → **Use Screen Reader** + **Use 3rd Party Screen Reader** ON; Gameplay →
**Advanced Tooltip Information** ON; Game Language **English**. Run the parser and hover each
equipped item:
```powershell
cd D:\Projects\D4Scanner\parser
python d4_gear_capture.py --follow --out gear.json
```

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

Enabling the accessibility settings is officially supported (zero risk). The shim reads no game
memory and injects nothing — it only receives text D4 hands to the OS accessibility layer, via a
log file. Screenshots/vision touch nothing in the game. The one ToS-gray step is placing a
self-signed `saapi64.dll` in the game folder; no documented bans exist for this tool class (d4lf
has used the same mechanism publicly for years), and `uninstall.ps1` reverses it. A lower-risk
alternative (run real NVDA + Speech Logger, no authored DLL) is in the research doc.

## Tests & maintenance

```powershell
node tracker\diff.test.js     # 25 assertions on the matcher + sample diff
```
Blizzard changes the voiced tooltip **format most seasons** (and broke DLL loading on the S12 PTR).
When the gear parser misreads, update the regexes in `parser/d4_gear_capture.py`; keep
`samples/sample_tts.log` current with a real captured block as a regression fixture.

## Status

- ✅ Gear parser — verified; hardened against real D4 menu/map noise + HTML entities
- ✅ TTS DLL — compiles + exports the 4 SAAPI fns; **confirmed loading in live D4** (routes real UI text to the log)
- ✅ Maxroll importer — **verified on a live Maxroll guide** (gear affixes, uniques, skills, boards, glyphs → target.json)
- ✅ Vision channel — stub verified; real Claude API path implemented (needs key + screenshots)
- ✅ Merge + compare wrapper — verified
- ✅ Diff engine — 25/25 tests pass
- ✅ Tracker UI — render + override verified (DOM-shim test): demo loads at 54%, override bumps %
- ✅ End-to-end: Maxroll target → diff vs live → per-category % (verified)
- ⏳ Real item-tooltip format — confirm/adjust gear regexes once you hover an equipped item in-game
- ⏳ Real vision call — implemented; run with your API key + screenshots
