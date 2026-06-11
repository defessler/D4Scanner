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
    public bool IsUpgrade { get; set; }      // beats the equipped piece it would displace
    public string? SlotLabel { get; set; }   // the matched target slot (label/slot)
    /// <summary>The item carries an imprinted aspect the build WANTS — worth salvaging to capture the
    /// aspect into the codex even when its affixes aren't an upgrade. Null when not applicable.</summary>
    public string? SalvageAspect { get; set; }

    /// <summary>Index into target.Gear of the slot this item was compared against — the compatible slot
    /// whose equipped piece it would displace (max margin). Null when no compatible target slot exists.
    /// The UI compare card uses this so the badge and the card describe the SAME equipped item.</summary>
    public int? CompareSlotIndex { get; set; }
    public int EquippedPresent { get; set; }   // present-count of that equipped piece (0 if slot empty)
    public int EquippedEff { get; set; }       // its effective presence (with the one-enchant credit)
    public double EquippedQuality { get; set; } // its avg roll quality (0-100)
    public bool EquippedEmpty { get; set; }    // nothing equipped fills the compared slot

    /// <summary>The item's Greater Affix count (from the temper-charge denominator), capped at the number of
    /// the slot's wanted affixes it actually carries — an estimate of "useful" GAs (we can't always tell per
    /// affix which line is the GA, but a kept item's GAs tend to sit on the stats that matter). Ranks between
    /// affix count and roll quality.</summary>
    public int GreaterOnWanted { get; set; }
    public int EquippedGreaterOnWanted { get; set; }   // same, for the equipped piece it would displace
    /// <summary>Total Greater Affixes on the item (item-level, reliable). The "★N GA" badge.</summary>
    public int GreaterCount { get; set; }
    /// <summary>True when this item is one enchant from full AND carries a Greater Affix — enchanting the wrong
    /// line destroys a GA forever, so surface a caution. Conservative (item-level): we can't always pinpoint
    /// which affix is the GA, so warn whenever any GA is present on a fixable item.</summary>
    public bool FixDestroysGA { get; set; }

    /// <summary>Affix completeness with the enchant credit: one wrong affix is fixable at the Occultist, so a
    /// 3/4 item competes in the 4/4 tier (roll quality then separates them).</summary>
    public int EffectivePresent => SlotPresent + (Fixable ? 1 : 0);

    /// <summary>Signed affix-count delta vs the equipped piece it would displace (enchant credit included).</summary>
    public int AffixDelta => EffectivePresent - EquippedEff;
    /// <summary>Signed useful-Greater-Affix delta vs that piece.</summary>
    public int GreaterDelta => GreaterOnWanted - EquippedGreaterOnWanted;
    /// <summary>Signed roll-quality delta vs that piece — the tiebreak when affix counts are even.</summary>
    public double QualityDelta => SlotQuality - EquippedQuality;
}

/// <summary>
/// Scores owned items against a target build for upgrade-hunting. Ordering, per the user's model:
///   1. Upgrades first — anything beating the equipped piece it would displace sorts above the rest.
///   2. Affix COUNT dominates value: more correct affixes at any roll beats fewer at high rolls —
///      EXCEPT one-affix-short items, which can be enchanted to complete the set and so compete in the
///      complete tier (with roll quality as the separator).
///   3. A unique can never be an upgrade over a non-unique when the build wants an aspect on that slot:
///      aspects can't be imprinted onto uniques.
/// The equipped bar is PER TARGET SLOT, from the SAME assignment the diff view uses (weapon-type
/// gated) — so a sword is never "an upgrade" over your crossbow, and a 3/4 ring IS an upgrade when
/// your worse ring is 1/4 even though your better ring is 4/4. UI-free / headlessly testable.
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

    /// <summary>Score the supplied non-equipped <paramref name="candidates"/> against the build.</summary>
    public static List<ScoredItem> Score(TargetBuild target, LiveBuild live, IEnumerable<Item> candidates, double gate)
    {
        var goal = GoalWeights(target);

        // The bar an upgrade must beat: the equipped item ASSIGNED to each target slot (same weapon-type
        // gated assignment the diff uses, so badge and compare card agree on what "equipped" means).
        // "useful" Greater Affixes: the item's GA count (reliable, from the temper denominator) capped by how
        // many of the slot's wanted affixes it carries — we can't always tell per-affix which line is the GA,
        // but a kept item's GAs tend to land on the stats that matter, so this is a fair, real-data-safe estimate.
        static int UsefulGA(TargetGear g, Item it) => Math.Min(it.GreaterAffixCount ?? 0, DiffEngine.PresenceCount(g, it));

        var assigned = DiffEngine.AssignSlots(target, live);
        var bars = new (int eff, int present, double quality, int gaw, bool nonUnique, bool empty)[target.Gear.Count];
        for (int gi = 0; gi < target.Gear.Count; gi++)
        {
            var g = target.Gear[gi];
            if (assigned.TryGetValue(gi, out var x) && x != null)
            {
                int present = DiffEngine.PresenceCount(g, x);
                bool fix = !x.IsUnique && g.Affixes.Count > 1 && present == g.Affixes.Count - 1;
                bars[gi] = (present + (fix ? 1 : 0), present, DiffEngine.SlotQuality(g, x), UsefulGA(g, x), !x.IsUnique, false);
            }
            else bars[gi] = (0, 0, 0, 0, false, true);
        }

        var result = new List<ScoredItem>();
        foreach (var it in candidates)
        {
            var sb = DiffEngine.SlotBaseName(it.Slot);

            // Evaluate the candidate against every COMPATIBLE same-base target slot, then keep the slot
            // whose equipped piece it would displace with the biggest margin (type-affinity breaks ties).
            int pick = -1; (int margin, int typeMatch, int eff, double q) pickKey = default;
            int pickPresent = 0, pickMet = 0; double pickQ = 0; bool pickFix = false;
            for (int gi = 0; gi < target.Gear.Count; gi++)
            {
                var g = target.Gear[gi];
                if (DiffEngine.SlotBaseName(g.Slot) != sb) continue;
                if (!DiffEngine.WeaponSlotCompatible(g, it)) continue;
                int present = DiffEngine.PresenceCount(g, it);
                bool fix = !it.IsUnique && g.Affixes.Count > 1 && present == g.Affixes.Count - 1;
                int eff = present + (fix ? 1 : 0);
                var key = (margin: eff - bars[gi].eff,
                           typeMatch: DiffEngine.WeaponTypeMatch(g.ItemId, it.ItemType) ? 1 : 0,
                           eff, q: DiffEngine.SlotQuality(g, it));
                if (pick < 0 || key.CompareTo(pickKey) > 0)
                {
                    pick = gi; pickKey = key;
                    pickPresent = present; pickFix = fix; pickQ = key.q;
                    pickMet = DiffEngine.ScoreSlot(g, it, gate);
                }
            }

            TargetGear? bestSlot = pick >= 0 ? target.Gear[pick] : null;
            var bar = pick >= 0 ? bars[pick] : default;
            // a unique can't take an imprinted aspect — if the build wants one on this slot, the unique
            // can never complete it (and enchanting uniques is off the table, so no fixable credit either)
            bool aspectBlocked = it.IsUnique && !string.IsNullOrEmpty(bestSlot?.Aspect);
            if (it.IsUnique) pickFix = false;
            int effective = pickPresent + (pickFix ? 1 : 0);
            int gaCount = it.GreaterAffixCount ?? 0;
            int pickGaw = bestSlot != null ? UsefulGA(bestSlot, it) : 0;
            // completing the slot means enchanting away one affix — if the item carries any Greater Affix, warn
            // (we can't always pinpoint which line is the GA, so caution whenever a GA is present on a fixable item)
            bool fixDestroysGA = pickFix && gaCount > 0;

            // overall-goal contribution: sum the build-wide weight of each affix the item carries (each affix once)
            double goalScore = 0;
            foreach (var a in it.Affixes ?? new())
                foreach (var kv in goal)
                    if (DiffEngine.PhraseMatch(kv.Key, a.Text)) { goalScore += kv.Value; break; }

            // beats the piece it would displace when it's more complete (with the enchant credit), or equally
            // complete but with more REAL presence, or — at a true tie — more useful Greater Affixes
            bool beats = pick >= 0 && (effective > bar.eff
                || (effective == bar.eff && pickPresent > bar.present)
                || (effective == bar.eff && pickPresent == bar.present && pickGaw > bar.gaw));
            bool upgrade = beats && !(aspectBlocked && bar.nonUnique);

            // SALVAGE upgrade: a legendary carrying an imprinted aspect the build wants is worth keeping
            // even when its affixes aren't — salvaging captures the aspect into the codex. (Uniques can't
            // be salvaged for aspects.) Matched via name/imprint/power text — see ItemCarriesAspect.
            string? salvage = null;
            if (!it.IsUnique)
                foreach (var want in target.Gear.Select(g2 => g2.Aspect).Concat(target.Aspects)
                                                .Where(a => !string.IsNullOrEmpty(a)).Distinct())
                    if (DiffEngine.ItemCarriesAspect(want!, it)) { salvage = want; break; }

            result.Add(new ScoredItem
            {
                Item = it, SlotPresent = pickPresent, SlotMet = pickMet,
                SlotTarget = bestSlot?.Affixes.Count ?? 0,
                SlotQuality = pickQ, GoalScore = goalScore, Fixable = pickFix, AspectBlocked = aspectBlocked,
                IsUpgrade = upgrade, SalvageAspect = salvage,
                CompareSlotIndex = pick >= 0 ? pick : null,
                EquippedPresent = bar.present, EquippedEff = bar.eff,
                EquippedQuality = bar.quality, EquippedEmpty = pick >= 0 && bar.empty,
                GreaterOnWanted = pickGaw, EquippedGreaterOnWanted = bar.gaw, GreaterCount = gaCount,
                FixDestroysGA = fixDestroysGA,
                SlotLabel = bestSlot?.Label ?? bestSlot?.Slot,
            });
        }

        return result
            .OrderByDescending(s => s.IsUpgrade)                 // affix upgrades always first
            .ThenByDescending(s => s.SalvageAspect != null)      // then wanted-aspect salvage upgrades
            .ThenByDescending(s => s.EffectivePresent)           // affix count (with the one-enchant credit)
            .ThenByDescending(s => s.SlotMet)                    // rolls already meeting thresholds…
            .ThenByDescending(s => s.GreaterOnWanted)            // …then Greater Affixes on wanted stats (1.5× each)…
            .ThenByDescending(s => s.SlotQuality)                // …then raw roll quality
            .ThenByDescending(s => s.SlotPresent)                // …then real presence over the enchant credit
            .ThenByDescending(s => s.Item.ItemPower ?? 0)        // secondary: item power
            .ThenByDescending(s => GearList.RarityRank(s.Item.Rarity))   // tertiary: rarity
            .ThenBy(s => s.Item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
