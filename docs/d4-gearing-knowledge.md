# Diablo IV gearing knowledge base — how to guide the player

**Researched 2026-06-10.** Live game: **Season 13 "Season of Reckoning"** on the **Lord of Hatred
expansion** (patch 3.0.3). Season 14 / patch 3.1 lands **~June 30, 2026** and reworks Mythic
Uniques — re-verify everything stamped ⚠S14 then.

Provenance: 7 parallel web-research passes (Maxroll resource pages updated 2026-05-17…06-03 were
the anchor sources, corroborated by Icy Veins S13 guides, Wowhead, Blizzard news) + an audit of
this app's own guidance code. An independent fact-check pass could not run, so each claim carries
the researcher's confidence; conflicts are flagged inline. **Cross-checked against the user's live
captures**, which independently confirm: IP 850 (normal) / 900 (Ancestral) items, "… Multiplier"
affixes, a Warlock character (LoH class), and Talisman charm/seal tooltips.

---

## 1. Current game state (the freshness anchor)

| Fact | Value | Conf |
|---|---|---|
| Expansion | Lord of Hatred (2nd expansion), launched 2026-04-27/28 with patch 3.0.0 | high |
| Season | S13 "Season of Reckoning" — no seasonal gimmick; everything it added is permanent | high |
| Classes | **8**: Barbarian, Druid, Necromancer, Rogue, Sorcerer, Spiritborn, **Paladin, Warlock** | high |
| Level cap | **70** (was 60); paragon decoupled from level, 300 pts + 42 from season ranks | high |
| Difficulty | **Torment I–XII** (was I–IV), unlocked sequentially via Pit tiers (T1≈Pit 10 … T12≈Pit 100) | high |
| Item Power | **850 cap normal / 900 Ancestral** (was 750/800); no in-between breakpoints | high |
| S14 incoming | Mythic Uniques 3.0 (Mythic becomes a quality modifier, +30% power), cube rerolls on uniques/charms, Pandemonium Ruptures, SSF, obol cap up | ⚠S14 |

Seasons since the app's knowledge froze (S8, Apr 2025): S9 Sins of the Horadrim (Strongrooms +
Escalating NMDs → **permanent**), S10 Infernal Chaos (removed), S11 Divine Intervention (**gearing
rework**: 1 temper slot, pick-your-affix, Quality masterworking; Tower launched), S12 Slaughter
(removed), S13 = LoH systems. Removed seasonal powers must never be recommended.

**Stale-data tripwires** — any source or cached constant mentioning these is pre-2026 and wrong:
IP cap 750/800/925 · "every Ancestral has ≥1 GA" · Torment I–IV · level cap 60 · 12-rank
masterworking with 4/8/12 crits · 2 temper slots · "uniques can't be tempered" · fixed unique
affixes · per-boss summon materials (Living Steel etc.) · 1000-armor / 70%-resist caps ·
6-class roster.

## 2. Item system

- **Rarity ladder**: Common → Magic (1–2 affixes) → Rare (3) → Legendary (4 + aspect) → Unique
  (4 + unique power) → Mythic Unique (always Ancestral, all-GA affixes + mythic power).
  *(Legendary affix count 4 vs the older 3 has minor source conflict — verify from a live tooltip.)*
- **Ancestral** = IP 900, drops from Torment I. **S13 removed the guaranteed Greater Affix on
  Ancestrals** — "Ancestral" alone is no longer a quality signal; a 0-GA Ancestral is just a 900-IP
  item (still strictly better than 850 gear). Magic/Rare can now drop Ancestral with GAs too —
  they're Horadric Cube crafting bases, not vendor trash.
- **Greater Affixes (GA)**: roll at **1.5× the affix's max**, star icon + Roman numeral in the name,
  1–4 per item. Each GA also grants **+1 temper charge**. Enchanting a GA line **destroys the GA
  permanently**; enchanting can never create one. A GA on an off-build stat is worth ~nothing
  (plus the temper charge) — players grade by *GA-on-wanted-affix count*, not raw GA count.
- **Item Power at endgame is binary**: 850 vs 900. It matters most on **weapons** (base DPS scales
  with IP); on armor/jewelry affix match dominates. Above the tier cap, IP carries no signal.
- **Torment tier gates** (what can drop where): T1 Ancestrals · T2 Legendary temper manuals ·
  T4 Neathiron · T6 Greater Lair Keys · T8 Unique Charms + GA odds rise sharply · T10 Mythic
  Seals · T11 Resplendent Sparks · T12 everything/best rates. T10 ≈ XP sweet spot, T12 = gear farm.

## 3. Crafting (the actions we can recommend instead of "farm a new one")

**Canonical order on a keeper item: TEMPER → ENCHANT → MASTERWORK → (sockets/aspect anytime) →
TRANSFIGURE last (irreversible).**

- **Enchanting (Occultist)** — rerolls exactly **one affix, forever** (after the first enchant only
  that line can ever be rerolled). Two offers + "keep current" per attempt; gold cost escalates
  (capped); makes the item account-bound. Cannot touch tempered lines. Cannot create GAs.
  → *1 wrong affix = fixable; 2+ wrong = replace (or expensive Horadric Cube fallback).*
  S13 wrinkle: uniques roll random secondary affixes and **one** unique secondary may be
  replaceable at the Occultist (medium conf — verify in-game).
- **Tempering (Blacksmith)** — **1 tempered affix per item** (was 2); **Rare, Legendary, Unique AND
  Mythic are temperable** (uniques new in 3.0). You **choose the exact affix** from a learned
  manual (S11+); only the value rolls. Charges: 3 base + 1 per GA (max 7); Rare items 1.
  **Bricking is dead**: a **Scroll of Restoration** (Dark Citadel 2,000 coins / Infernal Hordes
  "Spoils of Greater Equipment" / World Bosses) refills charges once they're all spent.
  Manuals are account-wide once learned — never stash manuals for alts. Tempers have a small
  chance to roll AS a GA on Ancestrals (conflicting sources — treat as bonus, not plan).
- **Masterworking = "Quality" system (S11 rework)** — Ancestral Legendary/Unique only.
  Each upgrade adds a random 2–5 Quality, **max 25**;每 rank = **+1% to base stats and ALL affix
  values**; at Quality 25 a **Capstone** gives **+50% to one random affix** (tempered lines
  eligible; stacks with a GA's +50%). Obducite per upgrade = `floor(3.75·Q + 10)` (≈500–1,400
  total to cap; ×2 for 2H). Quality never resets; only the capstone rerolls (cost conflicts:
  Maxroll 100–200 Obducite + 10M gold vs Icy Veins 1,000 + 1M — verify in-game).
  **The old 12-rank / 4-8-12-crit model no longer exists.**
  The TTS "Quality" tooltip line (`50 (+30/25) Quality`) is this system: the `/25` is the rank cap
  (leading-number composition unverified — likely effective % including bonuses).
- **Sockets (Jeweler)** — max: Helm/Chest/Pants/2H = 2; 1H/offhand/amulet/each ring = 1;
  gloves/boots = 0. Add a socket for **1 Scattered Prism** (goblins, world bosses, legion events).
  Gem tiers: …→ Royal → Grand (+ new "Horadric Gem" = 5 Grands via cube). **Runewords** = Ritual
  rune + Invocation rune in one 2-socket item (Helm/Chest/Pants/2H); max 2 equipped, no duplicate
  runes; gems and runes are mutually exclusive per item.
- **Horadric Cube (LoH, permanent)** — deterministic affix crafting in Primordial Dust currencies:
  Add Affix · **Focused/Chaotic Reroll** (the fallback when the enchant slot is burned) · Remove
  Affix · Upgrade Rare→Legendary · Common→random Unique · **recycle 3 identical Uniques → fresh
  random-rolled copy** (the multi-GA unique path) · Unique Power Reroll · rune/gem amalgamation.
  Tuning Prisms steer outcomes. **Transfiguration**: one random jackpot (~35% bonus affix, ~20%
  indestructible, ~20% bonus quality, ~15% upgrade an affix to GA, ~10% replace affix) then the
  item is **permanently unmodifiable** — strictly the last step.
- **Talisman (LoH, permanent)** — 1 Seal + up to 6 Charm sockets; Seal rarity gates capacity
  (Magic 3 / Rare 4 / Legendary 5 / Mythic 6). Set bonuses return via Set Charms; a Unique can be
  converted to a Unique Charm (power carries, affixes don't). **The app's S8 'seal'/'charm' parser
  types coincidentally still apply but the tooltip formats are from the new system.**
- **Trading lock**: any craft (temper/enchant/masterwork/cube) makes the item untradeable;
  Mythics always bound. A clean god-roll the player might sell should stay unmodified.

## 4. Aspects, uniques, mythics

- **Codex of Power**: salvaging a Legendary **permanently stores the best rolled value** of its
  aspect, account-wide. Imprints from the codex are **unlimited** (gold + mats; not free).
  In-game, a crossed-swords tooltip icon marks codex-upgrade drops.
  → *Salvage any legendary whose aspect roll beats the stored best — even off-build. Never sell those.*
- **Imprint slot matrix** (stable): Offensive = weapons/gloves/ring/amulet · Defensive =
  helm/chest/pants/shield/amulet · Resource = ring/amulet · Utility = helm/chest/pants/shield/
  gloves/boots/amulet · Mobility = boots/amulet. **Amplification: ×2 on 2H weapons, ×1.5 on amulet.**
- **Uniques/Mythics can NOT be imprinted** (confirmed — the app's AspectBlocked rule is right).
  Only fringe exception: cube Kullean prism adds a *random* Utility aspect to an amulet.
- **Uniques are no longer fixed**: since 3.0 secondary affixes roll randomly per copy — two copies
  of the same unique differ. Evaluate uniques like rollable items (roll ranges, GA count, temper
  state). A well-rolled legendary can legitimately beat a badly-rolled copy of the build's unique.
  Bad-roll fixes: temper it, cube Unique Power Reroll, or collect 3 copies and cube-recycle.
- **Mythics**: always max-rolled — owning it is the whole gap. Acquisition: lair bosses (~2%,
  **Belial best**), Mythic Tribute of Armaments (Undercity, near-guaranteed), or craft with
  **2 Resplendent Sparks** (random cache at Blacksmith; targeted rune recipe at Jeweler).
  Sparks: season journey (up to 14), first Lilith kill, salvaging a duplicate Mythic (+1 each).
  ⚠S14 reworks all of this.

## 5. Stats — what to prioritize when several gaps exist

1. **Build-defining uniques/aspects** missing entirely.
2. **+Ranks to key skills** (gloves/amulet/charms) — highest-leverage affix in the game.
3. **"… Damage Multiplier" affixes** (S13 addition: Crit/Vulnerable/All/Element/DoT Multiplier are
   true [x] multipliers). Same wording adds; different wordings multiply →
   **filling an empty multiplier category beats stacking an occupied one**.
4. Crit chance / Attack Speed **until their 100% caps** (two speed pools by wording: "Attack
   Speed" vs "Cast Speed", 100% each; per-skill breakpoints exist).
5. Main stat (1% dmg per 8 points; Barb per ~9.1) / Maximum Life.
6. Plain [+]% additive damage — one shared bucket with diminishing returns
   (marginal value ∝ 1/(1+bucket)) — always last.

**Defense (LoH rework — old caps GONE)**: armor and each resistance are *ratings* with asymptotic
DR = `V/(V·10/9 + C)`, C=5678 armor / 1136 resist at lvl 70 (≤90% asymptote). Priority: Max
Life/Barrier → fix the single **worst** elemental resistance (the sheet's combined "Toughness" is
misleading) → armor → discrete DR multipliers (few big > many small). Community T12 target ≈ 2–3M
Toughness (softcore; secondary source).
**Overpower** is now a stacking mechanic (15%[+] per stack, cap 4, 4s) — old model gone.
**Paragon/glyphs**: the post-gear power faucet. Glyphs level **only in the Pit** (3 attempts/clear,
+1 deathless); guaranteed +1 when Pit tier ≥ glyph level + 10; +1 extra level per 20 tiers above.
⚠Conflict: glyph cap 50 (most S13 sources, Legendary upgrade at 45) vs 150 (one Icy Veins page) —
verify in-game before encoding.

## 6. Endgame activities → what they uniquely pay (S13)

| Need | Send the player to |
|---|---|
| Glyph XP / paragon | **The Pit — exclusively** (tier ≥ glyph+10) |
| Obducite (masterwork) | **Nightmare Dungeons** (Treasure Breach sigils, Strongrooms) > Undercity *Tribute of Refinement* > Infernal Hordes. **NOT the Pit** (Ingolith no longer exists) |
| Neathiron (capstone) | World Bosses, Bartuc, Astaroth, Belial, Greater Refinement tribute |
| Forgotten Souls (enchant) | **Helltide** (also Hordes Spoils of Material) |
| Temper manuals | Tree of Whispers caches (start dropping T2) |
| Scroll of Restoration | Dark Citadel (2,000 coins) / Hordes Greater-Equipment spoils / World Bosses |
| Scattered Prisms | Goblins, World Bosses, Legion events |
| Missing aspect | Salvage spares; Undercity *Armaments* tributes; obol-gamble the cheapest carrying slot |
| Missing unique | Its **dedicated lair boss** (see below); fallback **Belial** (choose-any-table, guaranteed Ancestral Unique) |
| Mythic | Belial / Mythic Tribute of Armaments / 2-spark craft |
| GA hunting / under-rolled | Push Torment (odds jump T8+); **Tower** "Treasures of the Artificer" caches = **guaranteed-GA items**; Greater Armaments tributes |
| Multiple needs | **War Plans** (Temis): playlist of up to 5 activities with stacked bonus rewards — the meta-recommendation |

**Boss ladder (3.0)**: no summon materials — keys open the post-kill Hoard. Initiate (1 Lair Key):
Varshan, Grigoire, Beast in the Ice, Lord Zir, Urivar. Greater (1 Greater Lair Key; from Initiate
kills): Duriel, Andariel, Harbinger of Hatred, Butcher. Exalted: Belial (2 Betrayer's Husks),
Astaroth (Escalation Sigil), Bartuc (Hordes, 666 Aether), Mephisto (per Icy Veins). Every unique
has **one dedicated boss** — keep the unique→boss table as per-season *data*, synced from
Maxroll's cheat sheet, not hardcoded.
**Infernal Hordes spoils (real names)**: Spoils of **Material** / **Gold** / **Greater Equipment**
(400 Aether, ≥1 Ancestral Legendary + scrolls) / **Bartuc** (666 Aether).
**Obol gambling**: chest/pants/boots 25 · helm/gloves/ring/1H 50 · amulet/2H/bows 100; cap 2,500;
can yield non-mythic uniques; cheapest aspect-fishing = 25-obol slots.
**Native in-game loot filter exists** (Apr 2026): rule conditions include IP range, rarity,
Ancestral, codex-upgrade, GA count, required/optional affixes, specific unique, set bonus.

## 7. The expert "is this an upgrade?" decision tree

1. **Tier gate**: level 70+ in Torment → anything non-Ancestral (<900 IP) is salvage-by-default
   unless the slot itself is still sub-900.
2. **Slot/type wanted by the build?** (weapon-type/handedness gates apply.)
3. **Wanted-affix presence count** vs the target slot — presence at any roll beats absence
   (rolls are fixable: masterwork/temper; missing affixes mostly aren't).
4. **One-wrong-affix rule**: exactly one wrong *non-GA, non-tempered* innate affix = enchant-fixable
   (competes as complete); the wrong affix carrying a GA = warn, the GA dies with the enchant;
   2+ wrong = replace (cube Focused Reroll is the expensive fallback).
5. **GA-on-wanted-affixes count** — each worth 1.5× that affix's max; off-affix GAs ≈ only the
   +1 temper charge.
6. **Roll quality %** within ranges — the tiebreak, not the headline.
7. **Temper potential** — is the missing stat available as a learned temper for that slot? An
   empty temper slot is +1 free affix on any verdict-keep item.
8. **Sunk investment** — equipped item's Quality ranks are non-refundable; candidate must be
   worth re-paying masterwork costs. Warn before salvaging crafted items and before crafting
   tradeable god-rolls.
9. **Invest vs replace**: right base + ≤1 wrong affix + charges/scroll available → INVEST
   (temper → enchant → masterwork → capstone-reroll); else REPLACE (FIND with the activity table).

## 8. Gap analysis — what the app currently gets wrong (audit vs research)

| # | App today | Reality (S13) | Sev |
|---|---|---|---|
| 1 | `InfernalHordesAdvisor` offers six "Season 8" chests (Realm/Vault/Battle/Darkness/Creation/Salvation) | None exist. Real: Material / Gold / Greater Equipment / Bartuc, priced in Aether | **high** |
| 2 | `Activities`: masterwork mats from the Pit ("Obducite/Ingolith/Neathiron per tier") | Pit = glyphs only; Obducite from NMDs/Undercity/Hordes; Ingolith gone | **high** |
| 3 | `Activities`: glyph XP from Pit *or NMDs*; "Rare at 15, Legendary at 46" | Pit-exclusive; thresholds changed (Legendary ~45, cap 50⚠/150 conflict) | **high** |
| 4 | "Tormented Bosses (use boss summoning materials)" | Lair bosses, universal (Greater) Lair Keys spent after the kill | **high** |
| 5 | Masterworking = 12 ranks, crits at 4/8/12 | Quality 0–25, +1%/rank, capstone +50% at 25 (the parsed `x/25 Quality` line **is** this) | **high** |
| 6 | Tempering: finite rolls, brick risk, random affix, 2 slots, no uniques | 1 slot; pick-your-affix; 3+GA charges; Scrolls un-brick; **uniques/mythics temperable** | **high** |
| 7 | No Greater Affix model anywhere (scoring, parser, UI) | GA is the #1 grading currency; 1.5× value; enchant destroys it; +1 temper charge | **high** |
| 8 | Uniques treated as fixed affix sets ("its affix set is fixed by the item") | 3.0 randomized unique secondaries — evaluate rolls like legendaries | **high** |
| 9 | UpgradeScorer ignores IP tier except as sort tiebreak | 850/900 is a hard tier: sub-900 in Torment = replace-by-default; 900 weapons step-change DPS | med |
| 10 | "2+ wrong affixes = unfixable" | Cube Focused Reroll/Add Affix soften this (expensive fallback) | med |
| 11 | No Torment gating on recommendations | T2 manuals, T6 Greater Keys, T8 GA odds, T11 sparks… recommend only what their tier can drop | med |
| 12 | Equal weight per requirement row in completion % | Skill ranks & empty multiplier categories dominate; additive affixes have diminishing returns | med |
| 13 | LootFilter exports D4Companion JSON only | Game has a native loot filter to target | med |
| 14 | No Talisman/charm-set tracking; S8 seal/charm framing | Talisman = 7 sockets of power, seal rarity gates capacity, sets are back | med |
| 15 | Paladin/Warlock "speculative" | Real classes (roster = 8); weapon tables unverified — keep conservative bypass | low |
| 16 | "Codex imprints are free" wording | Unlimited but costs gold+mats; aspect amplification ×2 (2H) / ×1.5 (amulet) unmodeled | low |
| 17 | No Transfiguration awareness | Irreversible last step; parser may meet "unmodifiable" items | low |

**Still correct (validated)**: aspect-can't-imprint-on-uniques (AspectBlocked) · salvage-for-codex
(now with "best roll stored forever" sharpened) · one-enchant fixable credit (at the Occultist) ·
presence-over-rolls upgrade philosophy · temper-vs-enchant station split · the `x/25` Quality
parser format · weapon-type slot gating.

## 9. Prioritized implementation plan

1. **Stop the bleeding (wrong advice)** — rewrite `InfernalHordesAdvisor` (real spoils + Aether
   logic), `Activities` (S13 table in §6), `BuildGuide` temper/masterwork verbs (Quality model,
   un-brick via Scroll, temper-uniques). Drop NMD-glyph and Pit-obducite claims.
2. **Model Greater Affixes end-to-end** — parse the GA marker from TTS tooltips (star/numeral/
   "Greater"), store per-affix `IsGreater`, then: value GA-on-wanted ×1.5 in scoring; block/warn
   enchant-the-GA-line; +1 temper charge each; "Ancestral" no longer implies GA.
3. **Unique re-evaluation** — score owned uniques by their actual rolls vs ranges; remove
   "fixed by the item" copy; add temper-state to unique verdicts.
4. **Tier-aware guidance** — capture/ask the player's Torment tier; gate FIND/GET targets by the
   §2 drop table; emit "push Pit N → unlock Torment M" when gear supports it.
5. **Data-driven season pack** — move boss→unique tables, spoils names, obol prices, glyph
   thresholds, tier gates into a versioned JSON with a season stamp + in-app staleness warning
   (this file's tripwire list as the test). Re-verify at S14 (~June 30).
6. **New systems** — Horadric Cube verbs (ADD-AFFIX/REROLL/RECYCLE-3/TRANSFIGURE-last-with-warning),
   Talisman tracking, native loot-filter export, War Plans meta-recommendation, Tower for GA gaps.
7. **Priority model** — weight gaps per §5 (uniques/aspects > skill ranks > empty multiplier
   categories > capped stats > main stat/life > additive), and make completion % reflect it.

## 10. Open questions to verify in-game (cheap TTS captures)

- Exact composition of the `50 (+30/25) Quality` line under the Quality system.
- Glyph cap 50 vs 150; Legendary upgrade at 45 vs 50/51; radius bump 15 vs 25.
- Capstone reroll cost (Maxroll vs Icy Veins conflict).
- Scroll of Restoration: once per item vs unlimited.
- Legendary native affix count (3 vs 4) — read any live legendary tooltip.
- Whether tempered affixes can roll Greater (sources conflict).
- GA marker phrasing in TTS output (needed for item #2 of the plan).
- Unique secondary-affix enchanting (one replaceable line?) — hover an owned unique at the Occultist.
