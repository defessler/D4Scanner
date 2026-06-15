namespace D4Scanner.Core;

/// <summary>A target affix, tagged core (must-have) vs flexible, and whether the equipped item meets it.</summary>
public sealed record SubAffix(string Name, bool Core, bool Met);

/// <summary>Per-slot flexibility / substitute analysis: which affixes are core vs flexible, the best item the
/// player already owns for the slot, and a budget→endgame ladder (Now / Better / Best).</summary>
public sealed record SlotSub(
    string Slot, string Wanted, List<SubAffix> Affixes,
    string? BestOwned, string? BestRarity, int CoreMet, int CoreTotal, bool BestIsUpgrade,
    List<string> Ladder);

/// <summary>
/// Works out, for each target gear slot, which affixes really matter (core) versus which are flexible, the best
/// stand-in the player already owns, and a Now→Better→Best progression — so a player who lacks the exact items
/// can still see what to equip and aim for. Pure data; reuses <see cref="DiffEngine"/> scoring.
/// </summary>
public static class Substitutes
{
    static string Norm(string s) => DiffEngine.Normalize(s);

    /// <summary>Affix names the build treats as "core": value-gated on any slot, or wanted on 2+ slots.</summary>
    public static HashSet<string> CoreAffixNames(TargetBuild t)
    {
        var counts = new Dictionary<string, int>();
        var core = new HashSet<string>();
        foreach (var g in t.Gear)
            foreach (var a in g.Affixes)
            {
                var n = Norm(a.Name);
                counts[n] = counts.GetValueOrDefault(n) + 1;
                if (a.Min != null || a.MinPercent != null) core.Add(n);
            }
        foreach (var kv in counts) if (kv.Value >= 2) core.Add(kv.Key);
        return core;
    }

    static bool IsCore(TargetAffix a, HashSet<string> core) =>
        a.Min != null || a.MinPercent != null || core.Contains(Norm(a.Name));

    static int CoreMet(TargetGear g, Item item, HashSet<string> core) =>
        g.Affixes.Count(a => IsCore(a, core) && DiffEngine.AffixMet(a, item));

    /// <summary>Best item the player owns (equipped or in bags) for the slot, scored core-affixes-first. Also
    /// returns the chosen item's PRESENCE count — the same metric DiffEngine.UpgradeItems ranks by — so the
    /// upgrade flag can fall back to it when core-affix counts tie (a strictly-better item must still read as one).</summary>
    public static (Item item, int coreMet, int presence)? BestOwned(TargetGear g, LiveBuild live, HashSet<string> core)
    {
        var bs = DiffEngine.SlotBaseName(g.Slot);
        var pool = live.Gear.Concat(live.Inventory).Where(it => DiffEngine.SlotBaseName(it.Slot) == bs).ToList();
        (Item it, int c, int m)? best = null;
        foreach (var it in pool)
        {
            int c = CoreMet(g, it, core), m = DiffEngine.ScoreSlot(g, it);
            if (best == null || c > best.Value.c || (c == best.Value.c && m > best.Value.m)) best = (it, c, m);
        }
        return best == null ? null : (best.Value.it, best.Value.c, DiffEngine.PresenceCount(g, best.Value.it));
    }

    /// <summary>The whole-build substitute plan, one entry per target gear slot.</summary>
    public static List<SlotSub> Plan(TargetBuild t, LiveBuild live)
    {
        var core = CoreAffixNames(t);
        var result = new List<SlotSub>();
        foreach (var g in t.Gear)
        {
            string label = g.Label ?? g.Slot;
            var bs = DiffEngine.SlotBaseName(g.Slot);
            var uni = t.Uniques.FirstOrDefault(u => DiffEngine.SlotBaseName(u.Slot ?? "") == bs);
            string wanted = uni?.Name ?? (!string.IsNullOrEmpty(g.Aspect) ? g.Aspect! : "Any " + label);

            var equipped = live.Gear.Where(it => DiffEngine.SlotBaseName(it.Slot) == bs)
                .OrderByDescending(it => DiffEngine.ScoreSlot(g, it)).FirstOrDefault();

            var affs = g.Affixes
                .Select(a => new SubAffix(a.Name, IsCore(a, core), equipped != null && DiffEngine.AffixMet(a, equipped)))
                .ToList();
            int coreTotal = affs.Count(x => x.Core);
            int eqCoreMet = affs.Count(x => x.Core && x.Met);

            var best = BestOwned(g, live, core);
            // upgrade when it meets more CORE affixes, or — on a core tie (incl. coreTotal==0 / an empty slot) —
            // more present affixes. Presence parity with DiffEngine.UpgradeItems, so the panel can't disagree with
            // the diff's own upgrade list (which a core-count-only test did when counts tied).
            int eqPresence = equipped != null ? DiffEngine.PresenceCount(g, equipped) : 0;
            bool isUpgrade = best != null && !ReferenceEquals(best.Value.item, equipped)
                && (best.Value.coreMet > eqCoreMet
                    || (best.Value.coreMet == eqCoreMet && best.Value.presence > eqPresence));

            // "Better:" copy: aspect-only slots want no affixes; a slot with no CORE affixes still wants its flexible
            // ones — never render the nonsensical "with the 0 core affixes".
            string betterText = affs.Count == 0
                ? "any Rare/Legendary for the slot"
                : $"a Rare/Legendary with the {(coreTotal > 0 ? coreTotal : affs.Count)} {(coreTotal > 0 ? "core" : "wanted")} affix{((coreTotal > 0 ? coreTotal : affs.Count) == 1 ? "" : "es")} — temper one on if it's short";
            betterText += string.IsNullOrEmpty(g.Aspect) ? "" : $", then imprint {g.Aspect}";

            var ladder = new List<string>
            {
                "Now: "    + (best?.item.Name ?? equipped?.Name ?? "anything in the slot — even a placeholder"),
                "Better: " + betterText,
                "Best: "   + (uni != null ? $"{uni.Name} (its secondaries roll randomly — chase a well-rolled copy)" : $"a perfect-roll {label}"),
            };

            result.Add(new SlotSub(label, wanted, affs, best?.item.Name, best?.item.Rarity,
                best?.coreMet ?? eqCoreMet, coreTotal, isUpgrade, ladder));
        }
        return result;
    }
}
