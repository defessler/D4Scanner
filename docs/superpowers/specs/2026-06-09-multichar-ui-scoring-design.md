# Design — Multi-character + UI/scoring improvements

Date: 2026-06-09
Status: Approved (user directed autonomous implementation)

Four independent improvements to D4Scanner, delivered in four phases, each merged to
`origin/main` on its own branch. Small/contained work first; the architectural
multi-character change last (the other three render from whatever single `LiveBuild` is
active, so they don't depend on it).

---

## Feature 1 — Multi-character support

**Problem:** all gear from every login merges into one `LiveBuild`; the shim-attached
marker wipes it on every relaunch. A Rogue and a Barbarian bleed together.

**Identity:** profile keyed on **character name + class**, auto-detected, with a manual
override always available.

**Research finding:** the character name is *not* in gear tooltips, but D4's screen reader
voices the **character-select roster** as `Name | Level (Paragon) (Tier)` — confirmed in
`samples/sample_menu_noise.log`:
```
Zuri | 70 (208) (VI)
MementoMori | 70 (220) (VII)
```
So characters are distinguishable by name (even same-class alts). The only true dead-end is
**same name + same class** (no unique character ID is ever voiced — D4 is server-side, no
local save). That falls back to a manual pick.

**Components:**
- **`RosterParser` (Core):** parse `Name | Level (Paragon) (Tier)` → `RosterEntry{Name,
  Level, Paragon, Tier}`. Tolerates the `&apos;`-style HTML entities `GearParser.Clean`
  already handles. Maintains the latest roster snapshot.
- **`CharacterProfile` model (Core):** `{ Slug, Name, Class, LastSeenUtc }` + its own
  `LiveBuild` and imported `TargetBuild`. `Slug` = sanitized `name-class`.
- **`ProfileStore` (Core):** load/save `profiles/<slug>.json` under
  `%LOCALAPPDATA%\d4scanner\profiles\`, plus an `active.json` pointer (active slug). Pure,
  headless-testable I/O over a supplied directory path.
- **Active-character detection (`LogWatcher` → `MainWindow`):** on entering the game, the
  live scan yields class (weapon type / "X Only" lines) + paragon + attributes. Match
  against the roster: paragon is a near-unique discriminator across a handful of chars,
  class confirms. Resolve `(Name, Class)` → active profile. Same-name+same-class → manual
  picker. The class is derived from the equipped weapon types and any class-restricted item
  ("Rogue Only") lines already present in the stream.
- **Shim-wipe scoping:** the `=== d4scanner tts shim attached` reset now clears only the
  **active** profile's accumulation, not all profiles.
- **UI:** a character picker (dropdown showing detected name + class) in the header.
  Auto-selects on detection; user can override. Switching swaps the active `LiveBuild` and
  the per-profile target build, then `Render()`.

**Migration:** existing single `live.json` is imported once into a default profile
(`Unknown` / detected class) on first run so users don't lose their current loadout.

---

## Feature 2 — "DO THIS FIRST" hero-card height

**Problem:** the hero card renders very tall with dead vertical space between the headline
and the "have: … equip it" sub-line.

**Fix:** size the card to its content. Remove whatever forces the extra height (min-height
/ stretched body / spacer). Keep the gold left-accent and prominence — just no empty space.
Pure `MainWindow.xaml.cs` layout change (`HeroCard()` ~line 2594).

---

## Feature 3 — "Gear & Affixes" readability + counts

**Problem:** the value string (`x50% (67%) · x20% (50%)`) is noisy and hard to read; there's
no clear "how many of my pieces have this" count.

**Each affix row becomes, compactly:**
- **`3/6 pieces`** — equipped pieces with the affix met / target piece count (replaces bare
  `×6`).
- **Progress toward the combined goal** — sum the actual rolled magnitudes across the
  equipped pieces that have the affix vs. the **summed target** the build wants across all
  its slots: `have x70% / wants x120%`.
- **Bar** fills to `haveTotal / wantsTotal` (clamped to 100%); falls back to the piece-count
  ratio when no target magnitude exists for the affix.
- Drops the per-instance `x50% (67%) · x20% (50%)` string.

**Core change:** extend the affix aggregation feeding the panel (in `DiffEngine` /
`Group`) to emit, per distinct affix name: `havePieces`, `targetPieces`, `haveTotal`,
`wantsTotal` (and the formatting unit/sign so the App can render `x%`, `+N`, etc.).

---

## Feature 4 — "All Items" as a scored upgrade list

**Problem:** the modal lists every item with no upgrade signal; equipped items clutter it.

**Behavior:**
- **Filter:** non-equipped items only.
- **Score (two tiers):**
  1. *Primary* — how well the item completes the **perfect affix set for its own slot**
     (the target build's desired affixes for that slot).
  2. *Tiebreak* — contribution of the item's affixes toward the **overall combined-affix
     goal** (the same combined target used in Feature 3).
- **Layout:** one **flat list sorted by upgrade score** descending. Items that beat the
  equipped piece in their slot are **flagged as upgrades**, but the whole list is
  score-sorted so the best upgrades float to the top.

**Core change:** a scoring function (extend `DiffEngine.ScoreSlot` / add a wrapper) that
returns a comparable score combining the two tiers, plus an `isUpgrade` flag relative to the
equipped piece's score for that slot. The All-Items modal (`ShowInventoryModal` /
`GearList.Apply`) filters to non-equipped and sorts by this score.

---

## Delivery phases (each: branch → TDD → build warning-free → 92+ tests pass → merge to `origin/main` → push)

1. **Phase 1** — Feature 2 (hero-card height). Smallest.
2. **Phase 2** — Feature 3 (affix aggregation + row redesign).
3. **Phase 3** — Feature 4 (item scoring + filtered/sorted list).
4. **Phase 4** — Feature 1 (roster parser, profile store, detection, picker).

**Finally:** bump `<Version>` in `csharp/D4Scanner.App/D4Scanner.App.csproj`, commit, tag
`vX.Y.0`, push → CI builds the release.

## Testing

- `RosterParser` — parse valid/invalid roster lines, entity decoding, multi-entry rosters.
- Affix aggregation — havePieces/targetPieces/haveTotal/wantsTotal for met/under/missing
  cases.
- Item scoring — perfect-slot-set primary ordering, overall-goal tiebreak, isUpgrade vs
  equipped.
- `ProfileStore` — round-trip save/load, active pointer, migration of legacy `live.json`.
- All new Core logic added to `csharp/D4Scanner.Tests/Program.cs` (keep the suite green).
