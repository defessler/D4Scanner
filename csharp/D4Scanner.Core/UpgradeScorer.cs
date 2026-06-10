namespace D4Scanner.Core;

/// <summary>An owned, NON-equipped item scored against a target build for the "All Items" upgrade list.</summary>
public sealed class ScoredItem
{
    public Item Item { get; init; } = new();
    public int SlotPresent { get; set; }     // PRIMARY: target affixes PRESENT at any value
    public int SlotMet { get; set; }         // of those, how many also meet the roll threshold
    public int SlotTarget { get; set; }      // affixes the perfect set for that slot wants
    public double SlotQuality { get; set; }  // avg roll quality (0-100) of the slot affixes it has
    public double GoalScore { get; set; }    // contribution toward the overall combined-affix goal
    public bool Fixable { get; set; }        // exactly ONE affix short of the perfect set — one enchant fixes it
    public bool AspectBlocked { get; set; }  // unique item where the build wants an imprinted aspect (can't imprint a unique)
    public bool IsUpgrade { get; set; }      // beats the currently equipped piece in its slot
    public int EquippedPresent { get; set; } // present-count of the equipped piece in that slot (0 if empty)
    public string? SlotLabel { get; set; }   // the matched target slot (label/slot)
    /// <summary>The item carries an imprinted aspect the build WANTS — worth salvaging to capture the
    /// aspect into the codex even when its affixes aren't an upgrade. Null when not applicable.</summary>
    public string? SalvageAspect { get; set; }

    /// <summary>Affix completeness with the enchant credit: one wrong affix is fixable at the Occultist, so a
    /// 3/4 item competes in the 4/4 tier (roll quality then separates them).</summary>
    public int EffectivePresent => SlotPresent + (Fixable ? 1 : 0);
}

/// <summary>
/// Scores owned items against a target build for upgrade-hunting. Ordering, per the user's model:
///   1. Upgrades first — anything beating the equipped piece sorts above everything that doesn't.
///   2. Affix COUNT dominates value: more correct affixes at any roll beats fewer at high rolls —
///      EXCEPT one-affix-short items, which can be enchanted to complete the set and so compete in the
///      complete tier (with roll quality as the separator).
///   3. A unique can never be an upgrade over a non-unique when the build wants an aspect on that slot:
///      aspects can't be imprinted onto uniques.
/// UI-free / headlessly testable.
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
    /// The bar an upgrade must beat is the equipped piece's effective presence for that slot.</summary>
    public static List<ScoredItem> Score(TargetBuild target, LiveBuild live, IEnumerable<Item> candidates, double gate)
    {
        var goal = GoalWeights(target);

        // per slot base: the equipped piece's best (effective, real) presence + whether a non-unique sits there
        var eqBest = new Dictionary<string, (int eff, int present)>(StringComparer.OrdinalIgnoreCase);
        var eqNonUnique = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in target.Gear)
        {
            var sb = DiffEngine.SlotBaseName(g.Slot);
            foreach (var x in live.Gear.Where(x => DiffEngine.SlotBaseName(x.Slot) == sb))
            {
                int present = DiffEngine.PresenceCount(g, x);
                bool fix = !x.IsUnique && g.Affixes.Count > 1 && present == g.Affixes.Count - 1;
                var pair = (eff: present + (fix ? 1 : 0), present);
                var cur = eqBest.GetValueOrDefault(sb);
                if (pair.eff > cur.eff || (pair.eff == cur.eff && pair.present > cur.present)) eqBest[sb] = pair;
                if (!x.IsUnique) eqNonUnique[sb] = true;
            }
        }

        var result = new List<ScoredItem>();
        foreach (var it in candidates)
        {
            var sb = DiffEngine.SlotBaseName(it.Slot);

            // best matching target slot for this item (rings / multi-weapon slots can have several)
            TargetGear? bestSlot = null; int bestPresent = -1; int bestMet = 0; double bestQ = 0;
            foreach (var g in target.Gear.Where(g => DiffEngine.SlotBaseName(g.Slot) == sb))
            {
                int present = DiffEngine.PresenceCount(g, it);
                double q = DiffEngine.SlotQuality(g, it);
                if (present > bestPresent || (present == bestPresent && q > bestQ))
                { bestSlot = g; bestPresent = present; bestMet = DiffEngine.ScoreSlot(g, it, gate); bestQ = q; }
            }
            int slotPresent = Math.Max(0, bestPresent);
            int slotTarget = bestSlot?.Affixes.Count ?? 0;
            bool fixable = bestSlot != null && slotTarget > 1 && slotPresent == slotTarget - 1;
            // a unique can't take an imprinted aspect — if the build wants one on this slot, the unique
            // can never complete it (and enchanting uniques is off the table, so no fixable credit either)
            bool aspectBlocked = it.IsUnique && !string.IsNullOrEmpty(bestSlot?.Aspect);
            if (it.IsUnique) fixable = false;

            // overall-goal contribution: sum the build-wide weight of each affix the item carries (each affix once)
            double goalScore = 0;
            foreach (var a in it.Affixes ?? new())
                foreach (var kv in goal)
                    if (DiffEngine.PhraseMatch(kv.Key, a.Text)) { goalScore += kv.Value; break; }

            var bar = eqBest.GetValueOrDefault(sb);
            int effective = slotPresent + (fixable ? 1 : 0);
            // beats the equipped piece when it's more complete (with the enchant credit), or equally
            // complete but REALLY complete where the equipped one merely could be after an enchant
            bool beats = effective > bar.eff || (effective == bar.eff && slotPresent > bar.present);
            bool upgrade = bestSlot != null && beats
                        && !(aspectBlocked && eqNonUnique.GetValueOrDefault(sb));

            // SALVAGE upgrade: a legendary carrying an imprinted aspect the build wants is worth keeping
            // even when its affixes aren't — salvaging captures the aspect into the codex. (Uniques can't
            // be salvaged for aspects.)
            string? salvage = null;
            if (!it.IsUnique && !string.IsNullOrEmpty(it.Aspect))
            {
                foreach (var want in target.Gear.Select(g2 => g2.Aspect).Concat(target.Aspects).Where(a => !string.IsNullOrEmpty(a)))
                    if (DiffEngine.PhraseMatch(want!, it.Aspect)) { salvage = want; break; }
            }

            result.Add(new ScoredItem
            {
                Item = it, SlotPresent = slotPresent, SlotMet = bestMet, SlotTarget = slotTarget,
                SlotQuality = bestQ, GoalScore = goalScore, Fixable = fixable, AspectBlocked = aspectBlocked,
                EquippedPresent = bar.present, IsUpgrade = upgrade, SalvageAspect = salvage,
                SlotLabel = bestSlot?.Label ?? bestSlot?.Slot,
            });
        }

        return result
            .OrderByDescending(s => s.IsUpgrade)                 // affix upgrades always first
            .ThenByDescending(s => s.SalvageAspect != null)      // then wanted-aspect salvage upgrades
            .ThenByDescending(s => s.EffectivePresent)           // affix count (with the one-enchant credit)
            .ThenByDescending(s => s.SlotMet)                    // rolls already meeting thresholds…
            .ThenByDescending(s => s.SlotQuality)                // …then raw roll quality
            .ThenByDescending(s => s.SlotPresent)                // …then real presence over the enchant credit
            .ThenByDescending(s => s.Item.ItemPower ?? 0)        // secondary: item power
            .ThenByDescending(s => GearList.RarityRank(s.Item.Rarity))   // tertiary: rarity
            .ThenBy(s => s.Item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
