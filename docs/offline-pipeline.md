# Offline pipeline (Python / HTML)

The original script + browser flow. The C# app supersedes it for live use, but it shares the same JSON
formats (`schema/target.schema.json`, `schema/build.schema.json`) and is handy for offline runs,
reports, and as a cross-check of the C# port.

## See it work (no game, no DLL, no API key)

```powershell
python run_demo.py
```
Runs the whole pipeline on bundled sample data and prints a report (sample = a partially-complete Ball
Lightning Sorcerer at ~54%). It also generates `samples/sample_build.json` (gitignored), which the JS
test fixture needs. Then open `tracker/d4-tracker.html` → **Load bundled demo**.

## Compare a Maxroll build vs your capture

```powershell
# import any Maxroll build-guide or planner URL into the target side:
python parser\d4_maxroll_import.py "https://maxroll.gg/d4/build-guides/<build>-guide" --out target.json
#   (a build often has several profiles — pick one with --profile Endgame)

# one-step compare (gear + vision captured below):
python d4_compare.py --maxroll "https://maxroll.gg/d4/build-guides/<build>-guide" --gear gear.json --vision vision.json
```

The importer pulls gear affixes (resolved to readable names), uniques/mythics, active skills, and
paragon boards + glyphs from Maxroll's own data into a `target.json`. Verified end-to-end on a live
guide.

## Capture → merge → track

1. **Gear (TTS):** install a capture route ([capture.md](capture.md)), then either run the C# app, or
   the legacy `python parser\d4_gear_capture.py --once --equipped-only --out gear.json`.
2. **Vision (paragon / glyphs / skills / passives / aspects):** screenshot each paragon board, each
   socketed glyph (for its level), and the skill screen, then:
   ```powershell
   # needs ANTHROPIC_API_KEY:
   python d4_vision_capture.py --images board1.png board2.png glyphs.png skills.png --out vision.json
   # or offline, reuse the sample:
   python d4_vision_capture.py --stub ..\samples\sample_vision.json --out vision.json
   ```
3. **Merge + track:**
   ```powershell
   python d4_build_merge.py --gear gear.json --vision vision.json --out build.json
   ```
   Open `tracker/d4-tracker.html`, drop your **target** JSON and `build.json` on the two zones. Auto-diff
   shows ✓/✗ per requirement, per-category %, and overall %. Missing items get a persisted **manual
   override** checkbox. Terminal alternative: `node tracker\report.js target.json build.json`.

## Tests

```powershell
node tracker\diff.test.js   # JS matcher/diff (run `python run_demo.py` first — it generates the gitignored sample_build.json fixture)
```
The C# Core has its own, CI-enforced suite — see `csharp/README.md`.
