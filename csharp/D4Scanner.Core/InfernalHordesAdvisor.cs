namespace D4Scanner.Core;

/// <summary>
/// Recommends the optimal Infernal Hordes reward chest ("offering") based on the current build gaps.
/// Each offering provides different loot types; the advisor picks whichever maximises build progress.
/// Offerings are evaluated in priority order — the first matching heuristic wins.
/// </summary>
public static class InfernalHordesAdvisor
{
    /// <summary>All known Infernal Hordes offering names (canonical Season 8 names).</summary>
    public static class Offerings
    {
        public const string Realm    = "Spoils of the Realm";    // legendary gear → best for missing uniques
        public const string Vault    = "Spoils of the Vault";    // gold + crafting mats (Forgotten Souls, Manuals)
        public const string Battle   = "Spoils of Battle";       // summoning materials / boss keys
        public const string Darkness = "Spoils of Darkness";     // Nightmare Sigils for NMD farming
        public const string Creation = "Spoils of Creation";     // gems and runes for socketing
        public const string Salvation = "Spoils of Salvation";   // consumables (potions); lowest value
    }

    /// <summary>
    /// Recommends the best Infernal Hordes offering for the player's current build state.
    /// </summary>
    public static (string Offering, string Reason) RecommendOffering(DiffReport report, string? targetClass = null)
    {
        var gear    = report.Categories.FirstOrDefault(c => c.Id == "gear");
        var uniques = report.Categories.FirstOrDefault(c => c.Id == "uniques");
        var paragon = report.Categories.FirstOrDefault(c => c.Id == "paragon");

        // 1. Missing uniques → legendary gear chest (HIGH confidence)
        if (UniquesNeedFarming(gear, uniques))
            return (Offerings.Realm,
                "Missing key uniques — legendary drops from Realm chests improve unique drop rates.");

        // 2. Affixes need crafting/enchanting (HIGH confidence)
        var (craftNeeded, craftCount) = CraftingAffixesNeeded(gear);
        if (craftNeeded)
            return (Offerings.Vault,
                $"{craftCount} affix{(craftCount == 1 ? "" : "es")} need enchanting or tempering — Vault chests provide Forgotten Souls and Tempering Manuals.");

        // 3. Sockets need filling with gems/runes (MEDIUM-HIGH confidence)
        if (SocketsNeeded(gear))
            return (Offerings.Creation,
                "Multiple gear slots want gems or runes — Creation chests drop raw gems and runes for crafting at the Jeweler.");

        // 4. Glyphs need leveling (MEDIUM confidence)
        if (GlyphsNeedLeveling(paragon))
            return (Offerings.Darkness,
                "Glyphs need leveling — Darkness chests provide Nightmare Sigils to run Nightmare Dungeons for glyph XP.");

        // 5. Boss summoning needed (MEDIUM confidence, broad class coverage)
        if (BossSummoningNeeded(uniques, targetClass))
            return (Offerings.Battle,
                "Farm Tormented Bosses for uniques — Battle chests provide summoning materials for boss key crafting.");

        // 6. Build mostly complete, roll polishing active (MEDIUM confidence)
        if (gear != null && gear.Under >= 3 && gear.Pct >= 70)
            return (Offerings.Vault,
                "Most gear is present but under-rolled — Vault materials support Masterwork and enchanting for roll improvements.");

        // Fallback: legendary gear is always useful
        return (Offerings.Realm,
            "Legendary gear drops are broadly useful for backup slots and affix rerolling.");
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

    static bool GlyphsNeedLeveling(Category? paragon)
    {
        if (paragon == null) return false;
        var glyphs = paragon.Groups.FirstOrDefault(g =>
            g.Name.Equals("Glyphs", StringComparison.OrdinalIgnoreCase));
        return glyphs?.Items.Any(i => !i.Done) == true;
    }

    static bool BossSummoningNeeded(Category? uniques, string? targetClass)
    {
        if (uniques == null || uniques.Pct >= 80) return false;
        // All classes benefit from boss farming, but especially summoner archetypes
        var cls = (targetClass ?? "").ToLowerInvariant();
        return cls is "necromancer" or "barbarian" or "druid" or "spiritborn";
    }
}
