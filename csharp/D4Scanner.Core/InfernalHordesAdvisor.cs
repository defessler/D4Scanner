namespace D4Scanner.Core;

/// <summary>
/// Recommends the best Infernal Hordes end-of-run reward ("spoil") for the player's current build gaps.
/// The live game offers four spoils — Greater Equipment, Materials, Gold, and summoning Bartuc — purchased
/// with the Burning Aether earned during the run; the advisor picks whichever most advances the build.
/// Spoil names and Aether costs come from <see cref="SeasonPack"/>; heuristics are evaluated in priority
/// order, first match wins.
/// </summary>
public static class InfernalHordesAdvisor
{
    /// <summary>
    /// Recommends the best Infernal Hordes spoil for the player's current build state.
    /// </summary>
    public static (string Offering, string Reason) RecommendOffering(DiffReport report, string? targetClass = null)
    {
        var pack = SeasonPack.Current;
        var gear    = report.Categories.FirstOrDefault(c => c.Id == "gear");
        var uniques = report.Categories.FirstOrDefault(c => c.Id == "uniques");

        var gearSpoil = pack.Spoil("gear");        // Spoils of Greater Equipment (≥1 Ancestral Legendary + scrolls)
        var matSpoil  = pack.Spoil("materials");   // Spoils of Materials (Obducite / Forgotten Souls / Gem Fragments)
        var goldSpoil = pack.Spoil("gold");        // Spoils of Gold
        var bartuc    = pack.Spoil("bartuc");      // summon Bartuc (his unique table + Neathiron)

        // 1. Empty slots / many missing uniques → the Ancestral-gear chest fills slots fastest.
        if (UniquesNeedFarming(gear, uniques))
            return (gearSpoil.Name,
                $"Slots still empty — {gearSpoil.Name} ({gearSpoil.Aether} Aether) guarantees an Ancestral Legendary plus Scrolls of Restoration.");

        // 2. Affixes to enchant / temper → crafting materials.
        var (craftNeeded, craftCount) = CraftingAffixesNeeded(gear);
        if (craftNeeded)
            return (matSpoil.Name,
                $"{craftCount} affix{(craftCount == 1 ? "" : "es")} to enchant or temper — {matSpoil.Name} yields Forgotten Souls and Obducite for the work.");

        // 3. Sockets to fill → Gem Fragments from the materials spoil.
        if (SocketsNeeded(gear))
            return (matSpoil.Name,
                $"Multiple slots want gems — {matSpoil.Name} drops the Gem Fragments to craft them at the Jeweler.");

        // 4. Gear in place but under-rolled → push Quality, and chase Neathiron from Bartuc for Capstones.
        if (gear != null && gear.Under >= 3 && gear.Pct >= 70)
            return (bartuc.Name,
                $"Gear's in place but under-rolled — spend {bartuc.Aether} Aether on {bartuc.Name} for Neathiron (rerolls Masterwork Capstones) and his unique table.");

        // Fallback: gold funds enchant and Capstone rerolls and is never a wasted pick.
        return (goldSpoil.Name,
            $"{goldSpoil.Name} funds the gold cost of enchanting and Capstone rerolls — never wasted while you're still crafting.");
    }

    // ---- Heuristic helpers ----

    static bool UniquesNeedFarming(Category? gear, Category? uniques)
    {
        if (gear == null || uniques == null) return false;
        if (gear.Pct >= 60) return false;   // gear mostly complete — other needs take priority
        int missing = uniques.Groups.Sum(g => g.Items.Count(i => !i.Done));
        return missing >= 2;
    }

    static (bool needed, int count) CraftingAffixesNeeded(Category? gear)
    {
        if (gear == null) return (false, 0);
        int missing = gear.Groups.SelectMany(g => g.Items).Count(i => i.Status == "missing");
        int under   = gear.Groups.SelectMany(g => g.Items).Count(i => i.Status == "under" && !i.Tempered);
        int total = missing + under;
        return (total >= 2, total);
    }

    static bool SocketsNeeded(Category? gear)
    {
        if (gear == null) return false;
        return gear.Groups.Count(g => g.WantSockets.Count > 0) >= 2;
    }
}
