## D4Scanner v0.65.0

**Skill rows now show the game's own "rank X/Y" — revealing your +Ranks bonus.**

Equipped-skill rows previously read like "Dance of Knives — 27 pts", which both
mislabeled a skill *rank* as "points" and threw away the "/Y" base-max the game
voices ("RANK 27/15"). That gap — effective rank minus base — is exactly your
+Ranks bonus from gear and paragon, build-relevant information that was hidden.

Skill rows now read "rank 27/15": accurate, and it shows at a glance how much
+Ranks investment a skill carries. (Rows captured by an older version show
"rank 27" until the next time you hover the skill in-game, which refreshes the
base-max.)

Surfaced by validating the diff output against real build + captured-gear data.
No action needed.
