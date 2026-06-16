# Changelog

All notable changes to D4Scanner, newest first — consolidated to the significant update(s) per release, in version order. Runs of trivial or internal-only releases are grouped.

> **Versioning:** 0.x line. GitHub (Latest badge) and the in-app auto-updater are semver-aware, so 0.100.0 > 0.99.0; only a bare lexical `git tag` sort misplaces v0.100+ (use `git tag --sort=-v:refname`).

## v0.103.0 — 2026-06-16
- Hover compare card anchoring — fixed and verified. The card now follows your cursor icon-to-icon, opening beside the hovered icon (right, flipping left, or above at a window edge), always keeping that icon fully visible and the card on-screen. Fixes the real bug v0.101/v0.102 missed: switching to a new icon left the card parked at the previous one (WPF doesn't re-run a popup's placement when only its target changes). Verified numerically with a new `D4_RENDER_HOVER` placement probe across 1400/1000/760px.

## v0.102.0 — 2026-06-16
- Reworked the hover compare card to anchor to the specific hovered icon, rather than beside the whole left-aligned paper doll (where v0.101 effectively still opened on the right). Shipped with remaining placement bugs — fully fixed and verified in v0.103.0.

## v0.101.0 — 2026-06-16
- Hover compare card gained a side-aware open (armor slots open left, rings/amulet/weapons open right), a ~90 ms open delay to stop flicker on a quick mouse sweep (instant switching once a card is open), a gentle fade-in, and Esc-to-close. Superseded same-day by v0.102/v0.103.

## v0.98.0–v0.100.0 — 2026-06-16
- Internal-only cleanup: Core simplifications (GearParser whole-word match helper, redundant DiffEngine.EvalSlot pass removed, de-duplicated replay self-heal) and removal of ~90 lines of dead WPF App code. No behavior change.

## v0.97.0 — 2026-06-16
- The "Update ready" prompt now always installs the exact version it names. Previously, if a staged update sat unrestarted and a newer release shipped, the app advertised the newest version but installed the older staged one; it now downloads/applies whichever version it shows and picks the newest staged build when several are present.

## v0.96.0 — 2026-06-16
- "Clear live gear cache" now rebuilds all characters, not just the active one. When the TTS log had been rotated into the logs\ archive, each archived character's worn loadout is now reconstructed into its own profile, keyed by name and class (so a same-named Rogue and Barbarian never merge).

## v0.95.0 — 2026-06-16
- Log-management audit — six fixes, two data-loss. Heavy-log days no longer delete your newest archives (rotation counter now sorts by true date/counter order, not lexical _1, _10, _2…). Moving the log to another drive is cross-volume-safe and rolls back cleanly; moving into a folder that already has a logs\ subfolder merges archives instead of stranding them.
- Also: re-acquired items reappear correctly in OCR-only mode after a cache clear; switching characters during a read-only session preview now works; and the session picker says "no past sessions yet" instead of spinning forever.

## v0.94.0 — 2026-06-15
- Alt+I no longer stacks a second All-Items window on top of the first; it refreshes the single open window.

## v0.93.0 — 2026-06-15
- Guidance-layer fixes: stop nagging "socket your gear" when wanted sockets are already filled (e.g. via a runeword); align the Substitutes upgrade badge with the main upgrade list on core-affix ties; restore "salvage for a Resplendent Spark" advice on a spare worn Mythic; and drop the nonsensical "a Rare/Legendary with the 0 core affixes" text on aspect-only slots.

## v0.92.0 — 2026-06-15
- Live Talisman tracking now works — equipped charms, seals, and runes surface during play instead of only on replay; charms with slot-word flavor text are no longer misread and dropped.
- A corrupt hand-edited season_pack.json (null list/section) no longer crashes the guidance panel; "All Stats" no longer masks a specific-stat roll that meets the bar; and a 2H weapon with one socket is correctly told it can hold a second.

## v0.91.0 — 2026-06-15
- Closes the last four §8 gearing-knowledge gaps (all 17 now fixed): impact-weighted completion %, Torment-gating caveats on temper/improve steps, Item-Power-aware weapon upgrades at endgame, and recognition of Transfigured (permanently unmodifiable) items.

## v0.90.0 — 2026-06-15
- Internal-only maintainability pass (21 behavior-preserving changes): de-duplicated recency/clamp/unique-power code, removed dead code, and made the OCR scanner resolve the game window once per frame and dispose its process handles.

## v0.89.0 — 2026-06-15
- OCR no longer leaks a Direct3D device per frame; the paper-doll compare card is cleanly re-anchored (no orphaning) when gear refreshes mid-hover.
- Spiritborn Quarterstaff now counts as two-handed in crafting-cost estimates; a stale System32-installed capture shim is now offered for upgrade; item-name display is locale-independent.

## v0.88.0 — 2026-06-15
- The compare card now opens in the clear space beside the whole paper doll (right, or left if it doesn't fit), sized to the gap and aligned to the hovered row, so it no longer covers the doll's other slot icons.

## v0.86.0–v0.87.0 — 2026-06-15
- Fixed paper-doll hover-card flicker: the card now closes only after the mouse leaves both the slot and the card (and you can move onto it to read/scroll), plus left/right hysteresis and a stable width so an appearing scrollbar can't make it flip sides.

## v0.85.0 — 2026-06-15
- Fixes the long-standing "rings (and dual-wield weapons) swap places on hover" bug: slot positions are now keyed by item name and anchored to Character-panel announcement order, so re-hovering reuses a slot instead of renumbering it. (Done in TTS/Core; OCR positioning was ruled out — equipped rings share one tooltip position.)

## v0.84.0 — 2026-06-15
- OCR fast-scan cadence now triggers on gear being on screen (a panel or a floating item tooltip) rather than only a named panel, so hovered items are captured reliably instead of falling between scans (active 3s→1.5s, idle 20s→6s).

## v0.83.0 — 2026-06-14
- OCR-grounded panel detection makes the worn-vs-vendor/stash classifier far more accurate (~77%→~98% on a real session): stash items no longer leak into worn gear, the Blacksmith/Horadric Cube screens read as crafting, and item-comparison tooltips and the stats sheet are recognized even when their titles OCR-garble.

## v0.82.0 — 2026-06-14
- Groundwork for OCR↔TTS fusion: adaptive OCR cadence and a new off-by-default "Save capture diagnostics" setting that logs recognized words with on-screen positions plus the matching TTS lines (self-limiting at 400 MB / 30 days).

## v0.81.0 — 2026-06-14
- Per-slot upgrade badges and Do-Next suggestions now apply the weapon-type gate, so a melee weapon is never suggested as an upgrade for a ranged slot (or a one-hander for a two-hand slot).

## v0.80.0 — 2026-06-14
- Live-testing UI fixes: hover compare cards clamp fully on-screen (both axes); the ✕ close button is clickable across its whole box; padding between Settings' Close/Save buttons; and the tempering (⚒) marker no longer gets clipped in the BUILD WANTS list.

## v0.79.0 — 2026-06-14
- Adds OCR↔TTS sensor fusion: when OCR is enabled it reads which panel is open and feeds that to the TTS classifier, keeping a genuinely-equipped item on the paper doll when TTS is unsure but OCR confirms the character sheet was open. Strictly additive (only promotes to worn, never removes); Armory and the Purveyor "Ring" gamble are excluded. Also stops OCR writing an unused character.png.

## v0.78.0 — 2026-06-14
- Diagnostic --render export is now read-only — it no longer overwrites your saved live.json loadout mirror on close. Only affects the --render developer mode.

## v0.77.0 — 2026-06-14
- Fixed Talisman charm capture: the Horadric Cube's "1x Set Charm" recipe line no longer spawns a phantom charm that hid your real ones, and a full charm set (e.g. 5-piece Legacy of the Sightless) now shows all charms with set-bonus tracking instead of collapsing to one.

## v0.76.0 — 2026-06-14
- Hovering a bag copy of a same-named item no longer knocks the equipped one off the paper doll — the self-heal eviction now compares full item content, not just name + slot.

## v0.75.0 — 2026-06-14
- New "What You Can Craft" section above Build Progress: lays out every available crafting move (temper, enchant, masterwork, capstone reroll, socket, imprint) across your whole loadout at once, each tagged with station, rough cost, and cautions.

## v0.74.0 — 2026-06-13
- Talisman card now prompts you to scan your Talisman in-game when gear is captured but no talisman is, making the feature discoverable. Caps the v0.71–v0.73 talisman work.

## v0.73.0 — 2026-06-13
- Talisman card gained a SET BONUSES section listing each set's active/total count (e.g. "Legacy of the Sightless 5/5"), with completed sets highlighted green.

## v0.72.0 — 2026-06-13
- New TALISMAN card in the guidance rail showing captured seal, charms, and runes with rarity and top affixes; the seal shows its charm-slot capacity. A "what you own" view only (Maxroll has no talisman targets).

## v0.71.0 — 2026-06-13
- Talisman charms of every rarity are now captured — the parser previously only recognized "Set Charm"/"Unique Charm" and dropped Rare, Magic, Legendary, and Ancestral charms.

## v0.70.0 — 2026-06-13
- Exported loot filter now leads with an Item-Power floor rule (keep Ancestral ≥900 at endgame) and a cross-slot priority-affix ranking; per-slot detail is unchanged below.

## v0.69.0 — 2026-06-13
- Added Torment 12 to the push ladder so a player on Torment 11 now gets "Push to Torment 12 — clear Pit 100" (previously got no suggestion); top-tier wording tidied.

## v0.68.0 — 2026-06-13
- Imported builds no longer show unresolved aspect ids (e.g. "BSK Barbarian 001 2") as junk aspect names or imprint steps — the importer now filters class-keyed ids from aspects, and builds imported before the fix self-heal on load.

## v0.67.0 — 2026-06-13
- Loot-filter exports (markdown and Diablo4Companion preset) now include slot-less flex/utility aspects, which were previously dropped; slot-pinned aspects are unchanged and not duplicated.

## v0.66.0 — 2026-06-13
- All Items now hides pure artifact rows (an item with no slot, type, rarity, or affixes) left over from old captures; anything with even one of those is always kept. Display-only — scoring and diff untouched.

## v0.65.0 — 2026-06-13
- Equipped-skill rows now show the game's "rank X/Y" (e.g. "rank 27/15") instead of mislabeling rank as "points", revealing your +Ranks bonus from gear and paragon.

## v0.64.0 — 2026-06-13
- Parser hardening: masterwork Quality, masterwork rank/max, temper charges, and required level now use the lenient never-throwing number parse like every other field, so a future malformed digit run can't fault the whole tooltip. No change on today's TTS format.

## v0.62.0–v0.63.0 — 2026-06-13
- Fixed native-memory leaks in the OCR capture path: the WGC frame-grab path (v0.62.0) and the OCR scanner's bitmap conversion plus DataWriter (v0.63.0) left full-screen SoftwareBitmaps (~33 MB at 4K) to the finalizer on every grab; all now disposed deterministically.

## v0.61.0 — 2026-06-13
- Crash-safe settings and build files: app.json and the imported build file now write to a temp file and atomically rename, so a crash mid-save can no longer corrupt your settings or loaded build.

## v0.60.0 — 2026-06-13
- Fixed a phantom blank character: the tombstones record stored alongside profiles was being read as an empty "ghost" character. The loader now ignores files with no real name/slug.

## v0.59.0 — 2026-06-13
- Fixed "load build from a past session" for long sessions: reading a stored session back used a single read the OS may satisfy partially, silently truncating large sessions and dropping gear. It now reads the full session.

## v0.58.0 — 2026-06-13
- Hardened the auto-updater's version handling: it now extracts the full release tag instead of splitting the filename on - (which truncated hyphenated pre-release tags like v1.0.0-rc1). Latent foot-gun fix — release tags are clean today.

## v0.57.0 — 2026-06-13
- "Do Next" now shows a single Equip step per slot (the best owned item) and notes how many more sit in your bags, instead of one step per owned upgrade.

## v0.56.0 — 2026-06-13
- Endgame-only advisory: a below-cap equipped weapon's cell now notes that a capped weapon of the same type is a flat DPS upgrade (base weapon damage scales with Item Power). Hidden on already-capped weapons and before endgame.

## v0.55.0 — 2026-06-13
- Replaced the few full-color emoji (clear, pin, OCR) with monochrome glyphs to match the dark palette. Cosmetic only.

## v0.54.0 — 2026-06-13
- On narrow windows (below ~900px) the header now gives the build-search box full width and wraps the action buttons onto their own row; unchanged at normal widths.

## v0.53.0 — 2026-06-13
- "Adopt stray log" now parses the stray content separately and merges the result into your gear instead of appending raw bytes onto the live log — which could wipe your captured loadout or mis-parse a spliced tooltip.

## v0.52.0 — 2026-06-13
- Maxroll import now validates the derived class against the eight known D4 classes, falling back to "unknown" rather than storing a garbage class string (which silently disabled class-aware gear filtering).

## v0.51.0 — 2026-06-13
- Fixed garbled accented names in live capture: the log tailer now streams UTF-8 across reads, reassembling multibyte characters that straddle a read boundary (which previously mis-keyed items against dedup/tombstone/profile records). ASCII names were never affected.

## v0.50.0 — 2026-06-13
- Raised the parser's item-name length ceiling from 64 to 96 characters so long multi-affix names are no longer silently skipped.

## v0.49.0 — 2026-06-13
- Brightened the dimmest text tier (slot labels, legend, hints) for better contrast on the dark panels. Cosmetic only.

## v0.48.0 — 2026-06-13
- Owned target uniques now carry a roll-quality advisory showing how many of the build's wanted secondaries your copy actually has (e.g. "4/5 · missing Armor"). Presence still counts as met, so the completion % is unchanged — it's a "you could do better" hint only.

## v0.47.0 — 2026-06-13
- Mythics now read as full Greater-Affix items even when their tooltip lacked a temper line (e.g. captured from a comparison/bag hover).
- Aspect completion no longer double-counts the same aspect imprinted on two slots; the endgame Ancestral IP floor (900) was centralized into the season-data pack.

## v0.46.0 — 2026-06-13
- UX clarity pass: an explanatory empty paper doll (capture-not-set-up vs nothing-seen-yet, each with the right action), the always-on-top button renamed "On top" (freeing "Pin" for slot-compare), hover underlines on text links, and a new Interactions section in the help overlay.

## v0.45.0 — 2026-06-13
- Correctness/data-safety pass: Mythics can no longer be labeled "Junk"; "+N Ranks to Core Skill" now resolves to the clean skill name and matches the build; closed a residual Purveyor vendor-gear leak.
- Crash-safe atomic writes for live.json, character profiles, and the active-character pointer; self-healing icon/Maxroll caches; replay no longer resurrects deleted items.
- OCR scans can no longer overlap, the auto-update check no longer stacks duplicate downloads, and Esc now closes the All-Items and Update dialogs.

## v0.44.6 — 2026-06-13
- Weapons now lay out by count to stop the paper doll stretching wide: 1–2 weapons in a centered row (unchanged), 3–4 in a centered 2-column grid (Barbarian's four become a 2×2, Rogue's three two-over-one).

## v0.44.4 — 2026-06-13
- Single-occupancy slots (helm/chest/gloves/pants/boots/amulet) that a build targets both by affix and by unique now collapse to one cell instead of drawing a phantom empty duplicate. Weapons and rings are unaffected.

## v0.44.2 — 2026-06-13
- Gear icons now self-heal on startup (and re-download immediately when the Maxroll data cache is cleared) instead of staying silhouetted until the next build import.
- The equipped-gear socket message is now honest: D4 doesn't voice sockets on equipped gear, so gems can't be confirmed (runeword sockets still are).

## v0.44.0–v0.44.5 — 2026-06-13 — log management + Settings polish
- v0.44.0: Log management (final TODO phase). The TTS log rotates into dated archives with configurable retention; the log location is moved (file + archives) with the game following via env var and a stray-write adopt banner; load your build from any past session via a read-only preview; "Clear gear" rebuild replays archived logs too; new "Clear logs" action.
- v0.44.1/v0.44.3/v0.44.5: cache clearing got its own "Clear selected" button (out of the deferred Save flow), plus Settings fixes — pinned/footer Close buttons, dark session picker, edge-hugging scrollbar, animated diagnose bar, and clipped retention/number fields fixed.

## v0.43.0 — 2026-06-12
- Settings reworked to be deferred: every control edits a draft, a footer lists pending changes, and Save applies everything atomically (one watcher restart). "Clear live gear" now wipes saved loadouts and rebuilds characters by replaying the whole TTS log.

## v0.42.0 — 2026-06-12
- Removed the roll-quality threshold entirely: max roll is the universal target, affixes count as met by presence (plus any explicit build minimum), and BUILD WANTS now shows a value on every row. New row treatments for below-minimum, Greater Affixes, and umbrella-covered affixes; the threshold slider is gone from Settings.

## v0.41.0 — 2026-06-12
- Genuine duplicates of a same-named item now show as separate items whenever any real content differs (stateful text like equipped/favorited/durability never counts), while a masterwork-inflated stale re-scan still collapses away.
- The green upgrade badge now names the concrete bag item(s) on hover and click-jumps to that item in All Items.

## v0.40.0 — 2026-06-12
- Navigation pass: "Build spec" moved inline beside the My Gear tab (with its own ← Overview link); the Builds button became a pure switcher; build search now scopes to the active character's class by default with chips to browse other classes; open-a-.json moved into the search dropdown and welcome card.

## v0.39.0 — 2026-06-12
- Diagnose capture now runs on a background thread behind a progress modal instead of freezing the app while it re-reads the whole TTS log.
- Fixed the green upgrade badge clipping at the icon-tile edge; added an "Open log folder" button; reformatted the Aspect/Unique power boxes (noise lines dropped, sentences wrapped, roll numbers bold, flavor dimmed).

## v0.38.0–v0.38.1 — 2026-06-12 — equipped-gear detection rewrite
- v0.38.0: Fixed the vendor-gear leak — items hovered at vendors/stash/bags no longer replace worn gear. A standalone EQUIPPED voice line is no longer trusted alone, classification waits for the action tail across poll chunks, bare slot words can't poison panel state, Sell/Equip/Drop/Favorite demote, and a correct later sighting evicts a wrongly-equipped copy.
- v0.38.1: Icon extraction retries with backoff (instead of giving up for the session) when D4 holds the game storage at startup; fixed Do-Next name wrapping and the hover compare card clipping off the window edge.

## v0.37.0 — 2026-06-12
- Umbrella-affix matching: "All Stats", "All Damage Multiplier", "Resistance to All Elements", and "All Skills" now count toward the specific affixes they grant.
- A missing unique/Mythic now shows exactly which wanted affixes to chase; socket text and bar always agree ("not captured" stated plainly); genuine spare duplicates show for comparison while stale masterwork re-scans don't.

## v0.36.0 — 2026-06-12
- Comparison display fixes: extra affixes render as real rows, uniques' substance is visible, roll targets are harvested from owned copies, and sockets/icons display correctly.

## v0.35.0 — 2026-06-12
- Build Progress now shows the perfect-roll (max-roll) goal on every affix bar.

## v0.32.0 — 2026-06-11
- Captures your current Torment tier and gates guidance on it (tier-appropriate crafting/farming advice).

## v0.31.0 — 2026-06-11
- Added a per-slot upgrade path: an ordered, costed crafting plan (temper → enchant → masterwork → socket → imprint) for each slot.

## v0.30.0 — 2026-06-10
- Added per-item verdicts — Equip / Fixable / Keep / Stash / Junk — each with a concrete next action.

## v0.29.0 — 2026-06-10
- Greater Affix model: item-level GA count derived from the temper denominator plus per-affix inference (with masterwork-inflation correction), surfaced as ★ markers in scoring and the UI.

## v0.28.0 — 2026-06-10
- Guidance correctness purge: Activities, Infernal Hordes advice, Substitutes, and BuildGuide rebuilt on researched Season 13 / Lord of Hatred mechanics.
- Season-volatile guidance data extracted to a versioned season_pack.json, so seasonal numbers are a data edit rather than a code change.

## v0.27.0 — 2026-06-10
- Upgrades are now weapon-type gated, and Maxroll aspect import resolves real aspect names.
- Capture-channel merges made safe (TTS/OCR), Ancestral items get a distinguishing glow, and compare rows show a numeric delta.

## v0.26.0 — 2026-06-10
- Consolidates v0.24.0–v0.26.0: presence-first upgrade scoring, a shared-stash All Items view across characters, and hover deltas (v0.24); two-handed weapons get their own per-class tables — a live-verified fix (v0.24.1).
- Unified compare UI showing the build target and a numeric delta on every affix row, plus unique-item detail and honest skill targets (v0.25.x); inline comparisons, Ancestral marking, salvage-upgrade detection, and sort tiers (v0.26).

## v0.23.2 — 2026-06-10
- Consolidates v0.21.0–v0.23.2: per-character profiles, each with its own saved loadout (v0.21) and its own target build that switches with the character (v0.22).
- Robust character identity: name + class read from the character-select screen, paragon-based reconciliation, and same-name disambiguation (v0.22.2–v0.23.1); faster startup via session-marker skip, async catch-up, and a splash screen (v0.23.2).

## v0.20.0 — 2026-06-09
- Consolidates v0.14.0–v0.20.1: All Items table with by-affix filtering (v0.14); real game-data item icons extracted from the local D4 install (CASC + BCn decode, no network) (v0.14.1).
- UI overhaul to the cool-dark + amber palette with real-icon slot tiles (v0.15), a Build Spec view (v0.16), and a Build Progress panel with a bar for every build requirement (v0.17).
- Accurate weapon-type matching and aggregated progress (v0.18), paragon net effect (total attributes + level) on the doll (v0.19), and skills/ranks tracking with real affix values (v0.20).

## v0.13.0 — 2026-06-06
- Consolidates v0.4.0–v0.13.0 (TTS capture matures): capture goes fully offline — no cloud/Vision API — with an app log and status-bar detail (v0.8), and a no-text-clipping rule applied app-wide (v0.8.2).
- A TTS capture-diagnostics view (raw → parsed → classified → displayed) (v0.11) and live-gear merge logic extracted to Core for headless testing (v0.12).
- Affix accuracy and completeness: sockets/sets, hover-time freshness, and a capture-health verdict (v0.13); earlier work added tooltip-style compare cards, the paper-doll/tabs visual overhaul, and an in-app update check (v0.4–v0.9).

## v0.1.0 — 2026-06-01
- Initial release: import a Maxroll planner build, capture equipped gear via the screen-reader (TTS) shim, and see HAVE vs NEED per slot with value-aware scoring.

