namespace D4Scanner.Core;

/// <summary>A recommended in-game activity / crafting action, tailored to what the build still needs.</summary>
public sealed record Activity(string Title, string Detail);

/// <summary>
/// Turns the remaining gaps in a <see cref="DiffReport"/> into a short, build-specific list of what to go DO in
/// Diablo IV — which activities to run for loot and which crafter to visit — so the guidance answers not just
/// "what's missing" but "how to get it". Station knowledge is baked in (Season 8 / Vessel of Hatred).
/// </summary>
public static class Activities
{
    public static List<Activity> Recommend(DiffReport r)
    {
        var acts = new List<Activity>();
        var gear = r.Categories.FirstOrDefault(c => c.Id == "gear");
        bool MissingIn(string id) => r.Categories.FirstOrDefault(c => c.Id == id)?.Groups.Any(g => g.Items.Any(i => !i.Done)) ?? false;

        bool missAffix = gear?.Groups.Any(g => g.Items.Any(i => i.Status == "missing" && !i.Tempered)) ?? false;
        bool needTemper = gear?.Groups.Any(g => g.Items.Any(i => i.Status == "missing" && i.Tempered)) ?? false;
        bool under      = gear?.Groups.Any(g => g.Under > 0) ?? false;
        bool wantSockets = gear?.Groups.Any(g => g.WantSockets.Count > 0) ?? false;
        bool missGlyph = r.Categories.FirstOrDefault(c => c.Id == "paragon")?.Groups
            .Any(g => g.Name == "Glyphs" && g.Items.Any(i => !i.Done)) ?? false;

        if (MissingIn("uniques"))
            acts.Add(new("Hunt your missing uniques",
                "Target-farm the relevant Tormented Bosses (use boss summoning materials), gamble that slot at the " +
                "Purveyor of Curiosities with Obols, or run the Undercity in Nahantu for Unique drops."));

        if (MissingIn("aspects"))
            acts.Add(new("Fill your Codex of Power",
                "Salvage spare Legendaries at the Blacksmith to extract Aspects into your Codex, then Imprint at the Occultist. Codex unlocks are permanent and free to imprint."));

        if (missAffix)
            acts.Add(new("Chase the missing affixes",
                "Enchant a single affix at the Occultist to reroll toward a missing stat, gamble fresh items at the Purveyor, or farm targeted drops in Helltides and Nightmare Dungeons."));

        if (needTemper)
            acts.Add(new("Temper at the Blacksmith",
                "Add the build's manual affixes via Tempering (limited rerolls per item — check the counter before tempering). Stock Tempering Manuals from Whisper caches and Helltides."));

        if (wantSockets)
            acts.Add(new("Socket gems & runes (Jeweler)",
                "Add sockets and craft/upgrade the gems and runes your build wants at the Jeweler; you can unsocket without losing them."));

        // Helltide farming is valuable when gear actively needs crafting work
        if (under && (missAffix || needTemper || wantSockets))
            acts.Add(new("Run Helltides for crafting currency",
                "Helltides drop Forgotten Souls (for enchanting), Sigil Powder, and raw materials. Open Tortured Gifts for targeted gear drops."));
        if (under)
            acts.Add(new("Masterwork to push rolls higher",
                "Masterwork items at the Blacksmith to boost affixes and crit specific ones. Farm Masterwork materials in The Pit (all tiers) — Obducite/Ingolith/Neathiron drop from different Pit tiers."));

        if (missGlyph)
            acts.Add(new("Level your Glyphs",
                "Run The Pit (30+ tiers) or Nightmare Dungeons to earn Glyph XP — leveling Glyphs widens their bonus radius (Rare at 15, Legendary at 46). " +
                "The Pit also drops Horadric Seals (Vessel of Hatred)."));

        return acts;
    }
}








