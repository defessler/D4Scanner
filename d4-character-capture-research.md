# Capturing Live Diablo IV Character Data — Research Report

**Scope:** Best ways to capture a *live* D4 character's full build (gear + every affix incl.
tempering/masterworking ranks, legendary aspects, uniques/mythics, full paragon boards + glyphs +
glyph levels, active skills + ranks, key passives) to feed `d4-build-tracker.html`.
**Profile:** PC · maximum automation wanted · low–moderate ToS risk acceptable · **no account-ban risk.**
**As of:** 2026-05-30 (Season 13 "Season of Reckoning," post *Lord of Hatred*).
**Method:** 5-dimension multi-agent web sweep + adversarial verification of every load-bearing claim.

---

## TL;DR

1. **There is no easy button.** No official Diablo IV character API, no official web Armory, no
   build-export/share-code, and **no working third-party scraper** anymore. The data lives
   server-side, so there's no local file to read either.
2. **The only ban-safe live-capture family is screen-based** (screenshots / screen recording →
   OCR or vision model → JSON), analyzed *out of the game process*.
3. **Memory reading and packet capture are out** — both are EULA-prohibited (Blizzard named a
   HUD/overlay tool specifically and warned of permanent suspension; Warden scans process memory).
   They'd capture everything perfectly, which is exactly why they're disqualified.
4. **Split the problem:** the *target* build (what you're aiming at) is a solved, zero-risk import
   from a planner; only the *live* build needs capturing. Your tracker is already built around this split.
5. **Recommended:** screen-recording or screenshot sweep + a frontier (or local) **vision LLM** that
   emits your tracker's JSON schema. It's the most complete *and* the most patch-robust option,
   because it has no hardcoded screen coordinates or templates to break each season.

---

## What does NOT exist (verified, high confidence)

| Hoped-for source | Reality in 2026 |
|---|---|
| Official D4 web Armory (like WoW/D3) | **Does not exist.** `diablo4.blizzard.com` is a marketing site, no character viewer. |
| Battle.net / Blizzard D4 API | **No D4 endpoints, no `d4.profile` OAuth scope.** Only `wow.profile`, `d3.profile`, `sc2.profile`, `openid` exist. No API announced. |
| In-game "Armory" (Season 7, Jan 2025) | Real, but it's a **5-slot loadout save/swap** tool. **No export, no share-code, no JSON, no web/API read-out.** |
| Official build export / share-code | **None.** Unlike PoE, D4 has no copy-build-code feature. |
| `d4armory.io` (the old unofficial scraper) | **DEAD.** Domain 301-redirects to `diablo4.com`; companion fetcher repo archived read-only 2025-09-25 ("no longer active"). Used to read your `account_id` from `FenrisDebug.txt` → character JSON. |
| Local save/log/cache file | **Dead end.** Character data is **server-side**; a local "debug" file at most yields an account ID, not the build. |

> ⚠️ **Caveat on a possible mirror:** one verifier found a `d4armory.fly.dev` mirror that *appeared*
> operational (player lookup + a **packet-sniffing** DPS meter); another verifier got `ECONNREFUSED`.
> Status is **contradictory/unreliable — do not build on it**, and note its DPS-meter component uses
> packet sniffing, which is itself ban-risk (see below).

---

## What is OFF-LIMITS (account-ban risk — do not use)

- **Memory reading / process injection** (TurboHUD/Thud4-style). Would capture the entire build
  instantly and hands-off. **Prohibited:** Blizzard's official notice (PezRadar, 2023-07-26)
  named TurboHUD4 as banned "game-modifying software" with **permanent-suspension** risk, and
  EULA §1.C.vi + §4 (Warden memory monitoring) cover it. **There is no "read-only" carve-out.**
- **Network / packet capture (MITM).** Same EULA §1.C.vi data-mining prohibition; also *infeasible*
  (traffic is TLS-encrypted → needs an active MITM proxy with a custom root cert that trips client
  integrity checks); only a research-grade PoC exists and it breaks every patch.

**Bottom line:** memory = account-ban risk; packet = ban-risk *and* impractical. Excluded per your constraint.

---

## What IS viable: screen-based capture (low ban risk)

Two mechanisms, both never touch game memory:

### A. OCR / vision over your own screenshots or recording (RECOMMENDED engine)
Capture pixels you can already see, parse them **out of process**. D4 has a built-in **Print Screen**
hotkey → `Documents\Diablo IV\Screenshots`, and a **"Advanced Tooltip Information"** (+ "Advanced
Tooltip Compare") setting that surfaces affix value *ranges*, exact skill/passive/paragon-node values,
tempering anvil icons, and masterworking indicators — so the full dataset is *recoverable from images*.

- **Modern vision LLMs >> classic Tesseract** on D4's stylized serif fonts over busy/animated
  backgrounds. Tesseract needs heavy preprocessing or a custom-trained model to be usable.
- **Patch-robust:** a vision model has no hardcoded coordinates/templates, so it survives UI changes
  better than every template-matching tool below.

### B. Accessibility TTS hook (d4lf's trick) — exact text, higher risk
`d4lf` swaps in a fake screen-reader DLL (`saapi64.dll`) that intercepts the **exact tooltip text**
D4's Tolk accessibility engine would speak — more precise than OCR. **But** installing a DLL into the
game directory is the EULA-violating step (ToS: moderate), and the TTS hook is fragile (broke on the
S12 PTR, March 2026). It captures **item tooltips only** — not live paragon/glyphs/skills/passives.

---

## Existing PC tools (reusable tech)

| Tool | Tech | Captures (live) | Imports target build? | Status | Notes |
|---|---|---|---|---|---|
| **d4lf** (`d4lfteam/d4lf`) | Python; Tolk-TTS DLL + OCR | Gear + every affix (incl. temper/masterwork text), aspects, uniques | ✅ Maxroll/Mobalytics/D4Builds (paragon+glyphs+affixes) | Active, v9.2.4 (2026-05-29) | Best gear engine; read-only "Vision Mode" avoids input injection. No live paragon/skill/glyph-level capture. |
| **Diablo4Companion** (`josdemmers`) | C# (MIT); Tesseract + EmguCV | Gear affixes, aspects, runes, sigils | ✅ same planners; **exports JSON** | Active, v5.2.11 (2026-05-30) | Great as the **target-build ingester + JSON model**; multi-res/HDR/8-lang presets. No temper/masterwork/glyph/skill live capture. |
| **d4-item-tooltip-ocr** (`mxtsdev`) | PaddleOCR, custom D4-trained model | Single item tooltip → JSON | — | Low activity | Reusable parser/model; you supply screenshots + loop. |
| AutoHotkey D4 scripts | Input remap | **None** | — | Various | Only useful as a *capture-only* hotkey/macro layer. |
| `awesome-d4` (`cagartner`) | Link index | — | — | Community | Scan each season for new tools. |

**Key gap across all of them:** none reliably auto-captures the **live** paragon boards, **glyph
levels** (only shown on per-glyph hover), **skill ranks**, or **key passives**. You add those via a
vision/OCR pass regardless of which tool you start from.

---

## Ranked recommendations (for PC · max automation · no ban risk)

**1. Screenshot/recording sweep + vision LLM → tracker JSON (out-of-process).** ⭐ Primary.
The only path that captures the *complete* dataset at *zero* ban risk. Most patch-robust. DIY but moderate effort.

**2. Fork d4lf's Tolk-TTS capture (gear/affixes) + add vision passes for paragon/skills.**
More automated & exact for the gear half; ToS moderate (DLL); TTS fragile; still incomplete alone.

**3. Diablo4Companion as the target-build ingester + JSON exporter (+ its gear OCR).**
Use it to *solve the target side* and donate its JSON schema; weaker live engine than a vision LLM.

**4. Pure manual entry.** Zero risk, complete, zero build effort — but the opposite of hands-off.
Keep as the **validation/fallback** for fields the pipeline misreads.

**5. ❌ Memory reading / packet MITM.** Disqualified — account-ban risk. Listed only to rule out.

---

## Recommended end-to-end pipeline (wired to `d4-build-tracker.html`)

**One-time setup**
1. D4 → Settings → Gameplay → enable **Advanced Tooltip Information** *and* **Advanced Tooltip Compare**.
2. Native res (1440p/2160p), **HDR OFF** (HDR PNGs can save washed-out/wrong-tonemapped), borderless
   windowed, large legible UI font. Confirm Print Screen → `Documents\Diablo IV\Screenshots`.
3. In the tracker, define **one JSON schema** used for both target and live (the diff format), e.g.
   per slot `{item, itemPower, affixes:[{name,value,range,isGreater,isTempered,tempRank}], masterworkRank,
   aspect, isUnique/isMythic}`; `paragon:[{board, glyph, glyphLevel, notables}]`;
   `skills:[{name,rank,slotted}]`; `keyPassives:[…]`.

**Target build (hands-off, zero risk)**
4. Take the planner URL/code (Maxroll/D4Builds/Mobalytics). Ingest **once** via Diablo4Companion's
   import → export JSON (or parse the planner code yourself) → map into the tracker as the TARGET.

**Live build (semi-automated, no ban risk) — pick the lower-friction capture:**
5a. **Screen recording (preferred for hands-off):** record ONE short clip (OBS / Windows Game Bar,
    out-of-process) slowly panning over each equipped item's tooltip → stats sheet → skill screen →
    each paragon board → each glyph hover. Extract frames programmatically. *One take, no per-shot clicks.*
5b. **Or screenshot sweep:** hover each equipped item (~12), Print Screen each; + stats sheet + skill
    screen; + each paragon board (up to 5) + each glyph detail (glyph level only shows on hover).
    Optionally bind a **capture-only** key-repeat macro (synthesizes keystrokes only — reads no game
    state/memory) to reduce manual effort. Keep it *passive*; don't auto-navigate the game.

**Analysis (never touches the game)**
6. Feed the ordered images to a **vision LLM** (frontier API, or a **local open-weight model** like
   Qwen2.5-VL/MiniCPM-V to make frequent recaptures free/offline) with a prompt that hard-pins the
   tracker's JSON schema.
7. **Validation pass:** re-prompt to self-check digit fields (item power, affix values, masterwork/temper
   ranks, glyph levels) — vision models can flip 7/1 or misplace decimals on dense tooltips. Flag
   low-confidence fields for a quick manual fix (Rank 4 is your safety net here).
8. Emit live JSON → store **timestamped** (free build history) → run the tracker's diff vs the target.

**Optional upgrade:** fork d4lf's Tolk-TTS capture for exact gear/affix text (read-only Vision Mode),
keep the vision pass for paragon/skills/glyph-levels. Accept ToS: moderate (DLL) + TTS patch fragility.

**Do NOT:** use memory-reading overlays or packet/MITM tools; build on the dead `d4armory.io` or the
unreliable `.fly.dev` mirror; expect any planner or the in-game Armory to auto-import your live character.

---

## Honest caveats & open questions (verify before committing)

These are flagged by the verification + completeness pass — they directly affect how hands-off and how
complete the pipeline really is:

- **"Hands-off" is only partly achievable.** Every no-ban path needs you to surface the in-game panels.
  The screen-recording variant (5a) + a capture-only macro is the closest to hands-off; weigh that vs.
  the one-line "keep capture passive" guidance.
- **Capture cadence & cost are unaddressed.** Decide how often to recapture (per gear swap? per session?).
  A **local** vision model removes per-image API cost for frequent captures. Consider **incremental
  capture** (only re-OCR what changed) and a cheap "did anything change?" gate on the stats sheet.
- **Masterwork/tempering parseability is under-verified.** That advanced tooltips *show* ranges is
  confirmed; that a screenshot unambiguously yields the **masterwork rank number** and **which affixes
  are tempered / which 3 got the masterwork crit** is the hardest part — test it for real.
- **Glyph-level capture assumes per-glyph hover.** Unverified whether S13's paragon UI has a glyph
  *summary* panel listing all levels at once (would replace ~5 hover shots with one).
- **Single full-loadout view?** Unverified whether an inspect/compare view shows multiple item tooltips
  at once, which would cut the ~12 gear screenshots down.
- **Does Tolk TTS also *speak* skill/paragon/glyph tooltips?** If yes, the *entire* dataset could be
  captured as exact text via the TTS hook with no OCR at all — promising, unconfirmed.
- **Vision-LLM accuracy on D4 fonts** is extrapolated from generic OCR benchmarks, not measured on D4's
  parchment-over-animation tooltips. Run a small ground-truth test.
- **Automated *always-on* capture loop** edges toward the EULA "automates the game" gray zone more than
  a manual one-off does — keep analysis out-of-process and capture passive.
- **Multi-character / season resets:** label captures by character; builds churn hard at season start
  (S13 began 2026-04-27).
- **Re-check each season:** if Blizzard ever ships a D4 API/web Armory, it replaces this whole pipeline
  with a zero-risk official path.

---

## Deep-dive: the Text-to-Speech (TTS) approach

**Verdict: use TTS for the GEAR half only.** It yields equipped items + affixes as exact, structured text
(zero OCR error — strictly better than vision there), but it **cannot enumerate paragon boards, glyph
levels, skill ranks, or key passives** (those are cursor-based; only the hovered node is voiced, and
paragon narration is documented-buggy). Telling sign: **d4lf doesn't use TTS for paragon/skills** — it
scrapes planners + draws an overlay. So TTS is a *precision upgrade for gear*, not a full-build engine.

**Coverage (verified):**

| Element | TTS? | Notes |
|---|---|---|
| Item name/type/power/rarity | ✅ | One line each; ALL-CAPS name = start, "Right mouse button" = end |
| Affixes + roll ranges | ✅ | `+1,802 Maximum Life [1,526 - 1,830]`. Needs Advanced Tooltips ON |
| Inherent affixes, aspects, uniques/mythics | ✅ | Parse cleanly |
| Greater-affix flag | ⚠️ | Inferred from missing `[min-max]` range — fragile (d4lf uses icon-OCR instead) |
| Tempering | ⚠️ | Count only (`Tempers: 1/1`); not *which* affixes |
| Masterwork rank | ⚠️ | `Masterwork: n / 12` line IS voiced but d4lf discards it — add your own regex |
| Paragon boards / node allocation | ❌ | Cursor-based; use vision/planner |
| Glyph levels | ❓ | No source confirms levels are voiced — use vision |
| Skill ranks / key passives | ⚠️ | Names voiced on hover; ranks unconfirmed, not enumerable — use vision |

**Mechanism:** D4 → Tolk → `LoadLibrary("saapi64.dll")` → `SA_SayW(const wchar_t*)` per voiced line.
A fake DLL exporting the four SAAPI fns (`SA_SayW`, `SA_BrlShowTextW`, `SA_StopAudio`, `SA_IsRunning` —
the last MUST return true) forwards the text. Minimal fork = replace d4lf's named-pipe write in `SA_SayW`
with a UTF-8 append to a log file, then tail it. Reference: `d4lfteam/d4lf/tts/`, `josdemmers/D4TTS/saapi/`,
`dkager/tolk`. Fake DLL must be Authenticode-signed (d4lf's `install_dll.cmd` self-signs + adds cert to
Trusted Root) or D4 won't load it.

**Lower-risk DLL-free variant:** run real **NVDA + "Speech Logger" add-on** (`opensourcesys/speechLogger`)
and capture NVDA's output to a file instead of forging a DLL. Catch (verified): Tolk only selects NVDA if
it's running *and* `nvdaControllerClient64.dll` is in D4's folder (D4 doesn't ship it) — so you still drop
in **NVAccess's own signed, unmodified** DLL (no forgery, no Trusted-Root cert). Launch NVDA *before* D4
(driver choice is cached). **Test on live S13** — the March 2026 break hit the SAAPI path; NVDA-path status
unverified.

**Risk:** low ban risk (no memory/injection; passive IPC; no documented bans for d4lf/TTS filters in years
of use). Accessibility settings themselves = officially supported, zero risk. **Real cost = maintenance
treadmill:** TTS breaks each season via (1) the DLL hook (S12 PTR) and (2) utterance text-format changes
(d4lf shipped ~5 format fixes in the first 2 weeks of S13). Vision tolerates wording changes; only breaks on
real UI-layout changes.

**Recommendation:** two-channel hybrid — **TTS for gear, vision-LLM for paragon/glyphs/skills/passives** —
merged into one normalized build JSON with per-field source/confidence tags. Or go **vision-only** to avoid
the seasonal TTS treadmill (captures everything, smallest footprint, more affix-parsing care needed).

---

## Key sources
- Blizzard EULA (§1.C.vi data-mining; §4 memory monitoring): https://www.blizzard.com/en-us/legal/fba4d00f-c7e4-4883-b8b9-1b4500a402ea/blizzard-end-user-license-agreement
- Blizzard notice naming TurboHUD4 (2023-07-26): https://us.forums.blizzard.com/en/d4/t/a-notice-regarding-unauthorized-game-modifying-software-in-diablo-iv/102121
- "No D4 API" thread (active to Nov 2025): https://us.forums.blizzard.com/en/blizzard/t/diablo-4-api-d4armory/45191
- Battle.net OAuth scopes: https://community.developer.battle.net/documentation/guides/using-oauth
- d4armory fetcher (archived 2025-09-25): https://github.com/ryancollingwood/diablo_4_armory_fetcher
- d4lf: https://github.com/d4lfteam/d4lf · Diablo4Companion: https://github.com/josdemmers/Diablo4Companion
- d4-item-tooltip-ocr: https://github.com/mxtsdev/d4-item-tooltip-ocr · awesome-d4: https://github.com/cagartner/awesome-d4
- In-game Armory: https://www.icy-veins.com/d4/guides/armory-guide/ · https://www.wowhead.com/diablo-4/guide/systems/armory
- Advanced tooltips: https://www.thegamer.com/diablo-4-how-advanced-tooltips-work/ · Screenshots: https://game8.co/games/Diablo-4/archives/415772
- Char data is server-side: https://us.forums.blizzard.com/en/d4/t/where-are-saved-game-files-located/46682

*Generated by a verified multi-agent research workflow on 2026-05-30.*
