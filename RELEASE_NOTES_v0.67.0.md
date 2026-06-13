## D4Scanner v0.67.0

**Exported loot filters now include every aspect the build wants.**

The loot-filter export (both the markdown checklist and the Diablo4Companion
preset) built its aspect list only from aspects pinned to a specific gear slot.
A build can also list aspects that aren't tied to one slot — a flex/utility
aspect you'll place wherever it fits — and those were silently dropped from the
export, even though the in-app build progress tracks them and tells you to imprint
them.

Both exports now include these slot-less aspects: the markdown gets an "Other
Aspects" section (mirroring "Other Uniques"), and the companion preset lists them
too. Aspects pinned to a slot still appear under that slot and aren't duplicated.

No action needed — just re-export your loot filter to get the complete list.
