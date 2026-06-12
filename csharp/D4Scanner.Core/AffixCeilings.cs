namespace D4Scanner.Core;

/// <summary>
/// Per-affix max-roll ceilings harvested from every owned copy of the affix (best captured range max,
/// else the best value seen). Under the "100% baseline" — max roll is the universal display target —
/// these give a concrete "max X" number even for affixes the evaluated item doesn't carry (the BUILD
/// WANTS rows for a missing affix, the aggregate's perfect-goal column). Shared by AffixAggregate's
/// harvest and the App's row annotation so there is exactly one definition of "the known ceiling".
/// </summary>
public static class AffixCeilings
{
    /// <summary>Best-known ceiling for <paramref name="affixName"/> across <paramref name="owned"/>
    /// (umbrella-aware via <see cref="DiffEngine.AffixSatisfies"/>). 0 = no copy seen anywhere.</summary>
    public static double For(string affixName, IEnumerable<Item> owned)
    {
        double best = 0;
        foreach (var it in owned)
            foreach (var a in it.Affixes ?? new())
                if (DiffEngine.AffixSatisfies(affixName, a))
                    best = Math.Max(best, a.Max ?? a.Value ?? 0);
        return best;
    }

    /// <summary>Ceilings for many affixes at once (case-insensitive by label) — one pass over the pool,
    /// for annotating a whole row set.</summary>
    public static Dictionary<string, double> Harvest(IEnumerable<string> affixNames, IEnumerable<Item> owned)
    {
        var all = owned.SelectMany(it => it.Affixes ?? new()).ToList();
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in affixNames)
        {
            if (name.Length == 0 || result.ContainsKey(name)) continue;
            double best = 0;
            foreach (var a in all)
                if (DiffEngine.AffixSatisfies(name, a)) best = Math.Max(best, a.Max ?? a.Value ?? 0);
            result[name] = best;
        }
        return result;
    }
}
