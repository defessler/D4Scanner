# OCR ↔ TTS fusion — deep research (2026-06-14)

How to use the OCR capture channel (now enabled by default-capable users) to raise capture
**accuracy**, by matching it against the TTS log. Grounded in the current code; flags exactly what
needs real-capture validation before building.

## 1. Where we are

- **TTS** (`LogWatcher`): exact item *text* with a true `[ISO]` hover timestamp (`saapi64.cpp` stamps
  `GetSystemTime`). Knows *what* an item is; weak on *where* it was hovered (worn vs browsed; which
  ring/weapon slot — `SlotPosition` is inferred only from voiced char-panel headers and is often 0).
- **OCR** (`OcrCaptureEngine`): grabs the full D4 window every ~20 s (foreground + changed-frame only),
  runs `Windows.Media.Ocr`, extracts tooltip text blocks, parses items (subordinate to TTS in
  `LiveGearResolver.Merge`). As of **v0.79.0** it also feeds `PanelOracle` the *panel it visually sees*,
  which the TTS classifier uses to rescue worn gear (panel-state fusion).
- **The untapped bit:** `OcrCaptureEngine.cs:74` does `result.Lines.Select(l => l.Text)` — it throws away
  every word's **`BoundingRect`** (screen position). Position is precisely what TTS can't provide.

## 2. The fundamental constraint (verified, June 2026)

D4's character/inventory panel shows equipped gear as **icons**; item **names appear only in the hover
tooltip**. So OCR cannot read your loadout from the static panel — it reads a name only when you hover a
slot, the same hover-dependence as TTS. **OCR's unique contribution is therefore the *position* of the
hovered tooltip / cursor, not a free full-loadout read.**

## 3. What OCR can add at the item level

1. **Spatial slot assignment (the ring fix).** When you hover a ring on the character sheet, the tooltip's
   vertical screen position (via `BoundingRect`) tells upper-slot (Ring 1) from lower-slot (Ring 2). TTS
   supplies the exact ring; OCR supplies the slot. Matched, they end the "rings swap on hover" instability
   (today both rings often have `SlotPosition == 0`, so `LatestPerSlot`'s recency tiebreak flips them on
   each scan). Same idea disambiguates the 3–4 weapon slots.
2. **Cross-validation.** If OCR and TTS independently read the same item near the same time → high
   confidence. Disagreement → trust TTS text (OCR mis-reads stylized fonts — that's why inventory dedup
   already avoids OCR content fingerprints). This lets the app *flag* low-confidence captures instead of
   silently trusting them.
3. **Gap-fill / no-shim fallback.** OCR catches items TTS missed (a hover lost across a poll edge) and is
   the only channel for users who can't install the TTS shim.

## 4. The matching mechanism ("match OCR capture with the TTS log")

Correlate an OCR read with a TTS log entry by **(timestamp, content)**:
- TTS lines carry `[ISO]` times; OCR stamps each scan with `DateTime.UtcNow.Ticks` (same UTC tick scale —
  already relied on by the v0.79 oracle).
- For a hovered item, find the TTS item whose name matches the OCR-read name and whose hover time is within
  a small window of the OCR scan. The TTS item is authoritative for **text**; the OCR read contributes
  **position** (→ `SlotPosition`) and a **confidence vote**.

**The timing problem (the crux):** OCR scans every ~20 s; hovers are sub-second. OCR will often miss the
exact hover frame. Mitigations, in order of value:
- **Adaptive cadence:** scan much faster (≈1–2 s) *while a panel is open* (the oracle already knows when),
  idle at 20 s during combat. The expensive part is the OCR call, not the grab — gate on the frame-hash.
- Lingering helps: players rest on the character sheet for seconds while comparing.
- Accept partial coverage: positioning is a *progressive enhancement* — when OCR has a position for a slot
  it pins it; otherwise the current behaviour stands. Never let a *missing* OCR read worsen TTS (same
  fail-closed/additive principle as the v0.79 oracle).

## 5. The rigorous foundation — a capture diagnostic (recommended first build)

We should not design item-level fusion blind. The cheapest high-value step **is literally "match OCR
capture with the TTS log"** in diagnostic form: when capture-debug is on, each OCR scan saves
- the frame (PNG, throttled),
- the OCR text **with `BoundingRect`s**, and
- the concurrent TTS panel + the last N `[ISO]` log lines.

That gives us, on the user's real resolution/UI-scale:
- measured OCR **accuracy** vs the TTS ground truth (how often names match),
- the **character-panel slot layout** (where each slot's tooltip/icon sits) — the map the positioning
  needs, which we currently lack, and
- the real **timing overlap** between scans and hovers.

This validates every assumption in §3–§4 before we commit to the fusion, and is independently useful for
field-debugging capture issues.

## 6. Recommendation (phased)

- **Phase 0 — Diagnostic + adaptive cadence.** Save paired OCR/TTS captures (debug-gated); scan faster
  while a panel is open. Collect real data; measure accuracy; map the slot layout. *(small, safe, the
  empirical base.)*
- **Phase 1 — Position fusion (the ring fix).** Capture OCR word `BoundingRect`s; map tooltip position →
  slot index; attach it to the time+content-matched TTS item as an authoritative `SlotPosition`. Additive
  and fail-closed: a position only ever *pins* a slot, never removes gear. Fixes ring/weapon swap.
- **Phase 2 — Confidence/cross-validation.** Tag items OCR+TTS agree on as high-confidence; surface
  low-confidence (OCR-only or disagreeing) reads; let OCR fill genuine TTS gaps.

All three extend the existing, reviewed `PanelOracle` fusion rather than re-plumbing the merge, and keep
the invariants that protect the v0.37 vendor-leak fix (additive, fail-closed, replay-deterministic).

## 7. Open questions for real-capture validation (Phase 0 answers these)
- How accurately does `Windows.Media.Ocr` read D4's stylized tooltip font on the user's resolution?
- Are slot tooltip positions stable enough across resolutions/UI-scales to map by ratio, or do they need
  per-setup calibration?
- Does adaptive 1–2 s cadence catch enough hovers to be worth it, at acceptable CPU?
