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
