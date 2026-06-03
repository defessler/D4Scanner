namespace D4Scanner.Core;

/// <summary>A recommended in-game activity / crafting action, tailored to what the build still needs.</summary>
public sealed record Activity(string Title, string Detail);

/// <summary>
/// Turns the remaining gaps in a <see cref="DiffReport"/> into a short, build-specific list of what to go DO in
/// Diablo IV — which activities to run for loot and which crafter to visit — so the guidance answers not just
/// "what's missing" but "how to get it". Station knowledge is baked in (accurate as of the live game).
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
        bool under = gear?.Groups.Any(g => g.Under > 0) ?? false;
        bool wantSockets = gear?.Groups.Any(g => g.WantSockets.Count > 0) ?? false;
        bool missGlyph = r.Categories.FirstOrDefault(c => c.Id == "paragon")?.Groups
            .Any(g => g.Name == "Glyphs" && g.Items.Any(i => !i.Done)) ?? false;

        if (MissingIn("uniques"))
            acts.Add(new("Hunt your missing uniques",
                "Target-farm the relevant bosses, or gamble that slot at the Purveyor of Curiosities with Obols (earned in Helltides and from Local / Dungeon events)."));
        if (MissingIn("aspects"))
            acts.Add(new("Fill your Codex of Power",
                "Salvage spare Legendaries at the Blacksmith to extract their Aspects into your Codex, then imprint them onto gear at the Occultist."));
        if (missAffix)
            acts.Add(new("Chase the missing affixes",
                "Enchant a single affix at the Occultist to reroll toward a missing stat, gamble fresh items at the Purveyor, or farm targeted drops in Helltides and Nightmare Dungeons."));
        if (needTemper)
            acts.Add(new("Temper at the Blacksmith",
                "Add the build's manual affixes via Tempering (limited rerolls per item). Stock Tempering Manuals from Whisper caches and Helltides."));
        if (wantSockets)
            acts.Add(new("Socket gems & runes (Jeweler)",
                "Add sockets and craft / upgrade the gems and runes your build wants at the Jeweler; you can unsocket without losing them."));
        if (under)
            acts.Add(new("Masterwork to push rolls higher",
                "Masterwork items at the Blacksmith (needs 2 tempered affixes) to boost affixes and crit specific ones. Farm the materials in The Pit and Nightmare Dungeons."));
        if (missGlyph)
            acts.Add(new("Level your Glyphs in The Pit",
                "Run The Pit to earn Glyph XP — leveling Glyphs widens their bonus radius (Rare at 15, Legendary at 46)."));

        return acts;
    }
}
