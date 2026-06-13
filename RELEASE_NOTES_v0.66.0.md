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
