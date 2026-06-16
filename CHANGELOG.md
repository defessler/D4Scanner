# Changelog

Every released version of D4Scanner, newest first — consolidated from the GitHub release notes in strict version order (v0.100.0 → v0.1.0).

> **Version ordering:** GitHub correctly marks **v0.100.0** as Latest (it is semver-aware), and the in-app auto-updater compares with `System.Version`, so 0.100.0 > 0.99.0 everywhere it matters. Only *plain lexical* tag sorting (`git tag` with no flags) misplaces v0.100.0 before v0.11 — use `git tag --sort=-v:refname` (or `sort -V`) for correct order.

## v0.100.0 — 2026-06-16


A conservative cleanup of the WPF App layer (the one surface without headless unit tests). Every candidate was independently reference-checked across all `.cs` and `.xaml` (event handlers, `x:Name` controls, reflection, and the headless-render seam), with the compiler as the final guarantee that nothing live was touched. No behavior change — verified by a warning-free build, the full 1014-assertion Core suite, and render smokes (the default render is byte-identical).

### Under the hood

Removed ~90 lines of genuinely unreachable code:

- A whole vestigial detail-view subtree from an older rendering approach — `SkillsView`, `ParagonView`, and their exclusive helpers `IconTile(ReqItem)`, `Chip(ReqItem)`, and `Monogram` (skills/paragon are now rendered inline elsewhere; the detail panel routes them to `GroupRows`).
- `SocketProgressRow` — an orphaned predecessor of the live `SocketSlotRow`.
- `IconLegend` — a badge-legend builder never added to any panel.
- An unused theme brush (`GoldHi`) and a vestigial P/Invoke flag constant (`LOAD_LIBRARY_AS_DATAFILE`).

Found by a multi-agent scan with adversarial reference-verification; the one duplication candidate it surfaced (the three modal close-button blocks) was correctly rejected as carrying load-bearing comments and too risky to extract in untested UI.

## v0.99.0 — 2026-06-16


A quality pass across the Core engine (six file groups reviewed; every candidate adversarially verified for exact semantic equivalence). The Core is already clean — only two genuine simplifications cleared the bar, both verified by the full 1014-assertion test suite with no behavior change:

### Under the hood

- **`GearParser`** — folded the whole-word, case-insensitive `\b…\b` match idiom (duplicated five times across item-type / rarity / Ancestral / charm-designation detection) into a single named `WordIn` helper.
- **`DiffEngine.EvalSlot`** — removed a redundant second pass over a slot's affixes: the "umbrella-covered" set is now tracked inline during the main matching loop instead of re-deriving it with a second `MatchSlot` walk (this runs once per gear slot on every render).

No functional changes; build is warning-free.

## v0.98.0 — 2026-06-16


A small cleanup pass over the code added across this run's log-management, replay, and updater work. No behavior changes — verified by the full Core test suite (1014 assertions) and a warning-free build.

### Under the hood

- De-duplicated the "self-heal" stale-equipped-copy eviction shared by the two one-shot replay parsers (`BuildFromLines` and the new `ReplayCharacters`) into a single helper, so the subtle fingerprint-match logic lives in one place.
- Removed a dead branch in the per-character replay grouping (the class is already fixed by the group key, so the in-loop class upgrade could never fire).
- Simplified profile restoration to fill identity through one code path instead of a redundant initializer + guard.

## v0.97.0 — 2026-06-16


A discovery audit of the previously-unswept capture path and external-input helpers (Maxroll import, the updater, icon resolution, OCR/screen capture) came back clean except for one real updater bug — fixed here.

- **The "Update ready" prompt now always installs the exact version it names.** If you let a staged update sit without restarting and a *newer* release shipped before you did, the app would advertise the newest version ("v0.X ready") but actually install the older staged one — and skip downloading the new version on that check. It now downloads and applies whichever version it shows you, and always picks the newest staged build when more than one is present.

### Under the hood

- +4 Core test assertions (1010 → 1014) for the staged-version selection (newest-wins, order-independent, ignores stale/non-asset files). Build is warning-free.
- The audit's other candidates were correctly dismissed after adversarial verification: a download-integrity gap unreachable behind GitHub's CDN framing; a WGC frame-leak that can't fire in this configuration (verified against 789 real capture records — all use the PrintWindow path); and the TTS-wins-over-OCR slot merge, which is intentional, documented, and tested behavior.

## v0.96.0 — 2026-06-16


The deferred data-loss fix from the v0.95.0 log-management audit, shipped with dedicated test coverage.

- **"Clear live gear cache" now restores all of your characters, not just the active one.** When your TTS log had been rotated into the `logs\` archive folder (which happens automatically once it grows past the size cap), clearing the live-gear cache only rebuilt the character you were currently playing. Every other character — whose recent sessions lived in the archives — silently stayed empty until you logged into them again, even though the app promised to "rebuild by replaying the whole TTS log." Now each archived character's worn loadout is reconstructed into its own profile during the replay.

The reconstruction attributes each session's equipped gear to the character that was actually playing it, and keys profiles by name **and** class — so a Rogue and a Barbarian who happen to share a name (e.g. both "Heoki") are never merged, and a character played across several sessions keeps its newest gear per slot.

### Under the hood

- New Core `LogWatcher.ReplayCharacters` rebuilds per-character loadouts from archived logs — fully headless-testable. The old `Prefeed` path (which accumulated archive gear but never surfaced it, so the rebuild was dropped) is removed.
- +6 Core test assertions (1004 → 1010), including the critical case that two same-named, different-class characters reconstruct as **separate** profiles with no cross-contamination, and that cross-session merges keep the newest gear per slot. Build is warning-free.
- The restore runs before the live tail begins, so the currently-active character's newest session still merges cleanly on top.

## v0.95.0 — 2026-06-16


A multi-agent discovery audit aimed squarely at the newest, least-exercised code — the v0.43/v0.44 log-management and Settings-lifecycle subsystem, which had shipped without a defect sweep. Seven skeptical hunters, every finding adversarially verified for reachability (one candidate was correctly thrown out). Six genuine defects fixed:

- **Heavy log days no longer delete your *newest* archives.** The rotation counter wasn't zero-padded, so once a day produced 10+ archives they sorted `_1, _10, _11, _2, _3…` — and retention pruning then deleted `_10/_11/_12` (your most recent sessions) while keeping older ones. Archives now sort by their true (date, counter) order regardless of padding, so pruning always removes the genuinely oldest. *(data-loss)*
- **Moving the log to another drive no longer strands it.** A cross-volume move copied the active log to the new drive but then failed relocating the archive folder (Windows can't `Move` a directory across volumes), leaving the log orphaned at the destination while the app insisted "nothing was applied." Moves are now cross-volume-safe and roll back cleanly on any failure, so the log is never stranded and the message is always truthful. *(data-loss)*
- **Moving the log into a folder that already has a `logs\` subfolder no longer loses your history.** That case used to silently skip relocating the archives (stranding them, un-prunable) while still re-pointing the app at the new location. The archives now merge into the destination, preserving any same-named files already there.
- **Re-acquired items reappear correctly in OCR-only mode.** After clearing the live-gear cache with TTS capture off, an internal one-shot "replay" flag could get stuck on, which permanently disabled the mechanism that un-hides a deleted item when you pick it up and hover it again. The flag now retires correctly when there's no TTS channel.
- **Switching characters while viewing a past session now works.** Picking a different character from the switcher during a read-only session preview silently did nothing on screen (and the "Switched to…" toast lied). It now exits the preview and shows the chosen character.
- **The session picker no longer shows "loading sessions…" forever** when there are no past sessions — it now says "no past sessions yet."

### Under the hood

- +11 Core test assertions (993 → 1004), including a ≥10-same-day-rotation ordering test and a `MoveArchives` merge/collision/no-op test. Build is warning-free.
- New cross-volume-safe, merge-not-strand `LogStore.MoveArchives` (headlessly tested).
- All findings adversarially verified; 1 candidate correctly refuted (a "Battle.net launcher" hard-block that would have been a regression). One deeper data-loss issue in the rotated-archive *replay* path was confirmed and is deliberately deferred to its own release so it can ship with dedicated multi-session replay tests.

## v0.94.0 — 2026-06-15


A discovery pass over the WPF App layer (the surface the automated test suite doesn't cover) came back nearly clean — one real fix:

- **Alt+I no longer stacks the All-Items window on itself.** Pressing Alt+I while the All-Items list was already open opened a second copy on top of the first; the first Esc then closed only the top one and left a duplicate stranded underneath. It now refreshes the single window instead.

A second candidate was investigated and correctly discarded — verified against 789 real capture samples, the proposed change would have regressed live OCR parsing rather than fixing anything.

## v0.93.0 — 2026-06-15


A second self-directed audit (skeptical agents over previously under-covered subsystems, every finding adversarially verified) turned up four more genuine defects — all in the guidance/advice layer. One candidate was correctly thrown out as unreachable. Fixes:

- **No more "socket your gear" nag on a finished build.** If your wanted sockets were already filled (e.g. via a runeword), the recommendations panel still told you to socket them. Fixed.
- **Substitutes panel now agrees with the upgrade list.** When two items tied on core affixes, a strictly-better owned item could appear under "Best you own" without the upgrade badge — even though the main diff listed it as an upgrade. Now consistent.
- **Duplicate-Mythic advice restored.** A spare copy of a Mythic you're already wearing was mislabeled generic junk ("salvage for materials") instead of being flagged to salvage for a Resplendent Spark. Fixed.
- **Cleaner "Better:" guidance text.** Slots with no shared "core" affixes (or aspect-only slots) no longer render the nonsensical "a Rare/Legendary with the 0 core affixes."

### Under the hood

- +8 Core test assertions (985 → 993); build is warning-free.
- All findings adversarially verified before shipping (1 candidate correctly refuted as unreachable).

## v0.92.0 — 2026-06-15


A self-directed code audit (skeptical agents hunting each Core subsystem, then adversarially verifying every finding) turned up seven genuine defects — confirmed by independent skeptics that applied each fix and re-ran the full test suite. Here's what's fixed:

### Fixes

- **Live Talisman tracking now works.** Equipped charms, seals, and runes were silently dropped on the live capture path — the Talisman card stayed empty during play and only filled in when replaying a saved session. They now surface live like every other piece.
- **Charms with descriptive flavor text are no longer dropped.** A charm whose tooltip flavor or set-bonus line contained a slot word (e.g. "…focus upon a single spot") could be misread as an off-hand and discarded. Fixed — verified against a real captured charm.
- **No more crash from a corrupt season-pack override.** A hand-edited `season_pack.json` with a `null` list or section could crash the activities/guidance panel (and take the app down with it). The loader now tolerates it gracefully.
- **Smarter "All Stats" handling.** When a build wanted a minimum on a specific stat (e.g. Dexterity ≥ 100), a lower "All Stats" roll could mask a specific roll that actually met the bar, mis-ranking substitute suggestions. Fixed.
- **Two-handed weapon socket advice.** A 2H weapon with one socket is now correctly told it can hold a second one (it was previously capped at one).

### Under the hood

- +11 Core test assertions (974 → 985); build is warning-free.
- All findings adversarially verified before shipping — zero refuted.

## v0.91.0 — 2026-06-15


This release implements the last four open items from the §8 gearing-knowledge audit, so **all 17 tracked gaps are now fixed**. The work was designed via a multi-agent fan-out and adversarially reviewed before shipping.

### What's new

- **Smarter completion %** — the headline build-completion percentage is now *impact-weighted*. A missing build-defining unique or aspect weighs far more than a plain additive damage roll, so the number reflects how close you actually are to a functional build. (The X/Y-met counts and green-complete checks are unchanged — only the percentage got smarter.)
- **Torment-aware crafting steps** — guidance steps now warn you when an action is gated behind a higher Torment tier you haven't reached: TEMPER steps note that Legendary Temper Manuals start dropping at Torment 2, and "improve this roll" steps note that Greater-Affix odds jump at Torment 8.
- **Weapon upgrades respect Item Power** — at endgame, a 900 Ancestral weapon is now correctly flagged as an upgrade over a sub-900 equipped weapon even when it has fewer matching affixes (base weapon DPS scales with Item Power). Weapon-only, and only in Torment.
- **Transfigured items are recognized** — an item that's been Transfigured in the Horadric Cube is permanently unmodifiable. The app now reads that state and stops suggesting impossible temper/enchant/masterwork/socket/imprint steps on it, showing a single "locked" note instead.

### Under the hood

- +29 Core test assertions (945 → 974); build is warning-free.
- One latent crash (a corrupt season-pack override with a null field) was found and fixed during adversarial review.
- The docs §8 gap table is reconciled — all 17 gaps marked fixed.

## v0.90.0 — 2026-06-15


Completes the audit-driven cleanup begun in v0.89.0 by implementing the remaining lower-priority findings. **Entirely behavior-preserving** — a final 4-lens adversarial review verified all 21 changes preserve behavior (0 regressions); build warning-free; 945 Core tests green.

### Internal only — no behavior change
- **Less duplication, fewer magic numbers:** one `RelAge()` recency formatter (was 3 copies); three named log-retention clamp helpers (was 9 inline `Math.Clamp` calls); the hover-card gap/scrollbar constants hoisted to a single definition; a `PowerBoxFor` overload that collapses two identical unique-power UI blocks.
- **Dead code removed:** an unused icon-resolver parameter and overload, an unused file-count helper, and a single-affix ceiling method whose tests now exercise the live multi-affix path instead.
- **Tighter capture path:** the OCR scanner now resolves the Diablo IV window once per frame (rather than enumerating processes twice) and disposes the process handles.
- **Single source of truth:** the slot-name normalizer now delegates to one canonical implementation; small de-duplications in the log-retention prune, tombstone sweep, and Maxroll import.
- Documented why the three "is two-handed" weapon checks intentionally differ (so a future change won't wrongly unify them).

Nothing to migrate; no settings or data changes. If you're on v0.89.0 the only difference you'd notice is none — this is pure maintainability.

## v0.89.0 — 2026-06-15


The product of a full multi-agent audit (every area reviewed, each finding independently verified). Two real bugs fixed; the rest is low-risk cleanup with no behavior change. Build warning-free, 945 Core tests green.

### Fixed (user-visible)
- **OCR no longer leaks a graphics device on every frame.** Each screen grab created a Direct3D device wrapper that was never released, so native GPU/handle use grew unbounded during long capture sessions. Now disposed each grab.
- **The paper-doll compare card no longer gets orphaned by a live update.** If your gear refreshed (a new TTS/OCR read) while you were hovering a slot, the card could jump or close mid-read; it's now dismissed and cleanly re-anchored on each refresh.
- **Spiritborn Quarterstaff is correctly treated as two-handed** in the crafting-cost estimate — its masterwork (Obducite) cost and the 2-handed imprint note were being under-counted.
- **A stale System32-installed capture shim is now offered for upgrade** (the version check previously skipped that install location).
- **Item-name display is now locale-independent** (a Turkish/Azeri system locale could previously fork how names were title-cased).

### Internal (no behavior change)
- Removed ~12 pieces of dead code (an unused regex, two unused public methods, a write-only dictionary, a vestigial setting, unreachable branches, and an abandoned guidance helper).
- De-duplicated drift-prone code: one source of truth for the settings draft, the icon-catalog parser, the capture-shim path probe, and all of the app's on-disk paths (`AppPaths`).
- Fixed a few comments/docs that contradicted the code they described.

No settings or data changes; nothing to migrate.

## v0.88.0 — 2026-06-15


The compare card is wide (two side-by-side panels), and the paper doll is a compact centered grid (armor left, jewelry right, weapons below) — so opening the card next to the hovered *cell* inevitably covered the doll's other columns.

**Fix:** the card now measures the doll's icon grid and opens in the clear space **beside the whole doll** (to its right, or to its left if the right doesn't fit), sized to whatever that gap allows, while still aligning vertically with the row you're hovering. So it lands in the rail/margin and keeps every slot icon visible. In a window too narrow to fit a readable card beside the doll it falls back gracefully (a smaller card, then minimal overlap), but at normal sizes the icons stay clear.

This builds on the v0.86/v0.87 hover fixes — it keeps the anti-flicker (close only when the mouse leaves both the slot and the card) and the left/right hysteresis (no side-blinking).

### Under the hood
- `ShowHover` measures the doll grid (`_dollElement`, gated on the target actually being inside it) and budgets the card width to the clear space beside it; `ClampPopupInWindow` positions relative to the doll's window-space bounds instead of the hovered cell.
- App-only (WPF); build warning-free; Core test suite 945 assertions, all green.

## v0.87.0 — 2026-06-15


Follow-up to v0.86.0: the compare card could still flip rapidly between opening on the left and the right of a slot.

**Cause:** the card's auto vertical scrollbar appears/disappears as content height changes, shifting the card width by ~17px. That width change tipped the "does it overflow the right edge?" test back and forth, so the card kept switching sides — and each jump moved it off the cursor, re-triggering.

**Fix (two parts):**
- **Stable width** — the left/right decision now uses the card's own fixed width plus a scrollbar allowance, so an appearing scrollbar can't change which side is chosen.
- **Hysteresis** — once the card commits to a side, it *holds* that side across re-placements and only flips when that side genuinely stops fitting and the other one does. A borderline case can no longer toggle back and forth.

Also: re-entering the same slot now keeps the existing card open instead of rebuilding and re-deciding its position.

### Under the hood
- `ClampPopupInWindow`: placement uses a stored card width (not the live popup size) and a committed-side deadband (`_hoverOpenLeft`); the slot's `MouseEnter` skips re-show when the card already shows that slot.
- App-only (WPF); build warning-free; Core test suite 945 assertions, all green.

## v0.86.0 — 2026-06-15


Hovering a gear slot could make the floating compare card flicker in and out rapidly.

**Cause:** when the card opened on top of the cursor (common in a narrow window, where the card can't fit beside the slot), the cursor "collided" with the card — the slot read that as the mouse leaving, closed the card, the cursor was back on the slot, it reopened, and the loop repeated many times a second.

**Fix:** the card now closes only once the mouse has left **both** the slot and the card. Moving the cursor onto the card (or the card overlapping the cursor) keeps it open instead of triggering a close, so the flicker loop can't start. As a bonus you can now move onto the card to read or scroll it before it dismisses.

### Under the hood
- The slot's `MouseLeave` schedules a short deferred close instead of closing immediately; the close only fires when neither the slot nor the card has the mouse. The card's own enter/leave participate in the same check.
- App-only (WPF); build warning-free; Core test suite 945 assertions, all green.

## v0.85.0 — 2026-06-15


The fix for the long-standing "my two rings switch places when I hover them" bug.

### What was happening
Diablo IV re-announces the **"Ring"** slot label every time you hover a ring, and the tracker was numbering ring slots by a running **count** of those announcements. So inspecting ring 1 → ring 2 → ring 1 made the first ring "the 3rd Ring I've seen" — a higher slot number than ring 2 — and the two rings visibly swapped places on the paper doll. The same applied to dual-wield weapons.

### The fix
Slot positions are now keyed by the **item's name**, not a running count: the first distinct ring takes slot 1, the second takes slot 2, and **re-hovering a ring reuses its existing slot** instead of renumbering it. The assignment is anchored to the order your gear is announced when you open the Character panel (which follows the in-game slot order) and re-anchors each time you reopen it. Result: stable, no swapping, no matter how many times you mouse over them.

### Why not the OCR-position approach
The earlier plan was to use OCR to read each ring's on-screen position. Analyzing real capture sessions settled it: both of your equipped rings render their tooltip at the **same** screen position (X≈2901), so OCR position genuinely can't tell them apart — a single equipped-item tooltip anchors to a fixed detail-panel spot, not its slot. The name-keyed stability fix solves the actual problem (the swapping) deterministically and needs no screen capture at all.

### Under the hood
- `GearParser`: per-header position counter → per-name stable map (`_slotPosByName`); re-hover reuses the position. Entirely in Core (UI-free).
- Locked with a regression test that feeds ring1→ring2→ring1 through the real parser pipeline and asserts no renumber / no swap.
- Build warning-free; Core test suite 945 assertions, all green.

## v0.84.0 — 2026-06-15


A targeted fix to how often the OCR scanner runs, found by analyzing real capture sessions.

### The problem (measured)
The scanner ran *fast* only while a **named panel** (Character/Stash/Vendor) was detected, and *idle* otherwise. But a **floating item tooltip** — exactly what appears when you hover a ring, weapon, or any gear — usually covers the panel chrome, so detection saw "no panel" and dropped to the slow idle cadence. Result: hovering an item for a few seconds frequently fell *between* scans and was never captured. In the diagnostic data, item tooltips kept landing ~20 seconds apart instead of being caught as they appeared.

### The fix
The fast cadence now triggers on **gear being on screen** — a detected panel **or** a floating item tooltip (an "Item Power" line) — and the idle interval was tightened. So when you hover your gear, the scanner keeps up with you, capturing each item as it appears.

This makes live-gear tracking via OCR meaningfully more responsive, and it's the piece that unblocks the upcoming ring/weapon slot-position fix (which needs dense capture of equipped-item hovers to work).

### Under the hood
- `OcrCaptureEngine`: cadence keyed on gear visibility (panel **or** tooltip), active interval 3s → 1.5s, idle 20s → 6s.
- `CaptureDiag.NextIntervalMs` doc/signature updated to reflect the gear-visible trigger.
- Build warning-free; Core test suite 938 assertions, all green.

## v0.83.0 — 2026-06-14


The first shippable result of the OCR↔TTS fusion, built and validated against **your real 17-minute capture session** (59 diagnostic records). This release makes the panel classifier — the piece that decides whether a hovered item is *worn* vs *in a vendor/stash* — far more accurate, using what OCR actually sees on screen.

On your session, panel detection went from **50/65 (77%) to ~64/65 (98%)**. Every miss was on a menu/tooltip frame (gameplay was already perfect), and the fixes are veto-first so they can't misfire during combat.

### Fixed (each from a real misclassified frame)
- **Stash items no longer leak into your worn gear.** When the "Stash" title OCR-garbles to "STAS", detection now recognizes the stash by its garbled title and its exclusive "Edit Tab" control, instead of letting the always-on stats sidebar read the screen as your Character panel.
- **The Blacksmith salvage screen and the Horadric Cube are recognized as vendor/crafting**, not Character — so a salvage-all confirmation can't momentarily stamp bag items as worn.
- **Character item-comparison tooltips are now recognized.** Hovering an item to compare against your equipped one (which shows a "CHARACTER" column header but no slot grid) was previously unclassified; it now correctly reads as Character — the frame the position fusion needs.
- **The stats sheet is recognized even when its "Equipment" tab glyph OCR-garbles**, via the four core-attribute labels (Strength/Intelligence/Willpower/Dexterity).

### Honest status on the ring-slot fix
The root cause of "rings switch slots on hover" is now pinned precisely: the TTS log assigns ring slots by **hover order**, not screen position, so hovering the second ring first labels them backwards — and the TTS log genuinely has no per-ring identity to fix it from. OCR position is the only channel that can.

**That fix is designed but not yet shipped, on purpose.** Your session never hovered both rings in sequence, and the one item I could track across frames (the Helm tooltip) moved 760px by *layout*, not slot — which is evidence that a single tooltip's position may not, by itself, identify the ring. Shipping the override on that would be guessing. To finish it I need one targeted capture:

> With "Save capture diagnostics" on, open the Character panel and hover **ring 1 → ring 2 → ring 1 again** (~1s each), then **main-hand → off-hand** — ideally once on a normal capture and once at 1440p.

That calibration set will confirm the geometry and let the ring/weapon position fix ship validated rather than guessed.

### Under the hood
- All changes are in `PanelOracle.Detect` (Core, UI-free), derived line-by-line from the real OCR token sets and locked with 12 new regression tests built from those frames.
- Build warning-free; Core test suite at 938 assertions (+12).

## v0.82.0 — 2026-06-14


Groundwork for matching what the OCR scanner sees on screen to the TTS log, so a later release can fix slot-position issues (for example rings swapping places on hover) using real on-screen positions instead of guesswork.

### New
- **Adaptive OCR cadence.** When a gear panel is open the scanner now refreshes quickly to catch fast hovers, and idles during normal gameplay — more responsive capture without the constant-scan cost.
- **"Save capture diagnostics" setting** (Settings → Capture, off by default). When enabled, each OCR scan writes a small JSON record — the recognized words *with their on-screen positions*, the detected panel, and a snapshot of the matching TTS-log lines — into a `capture-diag` folder. This is the data used to tune OCR↔TTS matching. It is self-limiting (capped at 400 MB / 30 days, pruned automatically) and requires screen capture (OCR) to be on.

### Under the hood
- OCR now records which capture path produced each frame (Windows Graphics Capture vs PrintWindow) so on-screen positions are interpreted in the correct coordinate space.
- The panel-state oracle is kept warm even on unchanged frames, so the worn-gear rescue can't go stale during a perfectly still character-sheet hover.
- The capture engine gained a cooperative-shutdown path and a single shared panel detection per scan (fed to the oracle, the items, and the diagnostic alike).

### Quality
- Reviewed via a 5-dimension adversarial pass; three confirmed fixes to the diagnostic's log-tail reader and retention sweep (no silent truncation of the newest lines, correct handling of a buffer boundary that lands on a line break, and honest disk-budget accounting when a file can't be deleted).
- Build warning-free; Core test suite at 926 assertions (+27 this release).

## v0.81.0 — 2026-06-14

## D4Scanner v0.81.0

**Fixed: upgrades are now only suggested for the right weapon slot.**

A melee weapon (sword/dagger) could show up as an "upgrade" badge / Do-Next suggestion
for your **ranged** weapon slot (and vice-versa), because the per-slot upgrade finder
only matched on the generic "weapon" slot — it didn't check the weapon *type*. The
"All Items" list already gated this correctly; the badge and guidance path was missing
the same check.

Now the per-slot upgrade finder applies the same weapon-type gate everywhere: a melee
weapon is never an upgrade for a bow/crossbow slot (or the reverse), and a one-hander is
never suggested for a two-hand slot. Every other slot was already matched correctly.

No action needed.

## v0.80.0 — 2026-06-14

## D4Scanner v0.80.0

**UI fixes from live testing.**

- **Hover cards stay fully on-screen.** The floating compare card you get when hovering a
  paper-doll slot could run off the edge — off the left in a narrow window, or off the
  bottom for a tall card near the bottom of the screen. It now clamps to the window on
  both axes (opens to the right of the slot, flips left if needed, slides up if it would
  overflow the bottom), so the whole card is always visible.
- **The ✕ close button now works everywhere.** On the Settings (and a couple of other)
  modals the ✕ only registered a click if you hit the glyph dead-center — the padded area
  around it was transparent to clicks. The whole button box is now clickable.
- **Padding between the Close and Save buttons** in Settings, so they're no longer flush
  against each other.
- **The tempering (⚒) marker no longer gets cut off** in the BUILD WANTS list — a long
  affix name used to push it past the card edge. It now has its own column and always shows.

(Still on the list from testing: stabilising the two ring slots so they don't swap on
hover — that's coming with the OCR positioning work.)

No action needed.

## v0.79.0 — 2026-06-14

## D4Scanner v0.79.0

**New: OCR can now confirm your equipped gear — fewer items wrongly dropped off the paper doll.**

The screen-reader (TTS) feed is precise about *what* an item is, but it can't always tell
*where* you were looking at it — worn on your character vs. browsed in a bag, stash, vendor,
or the Armory. To stay safe it errs on the side of "not worn," which sometimes left a genuinely
equipped item off your paper doll (especially after a long browsing session, when the character-
panel marker had scrolled out of view).

This release adds a **sensor-fusion** between the two capture channels. When OCR (the optional
screen-capture mode) is enabled, it now reads which panel is actually open and feeds that to the
TTS classifier. If TTS is unsure about an item but OCR confirms your **character sheet** was open
at that moment, the item is correctly kept as worn. Crucially, it's **strictly additive and safe**:

- It only ever *promotes* a gear item to worn — it never wrongly removes one.
- It only acts on items that already carried a character-slot signal, with a real timestamp, within
  a tight time window.
- The Armory loadout screen (which looks like the character sheet) is explicitly excluded, and the
  Purveyor's "Ring" gamble is disambiguated — OCR sees the *vendor*, so it's never read as worn.

The result: with OCR enabled, your equipped gear is captured more reliably, without any new risk
of vendor/stash items leaking onto your doll. With OCR off, behavior is exactly as before.

Also fixed a stale behavior where the OCR mode wrote an unused `character.png` screenshot.

No action needed — turn on Screen Capture (OCR) in Settings to benefit; TTS-only setups are
unchanged.

## v0.78.0 — 2026-06-14

## D4Scanner v0.78.0

**Internal: the headless `--render` export is now strictly read-only.**

D4Scanner has a developer/diagnostic mode (`D4Scanner.exe --render out.png`) that
renders the window to an image without showing a UI — used to check layout and
catch clipping. It turned out this export could overwrite your saved `live.json`
loadout mirror: closing the off-screen window fired the same "save on exit" handler
the real app uses, persisting whatever throwaway state the render had loaded.

The render path now flags itself as read-only and never writes `live.json` or
settings on close. Your equipped loadout is unaffected either way — the authoritative
copy lives in your per-character profile — but the legacy mirror is no longer touched
by a render.

No action needed; this only affects the diagnostic `--render` mode.

## v0.77.0 — 2026-06-14

## D4Scanner v0.77.0

**Fixed: your Talisman charms now show correctly — including a full Set Charm set.**

Two real-data bugs were breaking the Talisman card when you'd been on the Talisman
and Horadric Cube screens:

- **A phantom charm from the Horadric Cube.** The cube's "Reroll Set Charm" recipe
  voices the words "1x Set Charm" (a material it needs). The app mistook that for an
  actual charm and created a bogus "Required Materials" item — which then took over
  the charm slot and *hid your real charms*. Charm detection is now precise: only a
  genuine standalone charm tooltip counts, never a crafting-recipe line that merely
  mentions one.

- **Only one charm showing instead of your whole set.** A Lord of Hatred talisman
  holds up to six charms, but the app was collapsing them all into a single slot — so
  a complete set (e.g. the 5-piece *Legacy of the Sightless*) appeared as just one
  charm. All your charms now show, with the set bonus tracked (e.g. "Legacy of the
  Sightless 5/5").

If you've equipped multiple charms, open the app and they'll all appear in the
TALISMAN card with their affixes and set progress.

No action needed.

## v0.76.0 — 2026-06-14

## D4Scanner v0.76.0

**Fixed: hovering an unequipped item no longer clears the equipped one off the paper doll.**

If you owned two items with the same name but different rolls — one equipped, a
different copy in your bags — hovering the bag copy would knock the equipped one off
the paper doll. The app treated them as the same item because it matched on name and
slot only, ignoring the actual stats.

The "self-heal" mechanism behind this (which correctly removes an item from the doll
when it's really sitting in your bags) now compares the full item content, not just
the name. So it only clears the worn item when the thing you hovered is genuinely the
*same* item — a different roll of the same-named item leaves your equipped piece
alone.

No action needed.

## v0.75.0 — 2026-06-14

## D4Scanner v0.75.0

**New "What You Can Craft" section — every crafting move on your gear, in one place.**

The app already knew the full crafting plan for each equipped item (temper → enchant
→ masterwork → capstone reroll → socket → imprint), but you could only see it one
slot at a time by clicking into an item. Now there's a dedicated **WHAT YOU CAN
CRAFT** section, just above Build Progress, that lays out the available moves across
your whole loadout at once.

Each equipped piece gets a card showing its crafting steps — tempering a missing
affix, enchanting a wrong one, masterworking toward Quality 25, rerolling an
off-build Capstone, adding/filling sockets, imprinting an aspect — each tagged with
the station, the rough material cost, and any caution (like an enchant that would
destroy a Greater Affix). Slots with nothing to craft are omitted.

No action needed — load a build and it appears automatically.

## v0.74.0 — 2026-06-13

## D4Scanner v0.74.0

**A nudge to scan your Talisman — and the talisman feature, verified on real data.**

Small finishing touch on talisman support: if you've captured gear but haven't
scanned your Talisman yet, the **TALISMAN** card now shows a one-line prompt to open
the Talisman screen in-game and hover your seal and charms, so the feature is
discoverable instead of just absent. (It stays quiet on a fresh launch before
anything is captured.)

This caps the talisman work from v0.71.0–v0.73.0: capturing charms of every rarity,
showing your seal + charms with the seal's charm-slot capacity, and tracking your
Set Charm set bonuses (complete sets in green). All confirmed working end-to-end
against a real capture log.

No action needed — open the Talisman screen in-game and your seal, charms, and set
bonuses populate the card.

## v0.73.0 — 2026-06-13

## D4Scanner v0.73.0

**The Talisman card now tracks your Set Charm set bonuses.**

Set Charms power the returning set bonuses, and the scanner was already capturing
each charm's set membership and progress from its tooltip — it just wasn't showing
it. The **TALISMAN** card now ends with a **SET BONUSES** section listing each set
you have pieces of, with its active/total count (e.g. "Legacy of the Sightless
5/5"). Completed sets are highlighted in green so a full set is obvious at a glance.

This rounds out talisman support across the last three updates: capturing charms of
every rarity (v0.71.0), showing your seal + charms (v0.72.0), and now your set-bonus
progress.

To populate it: open the Talisman screen in-game and hover your set charms.

## v0.72.0 — 2026-06-13

## D4Scanner v0.72.0

**Your Talisman now has its own card — seal + charms at a glance.**

Following v0.71.0 (which fixed *capturing* talisman charms), this adds the display
side: a **TALISMAN** card in the guidance rail showing the talisman pieces you've
captured — your seal, your charms, and any runes — each with its rarity and top
affixes. The seal also shows its charm-slot capacity (e.g. "Legendary · 5 charm
slots"), read straight from its tooltip.

Because Maxroll planners don't define talisman targets, this is purely a "what you
own" view rather than a build comparison. The card only appears once you've
captured a talisman piece (no empty box otherwise).

To populate it: on this version, open the Talisman screen in-game and hover your
seal and charms — they'll show up here and in All Items.

## v0.71.0 — 2026-06-13

## D4Scanner v0.71.0

**Talisman charms are now captured — not just Set/Unique ones.**

Every build uses the Talisman (a seal plus charm sockets), but the scanner only
recognized charms whose tooltip type read "Set Charm" or "Unique Charm". The Lord
of Hatred format also voices charms as **"Rare Charm", "Magic Charm", "Legendary
Charm", "Charm (Ancestral)"** — and those matched nothing, so the parser dropped
them entirely. Your rare and magic charms simply never showed up.

The parser now recognizes any rarity of charm, so they're captured and appear in
**All Items** like the rest of your gear. The fix is precise: a seal's "Unlocks N
Charm Slots" line and all-caps charm names can't be mistaken for a charm type, and
seals still resolve correctly as seals.

(Seals were already recognized via "Horadric Seal". A dedicated talisman section on
the paper doll is a separate display step — this release is about *capturing* the
items, which was the gap.)

No action needed — hover your charms in-game and they'll appear.

## v0.70.0 — 2026-06-13

## D4Scanner v0.70.0

**The exported loot filter is now a real setup guide, not just a per-slot list.**

The game's native loot filter (added April 2026) filters on conditions like Item
Power / Ancestral, specific uniques, and required affixes. The export now leads
with the two most useful of those that can be derived from your build:

- **Item-Power floor** — it states up front to keep Ancestral (≥ 900 Item Power)
  at endgame, the single most important filter rule, which was missing before.
- **Priority affixes** — a summary ranking affixes by how many gear slots your
  build wants them on. An affix wanted on four slots is a far higher-value filter
  condition than a single-slot one, and that cross-slot view wasn't visible in the
  per-slot lists.

The per-slot detail (uniques, aspects, affixes, sockets) is unchanged below the
summary. Re-export your loot filter to get the new sections.

## v0.69.0 — 2026-06-13

## D4Scanner v0.69.0

**Near-endgame players now get pushed toward Torment 12.**

When your build still has gaps, the app suggests pushing to the next Torment tier
that unlocks better drops. But the tier table stopped at Torment 11 (Resplendent
Sparks), so a player *on* Torment 11 — one tier below the cap — got no suggestion
at all, even though Torment 12 is the game's best-rates, gear-farming tier and the
obvious next step.

Torment 12 is now in the ladder: at Torment 11 you'll get "Push to Torment 12 —
clear Pit 100". The wording is also tidied for the top tier (it no longer says
"higher tiers also…", since there aren't any). A maxed Torment 12 player still gets
no push, as before.

This came out of a full cross-check of the app's season data against the
game-mechanics reference — everything else (socket counts, masterwork costs, obol
prices, boss ladder, item-power floor) matched.

No action needed.

## v0.68.0 — 2026-06-13

## D4Scanner v0.68.0

**Imported builds no longer show garbage aspect names.**

Validating a real Barbarian build turned up a junk entry in its aspect list —
"BSK Barbarian 001 2" — which also appeared as an actionable step telling you to
imprint it. It was a legendary aspect whose name never resolved during import, so
the raw internal id leaked through as the display name.

The importer already filters this kind of unresolved id for unique items, but the
check only recognized rarity-keyed ids (Unique/Legendary/…) and wasn't applied to
aspects, so a class-keyed id like this one slipped through. Now:

- the importer filters these out of aspects too, and
- builds you imported *before* this fix are healed automatically on load — the junk
  aspect is dropped from the list and from its slot, no re-import needed.

Real, resolvable aspects are untouched. The example build went from 8 aspects (one
junk) to 7 real ones.

No action needed — just reopen the app.

## v0.67.0 — 2026-06-13

## D4Scanner v0.67.0

**Exported loot filters now include every aspect the build wants.**

The loot-filter export (both the markdown checklist and the Diablo4Companion
preset) built its aspect list only from aspects pinned to a specific gear slot.
A build can also list aspects that aren't tied to one slot — a flex/utility
aspect you'll place wherever it fits — and those were silently dropped from the
export, even though the in-app build progress tracks them and tells you to imprint
them.

Both exports now include these slot-less aspects: the markdown gets an "Other
Aspects" section (mirroring "Other Uniques"), and the companion preset lists them
too. Aspects pinned to a slot still appear under that slot and aren't duplicated.

No action needed — just re-export your loot filter to get the complete list.

## v0.66.0 — 2026-06-13

## D4Scanner v0.66.0

**All Items no longer shows leftover junk rows from old captures.**

Validating against real captured data turned up a phantom inventory entry — an
item shown as "Ring" with an item power but no slot, no type, no rarity, and no
affixes. It was stale residue from an older capture of the Talisman panel (where
the screen reader voices the slot word "RING" as a header); the current parser
already handles that correctly and creates nothing, but the old artifact lingered
in your saved data and surfaced as a meaningless row in All Items.

All Items now hides any such pure artifact — an item with none of slot, type,
rarity, or affixes is uncategorizable and can't be equipped, compared, or matched,
so it's dropped from the view. The filter is deliberately strict: an item with even
one of those is always kept, so nothing real (or mid-capture) is ever hidden. This
only affects the All Items display — scoring, verdicts, and the build diff are
untouched.

No action needed — the junk row disappears on its own.

## v0.65.0 — 2026-06-13

## D4Scanner v0.65.0

**Skill rows now show the game's own "rank X/Y" — revealing your +Ranks bonus.**

Equipped-skill rows previously read like "Dance of Knives — 27 pts", which both
mislabeled a skill *rank* as "points" and threw away the "/Y" base-max the game
voices ("RANK 27/15"). That gap — effective rank minus base — is exactly your
+Ranks bonus from gear and paragon, build-relevant information that was hidden.

Skill rows now read "rank 27/15": accurate, and it shows at a glance how much
+Ranks investment a skill carries. (Rows captured by an older version show
"rank 27" until the next time you hover the skill in-game, which refreshes the
base-max.)

Surfaced by validating the diff output against real build + captured-gear data.
No action needed.

## v0.64.0 — 2026-06-13

## D4Scanner v0.64.0

**Parser hardening — the gear parser tolerates malformed numbers uniformly.**

Four numeric fields the parser reads from tooltips — masterwork Quality,
Masterwork rank/max, Temper charges, and Required Level — used a strict integer
parse that would throw on a malformed or oversized digit run and fault the entire
tooltip's parse. Every other number in the parser (Item Power, DPS, socket count,
set counts) already used the lenient, never-throwing parse and degraded gracefully.

This unifies those four fields onto the same lenient parse. On today's exact
screen-reader text nothing changes — ordinary masterwork/temper/level values parse
identically (pinned by new regression tests). But because the screen-reader format
shifts every season, this removes a latent asymmetry: a future format that runs
digits together can no longer crash the parse for those four fields when the rest
of the parser tolerates it.

No action needed — purely internal robustness.

## v0.63.0 — 2026-06-13

## D4Scanner v0.63.0

**More capture memory cleanup — the OCR scan path no longer leaks WinRT objects.**

Following v0.62.0's fix to the frame-grab path, a sweep of the sibling OCR
capture code found the same class of leak in two more places:

- The OCR scanner converted each captured frame into a native `SoftwareBitmap`
  for text recognition but never disposed it — leaving another full-screen-sized
  bitmap (~33 MB at 4K) to the garbage collector's finalizer on every changed
  frame it scanned (roughly every 20 seconds during play).
- The bitmap-conversion helper leaked its `DataWriter` (a small COM wrapper) per
  scan for the same reason.

Both are now disposed deterministically. The recognition result is unchanged;
only the cleanup of internal buffers becomes immediate, reducing native-memory
pressure during active OCR scanning.

No action needed — the fix applies automatically.

## v0.62.0 — 2026-06-13

## D4Scanner v0.62.0

**Capture memory fix — periodic OCR scanning no longer leaks native bitmaps.**

The Windows.Graphics.Capture frame-grab path created an intermediate native
`SoftwareBitmap` (a copy of the captured surface) but never disposed it,
leaving a full-screen-sized bitmap — about 33 MB at 4K — to the garbage
collector's finalizer on every successful grab. Because OCR capture grabs run
on a periodic timer, these accumulated as native-memory pressure between GC
passes during active OCR scanning.

The intermediate is now disposed deterministically the moment it has been
converted to the bitmap the app actually uses. On-screen behavior is
unchanged; only the cleanup of an internal buffer becomes immediate.

No action needed — the fix applies automatically.

## v0.61.0 — 2026-06-13

**Crash-safe settings & build files.** Your settings (`app.json` — log path, capture toggles, log-retention, window size, etc.) and an imported build file were written in place, so a crash or power-loss mid-save could corrupt them — silently resetting settings or breaking the loaded build on next launch. Both now write to a temp file and atomically rename into place, matching the crash-safety already applied to live gear and character profiles in v0.45.0.

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.60.0...v0.61.0

## v0.60.0 — 2026-06-13

**No more phantom blank character.** A non-profile file that lives alongside the character profiles (the tombstones record) was being read as if it were a character, producing an empty "ghost" entry — which could appear in the character switcher and also blocked first-run import of legacy data. The profile loader now ignores anything without a real name/slug, so only your actual characters show.

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.59.0...v0.60.0

## v0.59.0 — 2026-06-13

**Reliable "load build from a past session" for long sessions.** Reading a stored session back used a single file read that the OS is allowed to satisfy *partially* — so a long play session (a large slice of the log) could replay truncated, silently dropping the tail and leaving gear out of the rebuilt loadout. It now reads the full session exactly. Short sessions were already fine; this fixes the large-session case.

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.58.0...v0.59.0

## v0.58.0 — 2026-06-13

**Hardened the auto-updater's version handling.** The updater derived a release's version by splitting its filename on `-`, which would mishandle any hyphenated tag (e.g. a pre-release like `v1.0.0-rc1` was truncated to `v1.0.0`) — mislabeling the update and potentially renaming or cleaning up the wrong staged file. It now extracts the full tag correctly and compares the numeric version even when a pre-release suffix is present. (Releases use clean `vX.Y.Z` tags today, so this was a latent foot-gun rather than an active bug.)

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.57.0...v0.58.0

## v0.57.0 — 2026-06-13

**Tidier "Do Next" guidance.** If you owned several upgrades for the same slot (say four pairs of boots all better than what you're wearing), the Do Next list showed a separate "Equip" step for *each* — cluttering the list when you only need one. It now shows a single Equip step per slot — the best one (most of the build's wanted affixes) — and notes how many more sit in your bags, e.g. "already in your bags (+3 more owned)".

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.56.0...v0.57.0

## v0.56.0 — 2026-06-13

**Endgame weapon-power nudge.** At endgame (once your Torment tier is known), if an equipped weapon is below the Item-Power cap, its cell now notes that a capped weapon of the same type is a flat DPS upgrade — base weapon damage scales directly with Item Power, so closing that gap is one of the highest-value endgame steps. Advisory only; it doesn't show on already-capped weapons or before endgame.

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.55.0...v0.56.0

## v0.55.0 — 2026-06-13

**Consistent monochrome iconography.** A few spots used full-color emoji (🗑 clear, 📌 pin, 👁 OCR) that clashed with the app's cool, dark, monochrome palette. They're now monochrome glyphs in keeping with the rest of the UI: ✕ (clear), ＋ (pin to compare), ◉ (OCR — matching the ⌨ screen-reader icon beside it). Purely cosmetic.

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.54.0...v0.55.0

## v0.54.0 — 2026-06-13

**Header reflows on narrow windows.** When the window is narrow (below ~900px), the row of action buttons (Import, Profile, Character, Open on Maxroll, Builds, ⚙) used to squeeze the build-search box down to a sliver. The search box now takes the full width and the buttons wrap onto their own row beneath it. At normal widths nothing changes — search and buttons stay side-by-side.

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.53.0...v0.54.0

## v0.53.0 — 2026-06-13

**Safer "Adopt stray log".** After you move the capture log, the shim may keep writing to the old location until the game restarts; the banner offers to adopt that stray content. Previously it appended the stray's raw bytes onto the live log the app is actively tailing — which, if the stray contained a session "attach" marker, could **wipe your captured loadout**, or mis-parse a tooltip split across the splice. It now parses the stray separately and merges the result into your gear, leaving the active log untouched.

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.52.0...v0.53.0

## v0.52.0 — 2026-06-13

**More robust build-class detection.** When importing a Maxroll build, the class is derived from the planner's first skill token. A malformed or empty token previously stored a garbage class string (which mislabels the build and silently disables class-aware gear filtering). The class is now validated against the eight known D4 classes — anything unrecognized falls back to "unknown" (fail-open) rather than poisoning the build. Verified against real imported builds, which resolve to the correct class.

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.51.0...v0.52.0

## v0.51.0 — 2026-06-13

**Reliability: correct handling of accented names in the live capture.** The log tailer decoded each ~½-second read independently, so a UTF-8 multibyte character (any accented letter in an item or character name) that happened to straddle a read boundary decoded to garbage — which could then mis-key an item against its deduplication/tombstone/profile records. The tailer now streams UTF-8 across reads, reassembling split characters correctly. (English/ASCII names were never affected.)

Internally this also adds a `FeedBytes` test seam and a regression test that splits an accented name mid-character — the previously-untested byte path now has coverage.

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.50.0...v0.51.0

## v0.50.0 — 2026-06-13

**Long item names no longer dropped.** The parser rejected any item whose name exceeded 64 characters — long enough that names with multiple affix prefixes/suffixes could be silently skipped, leaving the item uncaptured. The ceiling is raised to 96 (the all-caps check still guards against non-name lines), with a regression test.

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.49.0...v0.50.0

## v0.49.0 — 2026-06-13

**Readability: brighter secondary text.** The dimmest text tier (`Faint`) — used for slot labels, the icon legend, retention hints, and other informational micro-text — was below comfortable contrast on the dark panels. It's now a touch lighter (still clearly the dimmest tier, still cool-gray), so those labels read cleanly without changing the app's dark, amber-accented look.

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.48.0...v0.49.0

## v0.48.0 — 2026-06-13

**Owned uniques now show their roll quality** (audit item A6, validated against real data).

A unique you own still counts as met (you have it) — but in Season 13 a unique also rolls *secondary* affixes, and a copy can roll badly for your build. Previously a badly-rolled owned unique showed a silent ✓ with no hint it could be better.

Now, when you own one of your build's target uniques, it carries an **advisory note** showing how many of the build's wanted secondaries your copy actually has — e.g. *"4/5 build secondaries · missing Armor"*, or *"0/2 — chase a better-rolled copy."* The headline completion % is unchanged (presence still counts it), so this is purely an "you could do better here" signal, not a penalty.

Validated against the real Dance-of-Knives build + captured loadout: Shrouded Gift correctly reads 4/5 (missing Armor), Sea Lord's Fine Gloves 1/2 (missing Core Skills).

Also adds two headless-validation seams (no effect in normal use): a `--live <live.json>` flag on the CLI and a `D4_RENDER_LIVE` render env var, so a real captured loadout can be diffed/rendered directly.

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.47.0...v0.48.0

## v0.47.0 — 2026-06-13

**Build-understanding correctness (verified subset of audit phase 3).** This phase's remaining items change scoring/filtering and are being held for real-build verification first (see `AUDIT-2026-06.md`) — what shipped here is the safe, confirmed set.

- **Mythics now read as full Greater-Affix items** even when their tooltip didn't carry a temper line (a Mythic is always all-GA). Previously a Mythic captured from a comparison/bag hover could show no GA stars and an unknown GA count.
- **Aspect completion no longer double-counts.** If the same aspect is imprinted on two slots, it's now counted once in the Aspects total. (The widely-suspected "gear vs. Aspects" double-count turned out not to exist — the per-slot aspect is display-only — so the fix is just the genuine same-aspect-twice case.)
- **The endgame Ancestral IP floor (900) is centralized** into the season-data pack, so next season's itemization change is a one-line data edit instead of a constant hunted across the code. No behavior change this season.

Held for verification (named in the audit): scoring a unique's secondary rolls, completing the per-class weapon matrix, hardening Maxroll class detection, weapon-type-keyword consolidation, and the umbrella-match constraint — each changes the headline % or gear filtering and will be validated against real builds before shipping.

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.46.0...v0.47.0

## v0.46.0 — 2026-06-13

**UX clarity pass** — the second wave from the deep audit (`AUDIT-2026-06.md`). These make the app easier to understand at a glance, especially when something isn't working yet.

- **No more silent empty paper doll.** When a build is loaded but no gear has been captured, the app now explains why and what to do — distinguishing "capture isn't set up yet" (→ a Set-up button) from "connected, nothing seen yet" (→ "open Diablo IV's character screen and hover your gear", with a Diagnose button). This was the single most likely "is it broken?" moment.
- **"Pin" no longer means two things.** The title-bar always-on-top button is now labeled **On top** (with a tooltip), freeing "Pin" for the slot-compare feature it collided with.
- **Text links look clickable.** Links like "Export loot filter", "← Overview", and "Build spec" now underline on hover instead of reading as plain text.
- **Help now teaches the mouse, not just the keyboard.** The `?` overlay gained an **Interactions** section: click a slot to pin & compare, hover to peek, the My Gear / Target / All Items tabs, and what the ↑ upgrade badge means.

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.45.0...v0.46.0

## v0.45.0 — 2026-06-13

**Correctness & data-safety pass** — the first wave from a deep code audit (see `AUDIT-2026-06.md` for the full ranked list). Nothing here changes how the app looks; it fixes cases where it showed wrong information, could lose data on a crash, or got stuck.

**Stopped showing wrong info**
- **Mythic uniques can no longer be labeled "Junk."** A Mythic is always Ancestral, so it never trips the "below the 900 Ancestral floor" rule — even if its tooltip didn't voice the word.
- **Skill ranks now match the build.** `+3 Ranks to Core Skill` was parsed as the literal text "Ranks to Core Skill" and failed to match your build's target; it now resolves to the clean skill name. (+Ranks is one of the highest-impact affixes in the game.)
- **Closed a residual vendor-gear leak.** A gambled item at the Purveyor could, in a narrow case (its vendor marker scrolled out of view), be mistaken for worn gear. Worn detection now requires a positive character-panel signal; a genuine equipped item still resolves via its Unequip tail.

**Won't lose your data**
- **Crash-safe writes.** `live.json`, character profiles, and the active-character pointer are now written atomically (temp-file + rename), so a crash or power-loss mid-save can't truncate them into garbage and silently wipe a character's loadout/progress.
- **Self-healing caches.** A one-time network hiccup no longer latches gear icons to silhouettes for the rest of the session, and a truncated/corrupt icon or Maxroll-data cache is now detected and re-fetched instead of breaking every future import.
- **Replay no longer resurrects deleted items.** Rebuilding gear by replaying the TTS log stopped un-deleting bag items you'd cleared.

**Won't get stuck / annoy**
- **OCR scans can't overlap.** A slow scan no longer lets the next one start concurrently and race shared state.
- **The auto-update check no longer stacks.** It won't kick off repeated ~70 MB downloads when settings change in quick succession.
- **Esc closes the All-Items and Update dialogs** (it already closed every other overlay).
- **The status bar no longer clips** its text with an ellipsis — it wraps, with the full text on hover.

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.44.6...v0.45.0

## v0.44.6 — 2026-06-13

**Fixed: the paper doll stretching too wide for Barbarian (and Rogue).**

Every weapon was laid out in a single horizontal row. Because each weapon cell is a fixed width, Barbarian's four-weapon arsenal ran about ~1,024px — far wider than the rest of the doll above it — stretching the whole view out sideways. Rogue's three weapons were borderline-wide too.

Weapons now arrange themselves by count:

- **1–2 weapons** (Sorcerer / Necromancer / Druid / Spiritborn) — a single centered row, exactly as before.
- **3–4 weapons** (Rogue / Barbarian arsenal) — a centered **2-column grid**: Barbarian's 4 become a tidy **2×2** (two-handers on top, one-handers below), and Rogue's 3 show two on top with the third centered beneath.

This caps the weapons area at roughly two cells wide so it sits neatly under the doll instead of pushing the panel out.

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.44.5...v0.44.6

## v0.44.5 — 2026-06-13

**Settings polish: themed session picker, edge-hugging scrollbar, an animated diagnose bar, and a tidier retention row.**

A cluster of Settings-panel fixes:

- **Dark session picker** — the "Load build from a past session" dropdown was rendering as a blinding-white Windows default. It now uses a dark, themed combo box (and dropdown list) that matches the rest of the app.
- **Scrollbar to the edge** — the Settings scrollbar sat well inside the panel. It now hugs the right edge, while the close ✕, footer buttons, and scrolling content stay aligned.
- **Animated diagnose bar** — "Diagnose capture" already ran off the UI thread (so it no longer freezes the app while reading a large log); now its loading bar actually animates with a looping sweep, plus a note that the app stays responsive while the log is parsed.
- **Tidier retention row** — in the log-retention line, "days" now matches the weight of "MB" and "archives" instead of being folded into the dim rotation hint.

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.44.4...v0.44.5

## v0.44.4 — 2026-06-13

**Fixed: a gear slot showing as an empty/ignored cell.**

When a build targets one single-occupancy slot in two ways at once — a gear affix slot *and* a unique (e.g. the gloves slot wanting both affixes and the unique "Sea Lord's Fine Gloves") — the paper doll drew two cells for that one slot: your equipped item on one, and an empty phantom on the other. The empty duplicate looked like the slot was being ignored.

Single-occupancy slots (helm/chest/gloves/pants/boots/amulet) now collapse to one cell: My Gear shows your equipped item, Target shows the wanted unique. Weapons and rings are unaffected (they legitimately hold multiple items).

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.44.3...v0.44.4

## v0.44.3 — 2026-06-13

**Settings polish.**

- The log-retention number fields (rotate at / keep / max age) were vertically clipping their digits — fixed.
- "Clear logs" moved out of the deferred Save flow into the cache card as its own "Logs" row, cleared immediately by "Clear selected" (with a warning if you clear it together with the live gear cache — that would replay an empty log).
- The settings modal now has an always-visible Close button: the header ✕ is pinned and a labeled "Close" sits in the footer next to Save (both discard unsaved changes).

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.44.2...v0.44.3

## v0.44.2 — 2026-06-13

**Gear icons self-heal + honest socket message.**

- **Gear icons** were silhouetting because the Maxroll item database (which maps your gear names to icon art) had been deleted by a cache clear and only came back on the next build import. It now restores automatically on startup if missing, and clearing "Maxroll data" re-downloads it immediately — icons no longer stay broken.
- **Sockets**: the "socket info not captured — hover with Advanced Tooltips on" message was misleading. Diablo IV doesn't announce sockets on equipped gear at all (a filled gem socket is silent), so that advice couldn't help. The message is now honest: "sockets aren't voiced by D4 on equipped gear — can't confirm gems." Runeword sockets are still captured.
- **Cache clearing** already has its own button separate from Save (shipped in v0.44.1) — update to pick it up.

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.44.1...v0.44.2

## v0.44.1 — 2026-06-13

**Cache clearing now has its own button** — separate from Settings Save.

The cache section was the last thing still bundled into the deferred "Save" flow. It now has its own **Clear selected** button inside the cache card that applies immediately — like the other one-shot actions (Diagnose, Scan now, Open log folder). Checking a cache row no longer appears in the pending-changes list and no longer waits for Save. The live-gear rebuild still confirms first, then runs right away.

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.44.0...v0.44.1

## v0.44.0 — 2026-06-13

**Log management** — Phase 6, the final phase of the TODO fix-up plan. All 23 TODOs are now shipped.

- The TTS log rotates into dated archive files with configurable retention (rotate at 8 MB, keep 12 files, max 90 days by default) — only at app start with the game closed, or at session end, never mid-session
- The log location is MOVED, not re-pointed: the file and its archives relocate, the game follows via an environment variable at its next launch, and a banner watches for (and can adopt) stray writes from a stale launch
- Load your build from any past session: a Settings combo lists every session across all log files; "View build" opens a read-only preview of that loadout with one click back to live
- "Clear gear" rebuilds now replay archived logs too, so rotation never silently shrinks your history
- New "Clear logs" staged action (also removes the legacy 55 MB d4_tts.jsonl side-car)

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.43.0...v0.44.0

## v0.43.0 — 2026-06-12

**Settings, deferred** — Phase 5 of the TODO fix-up plan. Nothing applies until you hit Save.

- Every control edits a draft; a footer lists your pending changes in plain words. **Save** applies everything atomically (one watcher restart, prompts fire at Save); **Revert** resets the draft; **✕ / Esc** close without applying.
- The cache section is one card — checking a row stages the clear; Cancel is gone; "Build index" now clears the real cache file.
- Clearing live gear now REBUILDS your characters: it wipes the saved loadouts and replays the entire TTS log from the beginning (survives restarts mid-replay; never deletes the log itself; long-deleted items can't resurrect through it).
- Diagnose capture is a true viewer — open it, close it, and your unsaved settings are still there.

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.42.0...v0.43.0

## v0.42.0 — 2026-06-12

**The 100% baseline** — Phase 4 of the TODO fix-up plan. The roll-quality threshold is GONE; max roll is the universal target.

- An affix counts as met by PRESENCE (plus the build's own explicit minimum when it has one). Roll bars measure toward the max roll — perfection is a goal on the bar, not a warning. Imported builds no longer shower you with "under-rolled" flags.
- BUILD WANTS now shows the VALUE on every row: explicit "≥ X" minimums, else the "max X" roll target harvested from any copy you own anywhere — an honest "—" when nothing is known.
- New row treatments: below an explicit build minimum = red→yellow urgency ramp with the minimum as a tick on the bar; Greater Affixes = orange→purple "exceeds" treatment; affixes satisfied by an "All …" umbrella glow softly.
- The threshold slider is removed from Settings (and the hidden header one too); "under-rolled" is now "below build min" everywhere.

⚠ Behavior notes: roll-quality-only "POLISH" suggestions go quiet for imported builds (they carry no minimums); a hand-edited MinRollPercent in a saved build file is ignored; bag upgrades that win on roll quality alone still badge via a quality tiebreak.

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.41.0...v0.42.0

## v0.41.0 — 2026-06-12

**Item identity** — Phase 3 of the TODO fix-up plan.

- Genuine duplicates of a same-named item now appear as separate items whenever ANY of their content differs (rolls, item power, masterwork, tempers, sockets, aspect…) — stateful text (equipped, favorited, durability, sell value) never counts
- A masterwork-inflated stale re-scan of an item you upgraded still collapses away — and can never resurface as a phantom copy of your worn gear
- The green upgrade badge now names the concrete bag item(s) on hover and click-jumps straight to that item in All Items, expanded; "Better in your bags" rows jump too

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.40.0...v0.41.0

## v0.40.0 — 2026-06-12

**Navigation** — Phase 2 of the TODO fix-up plan.

- "Build spec" moved inline, left of the My Gear tab (was a header toolbar button); the spec view has its own ← Overview link
- The Builds button is now a pure switcher between builds you've already worked on
- Build search scopes to your active character's class by default — chips let you browse other classes; an explicit chip choice is respected
- Open-a-.json-file moved into the search dropdown footer and the welcome card

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.39.0...v0.40.0

## v0.39.0 — 2026-06-12

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.38.1...v0.39.0

## v0.38.1 — 2026-06-12

**Equipped-gear detection rewrite** — consolidates v0.38.0 – v0.38.1.

- Vendor-gear leak fixed (v0.38.0): items hovered at vendors/stash/bags no longer replace your worn gear. The standalone EQUIPPED voice line is no longer trusted alone (D4 voices it before bag/vendor hover names too); classification waits for the action tail across poll chunks; bare slot words can''t poison the panel state; Sell/Equip/Drop/Favorite now demote; a correct later sighting evicts a wrongly-equipped copy; slot ties break by recency.
- Icon extraction retries with backoff instead of giving up for the session when Diablo IV holds the game storage at startup (v0.38.1)
- DO NEXT letter-by-letter name wrapping fixed; hover compare card no longer clips off the window edge (v0.38.1)

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.37.0...v0.38.1

## v0.37.0 — 2026-06-12

**Smarter matching** — consolidates v0.35.0 – v0.37.0.

- Max-roll targets in Build Progress: every bar shows the perfect-roll goal (v0.35)
- Comparison display fixes: extras as real rows, uniques'' substance visible, harvested targets, sockets, icons (v0.36)
- Umbrella-affix matching: "All Stats" / "All Damage Multiplier" / "Resistance to All Elements" / "All Skills" now count toward the specific affixes they grant (v0.37)
- Unique/Mythic wanted-affix requirements visible — a missing unique shows exactly what to chase (v0.37)
- Socket-bar honesty: text and bar always agree, "not captured" stated plainly (v0.37)
- Genuine duplicates: a fresh spare you own is shown for comparison; stale masterwork re-scans are not (v0.37)

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.34.2...v0.37.0

## v0.34.2 — 2026-06-11

**Icons, tombstones & polish** — consolidates v0.33.0 – v0.34.2.

- Name-based icon resolution: every owned item resolves its real game art, not just build items (v0.33)
- Resurrect-proof All-Items clearing: account-wide tombstones survive re-hovers and restarts (v0.34)
- Mythic purple-pink rarity treatment, prominent Clear-shown, readable per-slot sockets/runes (v0.34.1 – v0.34.2)

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.32.0...v0.34.2

## v0.32.0 — 2026-06-11

**Season 13 guidance engine** — consolidates v0.27.0 – v0.32.0.

- Weapon-gated upgrades, aspect import fix, capture-channel merges, ancestral glow (v0.27)
- Guidance correctness purge + season pack: all recommendations rebuilt on researched Season 13 / Lord of Hatred mechanics (v0.28)
- Greater Affix model: count from the temper denominator + per-affix inference with masterwork-inflation correction (v0.29)
- Per-item verdicts: Equip / Fixable / Keep / Stash / Junk with a concrete next action (v0.30)
- Per-slot upgrade path: temper → enchant → masterwork → socket → imprint, ordered and costed (v0.31)
- Torment-tier capture + tier-gated guidance (v0.32)

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.26.0...v0.32.0

## v0.26.0 — 2026-06-10

**Compare & upgrade scoring** — consolidates v0.24.0 – v0.26.0.

- Presence-first scoring; shared-stash All Items across characters; hover deltas (v0.24)
- Two-handed weapon class tables — live-verified fix (v0.24.1)
- Unified compare UI: the build target and a numeric delta on every affix row (v0.25)
- Unique item details, honest skill targets, aggregate current/target (v0.25.1)
- Inline comparisons, Ancestral marking, salvage-upgrade detection, sort tiers (v0.26)

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.23.2...v0.26.0

## v0.23.2 — 2026-06-10

**Multi-character support** — consolidates v0.21.0 – v0.23.2.

- Per-character profiles, each with its own saved loadout (v0.21)
- Per-character target builds — switching characters switches the build (v0.22)
- Robust identity: name + class read off the character-select screen, paragon-based reconciliation, same-name disambiguation (v0.22.2 – v0.23.1)
- Fast startup: session-marker skip, async catch-up, splash screen (v0.23.2)

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.20.0...v0.23.2

## v0.20.0 — 2026-06-09

**Full-loadout tracking & visual identity** — consolidates v0.14.0 – v0.20.1.

- All Items table with by-affix filtering (v0.14)
- Real game-data icons extracted from your local D4 install — CASC + BCn decode, no network (v0.14.1)
- UI overhaul: cool-dark + amber palette, real-icon slot tiles (v0.15)
- Build Spec view (v0.16)
- Build Progress panel — a bar for every requirement in the build (v0.17)
- Accurate weapon-type matching; aggregated overall progress (v0.18)
- Paragon net effect: total attributes + level on the doll (v0.19)
- Skills + ranks tracking, real affix values, "wants N" skill targets (v0.20, v0.20.1)

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.13.0...v0.20.0

## v0.13.0 — 2026-06-06

**TTS capture matures** — consolidates v0.4.0 – v0.13.0.

- Tooltip-style compare cards and a tidier list view (v0.4)
- Release CI hardening for the self-signed shim (v0.5)
- Core test harness expansion — Substitutes, Activities, LootFilter (v0.6)
- Visual overhaul: paper-doll tabs, character backdrop, in-app update check (v0.6.7)
- App icon + GearParser regex fixes (v0.7)
- Fully offline capture (no cloud/Vision API), app log, status-bar detail (v0.8)
- No-text-clipping rule applied app-wide — wrap, never truncate (v0.8.2)
- Update modal + settings rework; item-classification fast paths (v0.9.x)
- TTS capture diagnostics view: raw → parsed → classified → displayed (v0.11)
- Live-gear merge logic extracted to Core, headlessly tested (v0.12)
- Affix accuracy & completeness: sockets/sets, hover-time freshness, capture-health verdict (v0.13)

**Full Changelog**: https://github.com/defessler/D4Scanner/compare/v0.1.0...v0.13.0

## v0.1.0 — 2026-06-01

**Initial release** — a live Diablo IV build tracker: import a Maxroll planner build, capture your equipped gear through the screen-reader (TTS) shim, and see HAVE vs NEED per slot with value-aware scoring.

