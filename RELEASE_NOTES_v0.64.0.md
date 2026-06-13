## D4Scanner v0.64.0

**Parser hardening — the gear parser tolerates malformed numbers uniformly.**

Four numeric fields the parser reads from tooltips — masterwork Quality,
Masterwork rank/max, Temper charges, and Required Level — used a strict integer
parse that would throw on a malformed or oversized digit run and fault the entire
tooltip's parse. Every other number in the parser (Item Power, DPS, socket count,
set counts) already used the lenient, never-throwing parse and degraded gracefully.

This unifies those four fields onto the same lenient parse. On today's exact
screen-reader text nothing changes — ordinary masterwork/temper/level values parse
identically (pinned by new regression tests). But because the screen-reader format
shifts every season, this removes a latent asymmetry: a future format that runs
digits together can no longer crash the parse for those four fields when the rest
of the parser tolerates it.

No action needed — purely internal robustness.
