# D4Scanner — C# live build tracker

A pure C# (.NET 8 / WPF) desktop app that **watches the screen-reader log live** and shows your
equipped gear vs a target build — per slot, with your rolled values — updating automatically as you
hover items in game. No more re-running scripts: open it once and just look at it.

```
csharp/
  D4Scanner.Core/   parser + diff engine + log watcher (no UI; headless-testable)
    Models.cs         data types (Item, Affix, TargetBuild, DiffReport, …)
    GearParser.cs     port of parser/d4_gear_capture.py (TTS lines → equipped items)
    DiffEngine.cs     port of tracker/diff.js (value-aware HAVE-vs-NEED per slot)
    LogWatcher.cs      tails %LOCALAPPDATA%\d4scanner\d4_tts.log + builds the live gear
  D4Scanner.Cli/    console verify/report (also a headless live --watch)
  D4Scanner.App/    the WPF window (live, auto-updating)
```

## Run it

**Easiest: download the released .exe** — https://github.com/defessler/D4Scanner/releases (self-contained,
no .NET needed). Or run from source:
```powershell
dotnet run --project D:\Projects\D4Scanner\csharp\D4Scanner.App
```

1. **Maxroll build:** paste a build URL in the box and click **Import** — it grabs the build live and
   becomes your target (remembered across launches; optional profile box, e.g. `Endgame`).
2. **Paragon/Skills…:** pick screenshots of your paragon boards / glyph tooltips / skill tree to fill in
   the non-gear half via Claude vision (needs `ANTHROPIC_API_KEY`; result saved + reloaded).
3. Play. As you hover equipped items the window updates live: overall %, per slot your item + ✓/⚠/✗ for
   each needed affix **with your rolled value**, **under-rolled** flagged (roll-% slider), plus your extra
   affixes. **Pin** keeps it on top. (**Target…** / **Log…** override the files if needed.)

## Terminal version (handy for verifying)

```powershell
# one-shot report:
dotnet run --project csharp\D4Scanner.Cli -- --target D:\Projects\D4Scanner\target.json
# live in the console:
dotnet run --project csharp\D4Scanner.Cli -- --target D:\Projects\D4Scanner\target.json --watch
```

## Status

- ✅ Core (parser + diff + watcher) — builds; **output verified identical** to the Python/JS pipeline
  on the real game log (Dance of Knives target → 16/55, 11 equipped items, per-slot values).
- ✅ WPF app — builds and launches; live updates via a 500 ms log tail + 1 s target-file poll.
- Capture source is still the existing `saapi64.dll` writing the log. (Investigating a way to capture
  **without touching the Diablo IV folder** — see the research notes.)
- The old Python/HTML pipeline still works and shares the same JSON formats; this C# app supersedes the
  "re-run a script + re-open HTML" flow.

## Build / requirements

.NET 8 SDK (present). `dotnet build csharp\D4Scanner.App` or open the folder in Visual Studio 2022.
