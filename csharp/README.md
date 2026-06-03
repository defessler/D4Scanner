# D4Scanner — C# live build tracker

A pure C# (.NET 8 / WPF) desktop app that **watches the screen-reader log live** and shows your
equipped gear vs a target build as a Diablo-IV-style **paper doll** plus a prioritized **"do this
next"** plan — updating automatically as you hover items in game. Open it once and just look at it.

```
csharp/
  D4Scanner.Core/   parser + diff + guidance (no UI; headless-testable)
    Models.cs         data types (Item, Affix, TargetBuild, DiffReport, …)
    GearParser.cs     TTS log lines → equipped items
    DiffEngine.cs     value-aware HAVE-vs-NEED per slot / category
    BuildGuide.cs     prioritized "Do Next" plan (impact-ordered steps)
    Substitutes.cs    best-owned stand-ins + Now / Better / Best tiers
    Activities.cs     build-tailored farming / crafting recommendations
    LootFilter.cs     export wanted affixes (markdown + D4Companion preset)
    GameDataIcons.cs  real item icons extracted from the local D4 install
    LogWatcher.cs     tails %LOCALAPPDATA%\d4scanner\d4_tts.log
  D4Scanner.Cli/    console verify / report (headless --watch)
  D4Scanner.App/    the WPF window (live, auto-updating) — the primary front-end
```

## Run it

**Easiest: download the released .exe** — https://github.com/defessler/D4Scanner/releases
(self-contained single file, no .NET needed; the capture shim is embedded). Or from source:
```powershell
dotnet run --project D:\Projects\D4Scanner\csharp\D4Scanner.App
```

On first run a **setup checklist** walks three steps (the checks fill in as you complete them):

1. **Import a build** — type a build name to search, or paste a Maxroll build-guide / planner URL,
   then hit **Import**. Remembered across launches; multi-profile builds get a profile picker.
2. **Enable in-game capture** — click **Install capture DLL** (one click; installs the signed shim
   to a user PATH dir — nothing in the game folder). Then in D4: Accessibility → *Use Screen Reader*
   + *Use 3rd-Party Screen Reader* ON, Gameplay → *Advanced Tooltip Information* ON, language English.
3. **Capture skills & paragon** — the **Paragon / Skills** button: pick screenshots of your paragon
   boards / glyph tooltips / skill tree to fill the non-gear half via Claude vision
   (needs `ANTHROPIC_API_KEY`; the result is saved and reloaded).

Then play. The **overview** is a two-column glance:

- **Left — paper doll.** Your slots laid out like the in-game character screen, each showing the
  wanted item (or your equipped one — toggle **My gear / Target**) with real item icons pulled from
  your own D4 install. **Hover** a slot for a side-by-side compare card; **click** to pin it into the
  **compare deck** below (EQUIPPED vs BUILD WANTS, under-rolled affixes flagged, plus best-owned
  substitutes and a Now → Better → Best ladder). **Compare all gaps** pins everything still needing
  work; **Clear pins** empties it. The slot the top action points at gets a subtle ring.
- **Right — guidance rail.** **DO NEXT** leads with a prominent hero card for the single
  highest-impact action, then the rest of the plan as compact rows (click any to focus that slot). A
  collapsible **Recommended Activities** section says where to farm / craft the loot you still need,
  and **Export loot filter** writes a markdown checklist + a D4Companion-compatible preset.

More: **Next Steps** (the full plan — searchable, filterable by effort, paged), **Build details**
(the complete target), **Open on Maxroll** (the source build in your browser), **Builds** (switch
between imported builds). **Pin** keeps the window on top; a roll-% slider flags under-rolled affixes;
toasts confirm imports / captures / equips; the window remembers its size between launches.

### Keyboard shortcuts
`?` / `F1` cheatsheet · `Alt+O` overview · `Alt+N` Next Steps · `Alt+B` Build details · `/` jump to
search · `Ctrl` `+` / `−` / `0` zoom in / out / reset · `Esc` close popup → clear focus → clear pins
→ back to overview.

## Terminal version (handy for verifying)

```powershell
# one-shot report:
dotnet run --project csharp\D4Scanner.Cli -- --target D:\Projects\D4Scanner\target.json
# live in the console:
dotnet run --project csharp\D4Scanner.Cli -- --target D:\Projects\D4Scanner\target.json --watch
```

## Tests

Dependency-free regression checks on the Core diff + guidance logic (no test framework — just a console
that asserts and exits non-zero on failure, mirroring the JS `tracker/diff.test.js`):
```powershell
dotnet run --project csharp\D4Scanner.Tests
```
72 assertions covering `Normalize` / `PhraseMatch` / `SlotBaseName`, `ScoreSlot` / `AffixMet`, full
`DiffEngine.Diff` (met + under-rolled + missing), `BuildGuide.Steps` ordering/verbs, the fragile
**GearParser** against `samples/sample_tts.log` (names, rarity/slot, item power, masterwork/temper,
affix values + ranges + percent, unique/mythic/ancestral flags), plus `Substitutes` (core-vs-flexible
classification, best-owned, ladder), `Activities` recommendations, and `LootFilter` (markdown +
D4Companion preset). Enforced by CI (`.github/workflows/ci.yml`) on every push.

## Status

- ✅ **Live WPF app** — paper-doll overview + impact-ordered "Do Next" guidance with a hero action,
  real item icons, in-app Maxroll import, one-click capture install, vision capture, hover/pin compare
  deck, substitutes, recommended activities, loot-filter export, keyboard shortcuts + zoom, responsive
  reflow. Live updates via a 500 ms log tail + 1 s target-file poll.
- ✅ **Core** (parser + diff + guide) — output verified identical to the Python/JS pipeline on the real
  game log (Dance of Knives target → per-slot values).
- ✅ **Released** as a self-contained single-file `.exe` via GitHub Actions on each `v*` tag (the build
  signs and embeds the capture shim).
- ⏳ Re-validate the capture route per season (3rd-party screen readers broke once on the S12 PTR).

## Build / requirements

.NET 8 SDK. `dotnet build csharp\D4Scanner.App` (transitively builds Core), or open the folder in
Visual Studio 2022. The vendored CascLib emits one benign `SYSLIB0014` warning — expected.
