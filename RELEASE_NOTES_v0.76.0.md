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
