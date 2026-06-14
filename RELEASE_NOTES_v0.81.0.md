## D4Scanner v0.81.0

**Fixed: upgrades are now only suggested for the right weapon slot.**

A melee weapon (sword/dagger) could show up as an "upgrade" badge / Do-Next suggestion
for your **ranged** weapon slot (and vice-versa), because the per-slot upgrade finder
only matched on the generic "weapon" slot — it didn't check the weapon *type*. The
"All Items" list already gated this correctly; the badge and guidance path was missing
the same check.

Now the per-slot upgrade finder applies the same weapon-type gate everywhere: a melee
weapon is never an upgrade for a bow/crossbow slot (or the reverse), and a one-hander is
never suggested for a two-hand slot. Every other slot was already matched correctly.

No action needed.
