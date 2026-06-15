namespace D4Scanner.Core;

/// <summary>One prioritized, action-typed step toward the target build. Lower <see cref="Tier"/> = higher
/// impact. <see cref="FocusKey"/> lets a UI jump to the relevant slot ("gear:N") or category ("cat:id").</summary>
public sealed record GuideStep(int Tier, string Verb, string Text, string? Detail, string Headline, string? FocusKey);

/// <summary>
/// Turns a <see cref="DiffReport"/> into an ordered "Do Next" plan spanning the whole build — gear upgrades
/// you already own, missing uniques/aspects/skills/paragon, and affixes to craft or improve. Pure data (no
/// UI), so it's shared by the app and the CLI and is unit-testable.
/// </summary>
public static class BuildGuide
{
    public static string TierLabel(int t) => t switch
    {
        0 => "FREE NOW · equip gear you already own",
        1 => "ACQUIRE · uniques & aspects",
        2 => "CRAFT · add the missing affixes",
        3 => "POLISH · push under-rolled affixes higher",
        _ => "SET UP · skills & paragon",
    };

    /// <summary>The full plan, ordered by impact (free equips → acquire → craft → polish). When
    /// <paramref name="live"/> is supplied, steps note when a wanted aspect or rune already exists on a
    /// different owned piece ("you have it on X").</summary>
    public static List<GuideStep> Steps(DiffReport r, LiveBuild? live = null, int? torment = null)
    {
        var acts = new List<GuideStep>();
        var gear = r.Categories.FirstOrDefault(c => c.Id == "gear");
        var owned = live != null ? (live.Gear ?? new()).Concat(live.Inventory ?? new()).ToList() : new List<Item>();
        static string CleanSock(string s) => s.Replace("Rune: ", "").Replace("Gem: ", "").Trim();

        // Verb-based Torment caveats (GAP 11): when we know the player's tier, a step whose action is gated
        // behind a higher Torment tier gets a one-line note. Per-TARGET tier gating (which boss is which tier)
        // is intentionally out of scope (D8-blocked) — these are generic, gate-derived caveats only.
        string? temperCaveat = null, gaCaveat = null;
        if (torment is int tnow)
        {
            var gates = SeasonPack.Current.TormentGates;
            // (g.Unlocks ?? "") — a corrupt user-override pack with an explicit "unlocks": null deserializes the
            // string to null (System.Text.Json overrides the "" initializer), and a bare .Contains() would NRE
            // out of the guidance render. Null-tolerant, mirroring the null-safe gate handling in Activities.
            var temperGate = gates.FirstOrDefault(g => (g.Unlocks ?? "").Contains("Temper", StringComparison.Ordinal));
            if (temperGate != null && tnow < temperGate.Tier)
                temperCaveat = $"Torment {temperGate.Tier} unlocks {temperGate.Unlocks} — you're on Torment {tnow}; learn the manual first.";
            var gaGate = gates.FirstOrDefault(g => (g.Unlocks ?? "").Contains("Greater Affix", StringComparison.Ordinal));
            if (gaGate != null && tnow < gaGate.Tier)
                gaCaveat = $"Greater-Affix odds jump at Torment {gaGate.Tier} — you're on Torment {tnow}.";
        }
        static string? AppendNote(string? detail, string? note) =>
            note == null ? detail : string.IsNullOrEmpty(detail) ? note : detail + "  ·  " + note;

        if (gear != null)
            for (int gi = 0; gi < gear.Groups.Count; gi++)
            {
                var g = gear.Groups[gi];
                var key = "gear:" + gi;
                // tier 0 — free win: equip a better item you already own. Show ONLY the best owned upgrade
                // per slot (most build-affixes met), not every candidate — a slot with several owned upgrades
                // (e.g. four boots) would otherwise flood the guide. Note how many more are sitting in bags.
                if (g.UpgradeItems.Count > 0)
                {
                    var up = g.UpgradeItems.OrderByDescending(u => u.Met).First();
                    int more = g.UpgradeItems.Count - 1;
                    acts.Add(new GuideStep(0, "EQUIP", $"{g.Name} — {up.Name}",
                        more > 0 ? $"already in your bags (+{more} more owned)" : "already in your bags",
                        $"Equip {up.Name} on your {g.Name} — you already own a better fit", key));
                }
                foreach (var i in g.Items)
                {
                    if (i.Status == "missing")       // tier 2 — craft/temper the missing affix
                        // a "max X" display target is a GOAL, not a requirement — never word it as one
                        acts.Add(new GuideStep(2, i.Tempered ? "TEMPER" : "GET", $"{g.Name} — {i.Label}",
                            i.Tempered ? AppendNote("at the Blacksmith", temperCaveat) : i.NeedIsMax ? $"rolls up to {i.Need?.Replace("max ", "")}" : i.Need,
                            i.Tempered ? $"Temper {i.Label} onto your {g.Name}"
                                       : $"Get {i.Label} on your {g.Name}" + (i.Need != null && !i.NeedIsMax ? $" ({i.Need})" : ""), key));
                    else if (i.Status == "under")    // tier 3 — polish an under-rolled affix
                    {
                        // Tempered affixes that rolled low need a re-temper at the Blacksmith, not an enchant
                        string uVerb = i.Tempered ? "RE-TEMPER" : "IMPROVE";
                        string uStation = i.Tempered ? "at the Blacksmith" : (i.Val != null ? i.Val + " → " : "") + i.Need;
                        acts.Add(new GuideStep(3, uVerb, $"{g.Name} — {i.Label}", i.Tempered ? uStation : AppendNote(uStation, gaCaveat),
                            $"Improve {i.Label} on your {g.Name} — at {i.Val} ({i.Need})", key));
                    }
                }
                // tier 2 — sockets the build wants here that aren't filled; note if a wanted rune sits elsewhere
                if (g.WantSockets.Count > 0 && !g.SocketsDone)
                {
                    var wanted = string.Join(" + ", g.WantSockets.Select(CleanSock));
                    var ownName = g.LiveItems.FirstOrDefault()?.Name;
                    string sDetail = "socket at the Jeweler";
                    foreach (var ws in g.WantSockets)
                    {
                        var code = CleanSock(ws);
                        var holder = owned.FirstOrDefault(o => !string.Equals(o.Name, ownName, StringComparison.OrdinalIgnoreCase)
                            && o.SocketedRunes.Any(rc => string.Equals(rc, code, StringComparison.OrdinalIgnoreCase)));
                        if (holder != null) { sDetail = $"{code} is socketed in your {holder.Name} — move it"; break; }
                    }
                    acts.Add(new GuideStep(2, "SOCKET", $"{g.Name} — {wanted}", sDetail, $"Socket {wanted} in your {g.Name}", key));
                }
            }

        // tier 1 — build-defining: missing uniques, aspects, skills/passives, paragon
        var belial = SeasonPack.Current.BossLadder.Universal.Name;
        string uniqueHint = string.IsNullOrEmpty(belial) ? "its dedicated Lair Boss · Obol gamble"
                                                          : $"its dedicated Lair Boss · {belial} (any table) · Obol gamble";
        foreach (var (_, i) in CatItems(r, "uniques").Where(x => !x.i.Done))
            acts.Add(new GuideStep(1, "FIND", i.Label, i.Have != null ? "have: " + i.Have + " — equip it" : uniqueHint, $"Track down {i.Label}", "cat:uniques"));
        foreach (var (_, i) in CatItems(r, "aspects").Where(x => !x.i.Done))
        {
            // flag the gap, but note when the aspect already sits on a different owned piece (bags / another slot)
            var holder = owned.FirstOrDefault(o => DiffEngine.ItemCarriesAspect(i.Label, o));
            string aDetail = holder != null ? $"you have it on {holder.Name} — salvage & imprint from your Codex" : "at the Occultist";
            acts.Add(new GuideStep(1, "IMPRINT", i.Label, aDetail, $"Imprint the {i.Label}", "cat:aspects"));
        }
        // Note: seals/charms have no target in the build schema yet — when added, route here

        // Deduplicate: if multiple slots require the exact same affix action (e.g. three weapons all
        // need "Damage Over Time Multiplier"), merge them into one step with a combined slot label.
        var deduped = acts
            .GroupBy(a => (a.Tier, a.Verb, AfxLabel(a.Text)))
            .SelectMany(g =>
            {
                if (g.Count() == 1) return g.AsEnumerable();
                var merged = g.First();
                var slots = string.Join(" / ", g.Select(s => s.Text.Split(" — ")[0]).Distinct());
                return new[] { merged with { Text = $"{slots} — {AfxLabel(merged.Text)}" } };
            })
            .OrderBy(a => a.Tier)
            .ToList();
        return deduped;
    }

    /// <summary>Human label for a focus key ("gear:3" → the slot name, "cat:uniques" → "Uniques").</summary>
    public static string FocusLabel(DiffReport r, string key)
    {
        if (key.StartsWith("cat:")) return ShortName(key[4..]);
        if (key.StartsWith("gear:") && int.TryParse(key.AsSpan(5), out var gi))
        {
            var groups = r.Categories.FirstOrDefault(c => c.Id == "gear")?.Groups;
            if (groups != null && gi >= 0 && gi < groups.Count) return groups[gi].Name;
        }
        return key;
    }

    static string ShortName(string id) => id switch
    {
        "gear" => "Gear", "uniques" => "Uniques", "skills" => "Skills",
        "paragon" => "Paragon", "aspects" => "Aspects", "mercenary" => "Mercenary", _ => id,
    };

    /// <summary>Total number of distinct tiers represented in a step plan.</summary>
    public static int TierCount(List<GuideStep> steps) => steps.Select(s => s.Tier).Distinct().Count();

    static string AfxLabel(string stepText) =>
        stepText.Contains(" — ") ? stepText.Substring(stepText.IndexOf(" — ") + 3) : stepText;

    static IEnumerable<(string grp, ReqItem i)> CatItems(DiffReport r, string id)
    {
        var c = r.Categories.FirstOrDefault(x => x.Id == id);
        if (c == null) yield break;
        foreach (var g in c.Groups)
            foreach (var i in g.Items)
                yield return (g.Name, i);
    }
}


