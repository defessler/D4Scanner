namespace D4Scanner.Core;

/// <summary>A recommended in-game activity / crafting action, tailored to what the build still needs.</summary>
public sealed record Activity(string Title, string Detail);

/// <summary>What the guidance engine knows about the player beyond the diff: their Torment tier (for gating
/// drop recommendations) and class. Both optional — guidance degrades gracefully when unknown.</summary>
public sealed record GuideContext(int? Torment = null, string? Class = null);

/// <summary>
/// Turns the remaining gaps in a <see cref="DiffReport"/> into a short, build-specific list of what to go DO in
/// Diablo IV — which activities to run for loot and which crafter to visit — so the guidance answers not just
/// "what's missing" but "how to get it". The season-volatile copy (activity titles/details, farm sources) lives
/// in <see cref="SeasonPack"/> so it tracks the live game without a code change.
/// </summary>
public static class Activities
{
    public static List<Activity> Recommend(DiffReport r, GuideContext? ctx = null)
    {
        var pack = SeasonPack.Current;
        var acts = new List<Activity>();
        void Add(string key) { var a = pack.Activity(key); acts.Add(new Activity(a.Title, a.Detail)); }

        var gear = r.Categories.FirstOrDefault(c => c.Id == "gear");
        bool MissingIn(string id) => r.Categories.FirstOrDefault(c => c.Id == id)?.Groups.Any(g => g.Items.Any(i => !i.Done)) ?? false;

        bool missAffix = gear?.Groups.Any(g => g.Items.Any(i => i.Status == "missing" && !i.Tempered)) ?? false;
        bool needTemper = gear?.Groups.Any(g => g.Items.Any(i => i.Status == "missing" && i.Tempered)) ?? false;
        bool under      = gear?.Groups.Any(g => g.Under > 0) ?? false;
        bool wantSockets = gear?.Groups.Any(g => g.WantSockets.Count > 0) ?? false;
        bool missGlyph = r.Categories.FirstOrDefault(c => c.Id == "paragon")?.Groups
            .Any(g => g.Name == "Glyphs" && g.Items.Any(i => !i.Done)) ?? false;

        if (MissingIn("uniques")) Add("uniques");
        if (MissingIn("aspects")) Add("aspects");
        if (missAffix) Add("affixes");
        if (needTemper) Add("temper");
        if (wantSockets) Add("sockets");

        // Helltide currency is worth a call-out only when gear actively needs crafting work.
        if (under && (missAffix || needTemper || wantSockets)) Add("currency");
        if (under) Add("masterwork");
        // Under-rolled gear that's already present is really a Greater-Affix / higher-roll hunt.
        if (under && !missAffix && !needTemper) Add("greaterAffixes");

        if (missGlyph) Add("glyphs");

        // Torment gating: when we know the player's tier and the build still wants gear, point them at the
        // next tier that unlocks better drops (Greater Lair Keys, sharper Greater-Affix odds, Sparks…).
        if (ctx?.Torment is int t && t < 12 && r.Pct < 100)
        {
            var nextGate = pack.TormentGates.FirstOrDefault(g => g.Tier > t);
            if (nextGate != null)
            {
                var pit = pack.PitForTorment(nextGate.Tier);
                // The "higher tiers also…" nudge only makes sense below the cap; Torment 12 IS the top tier.
                string tail = nextGate.Tier < 12 ? "; higher tiers also raise Greater Affix odds and item rolls." : ".";
                acts.Add(new($"Push to Torment {nextGate.Tier}" + (pit is int p ? $" — clear Pit {p}" : ""),
                    $"You're on Torment {t}. Torment {nextGate.Tier} unlocks {nextGate.Unlocks}{tail}"));
            }
        }

        // When several different activities are recommended, suggest bundling them into a War Plan.
        if (acts.Count >= 3) Add("warPlans");

        // Infernal Hordes offering recommendation — only shown when the build still has gaps.
        if (r.Pct < 100)
        {
            var (offering, offeringReason) = InfernalHordesAdvisor.RecommendOffering(r, r.TargetClass);
            acts.Add(new($"Infernal Hordes — take {offering}", offeringReason));
        }

        return acts;
    }
}
