namespace D4Scanner.Core;

/// <summary>An owned, NON-equipped item scored against a target build for the "All Items" upgrade list.</summary>
public sealed class ScoredItem
{
    public Item Item { get; init; } = new();
    public int SlotMet { get; set; }         // PRIMARY: target affixes for its own slot the item meets ("perfect set")
    public int SlotTarget { get; set; }      // affixes the perfect set for that slot wants
    public double SlotQuality { get; set; }  // avg roll quality (0-100) of the slot affixes it has — sub-tiebreak
    public double GoalScore { get; set; }    // SECONDARY: contribution toward the overall combined-affix goal
    public bool IsUpgrade { get; set; }      // beats the currently equipped piece in its slot
    public int EquippedMet { get; set; }     // met count of the equipped piece in that slot (0 if empty)
    public string? SlotLabel { get; set; }   // the matched target slot (label/slot)
}

/// <summary>
/// Scores owned items against a target build for upgrade-hunting. Two-tier, per the design:
///   1. PRIMARY — how well the item completes the perfect affix set for its own slot.
///   2. SECONDARY — contribution of its affixes toward the overall combined-affix goal.
/// Sorted best-upgrade-first so the most useful pieces float to the top. UI-free / testable.
/// </summary>
public static class UpgradeScorer
{
    /// <summary>Weight of each wanted affix across the WHOLE build: how many target slots ask for it.
    /// This is the "overall combined goal" an item's affixes are measured against.</summary>
    public static Dictionary<string, int> GoalWeights(TargetBuild t)
    {
        var w = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in t.Gear)
            foreach (var a in g.Affixes)
            {
                var n = (a.Name ?? "").Trim();
                if (n.Length == 0) continue;
                w[n] = w.TryGetValue(n, out var c) ? c + 1 : 1;
            }
        return w;
    }

    /// <summary>Score the supplied non-equipped <paramref name="candidates"/> against the build.
    /// The bar an upgrade must beat is the best equipped piece's met-count for that slot (from <paramref name="live"/>).</summary>
    public static List<ScoredItem> Score(TargetBuild target, LiveBuild live, IEnumerable<Item> candidates, double gate)
    {
        var goal = GoalWeights(target);

        // best equipped met-count per slot base (the bar an upgrade must beat)
        var eqMet = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in target.Gear)
        {
            var sb = DiffEngine.SlotBaseName(g.Slot);
            int best = live.Gear.Where(x => DiffEngine.SlotBaseName(x.Slot) == sb)
                                .Select(x => DiffEngine.ScoreSlot(g, x, gate))
                                .DefaultIfEmpty(0).Max();
            if (best > eqMet.GetValueOrDefault(sb)) eqMet[sb] = best;
        }

        var result = new List<ScoredItem>();
        foreach (var it in candidates)
        {
            var sb = DiffEngine.SlotBaseName(it.Slot);

            // best matching target slot for this item (rings / multi-weapon slots can have several)
            TargetGear? bestSlot = null; int bestMet = -1; double bestQ = 0; int bestTarget = 0;
            foreach (var g in target.Gear.Where(g => DiffEngine.SlotBaseName(g.Slot) == sb))
            {
                int met = DiffEngine.ScoreSlot(g, it, gate);
                double q = DiffEngine.SlotQuality(g, it);
                if (met > bestMet || (met == bestMet && q > bestQ))
                { bestSlot = g; bestMet = met; bestQ = q; bestTarget = g.Affixes.Count; }
            }
            int slotMet = Math.Max(0, bestMet);

            // overall-goal contribution: sum the build-wide weight of each affix the item carries (each affix once)
            double goalScore = 0;
            foreach (var a in it.Affixes ?? new())
                foreach (var kv in goal)
                    if (DiffEngine.PhraseMatch(kv.Key, a.Text)) { goalScore += kv.Value; break; }

            int eq = eqMet.GetValueOrDefault(sb);
            result.Add(new ScoredItem
            {
                Item = it, SlotMet = slotMet, SlotTarget = bestTarget, SlotQuality = bestQ,
                GoalScore = goalScore, EquippedMet = eq,
                IsUpgrade = bestSlot != null && slotMet > eq,
                SlotLabel = bestSlot?.Label ?? bestSlot?.Slot,
            });
        }

        return result
            .OrderByDescending(s => s.SlotMet)         // perfect-set completion dominates
            .ThenByDescending(s => s.GoalScore)        // then overall-goal contribution
            .ThenByDescending(s => s.SlotQuality)      // then roll quality
            .ThenByDescending(s => s.Item.ItemPower ?? 0)
            .ThenBy(s => s.Item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
