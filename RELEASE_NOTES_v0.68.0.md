## D4Scanner v0.68.0

**Imported builds no longer show garbage aspect names.**

Validating a real Barbarian build turned up a junk entry in its aspect list —
"BSK Barbarian 001 2" — which also appeared as an actionable step telling you to
imprint it. It was a legendary aspect whose name never resolved during import, so
the raw internal id leaked through as the display name.

The importer already filters this kind of unresolved id for unique items, but the
check only recognized rarity-keyed ids (Unique/Legendary/…) and wasn't applied to
aspects, so a class-keyed id like this one slipped through. Now:

- the importer filters these out of aspects too, and
- builds you imported *before* this fix are healed automatically on load — the junk
  aspect is dropped from the list and from its slot, no re-import needed.

Real, resolvable aspects are untouched. The example build went from 8 aspects (one
junk) to 7 real ones.

No action needed — just reopen the app.
