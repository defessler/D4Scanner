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
