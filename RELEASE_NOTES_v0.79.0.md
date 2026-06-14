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
