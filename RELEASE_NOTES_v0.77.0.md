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
