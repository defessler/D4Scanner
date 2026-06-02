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

    /// <summary>The full plan, ordered by impact (free equips → acquire → craft → polish).</summary>
    public static List<GuideStep> Steps(DiffReport r)
    {
        var acts = new List<GuideStep>();
        var gear = r.Categories.FirstOrDefault(c => c.Id == "gear");

        if (gear != null)
            for (int gi = 0; gi < gear.Groups.Count; gi++)
            {
                var g = gear.Groups[gi];
                var key = "gear:" + gi;
                foreach (var up in g.UpgradeItems)   // tier 0 — free win: equip a better item you already own
                {
                    var name = up.Split("  (")[0];
                    acts.Add(new GuideStep(0, "EQUIP", $"{g.Name} — {name}", "already in your bags",
                        $"Equip {name} on your {g.Name} — you already own a better fit", key));
                }
                foreach (var i in g.Items)
                {
                    if (i.Status == "missing")       // tier 2 — craft/temper the missing affix
                        acts.Add(new GuideStep(2, i.Tempered ? "TEMPER" : "GET", $"{g.Name} — {i.Label}", i.Tempered ? "at the Blacksmith" : i.Need,
                            i.Tempered ? $"Temper {i.Label} onto your {g.Name}" : $"Get {i.Label} on your {g.Name}" + (i.Need != null ? $" ({i.Need})" : ""), key));
                    else if (i.Status == "under")    // tier 3 — polish an under-rolled affix
                        acts.Add(new GuideStep(3, "IMPROVE", $"{g.Name} — {i.Label}", (i.Val != null ? i.Val + " → " : "") + i.Need,
                            $"Improve {i.Label} on your {g.Name} — at {i.Val} ({i.Need})", key));
                }
            }

        // tier 1 — build-defining: missing uniques, aspects, skills/passives, paragon
        foreach (var (_, i) in CatItems(r, "uniques").Where(x => !x.i.Done))
            acts.Add(new GuideStep(1, "FIND", i.Label, i.Have != null ? "have " + i.Have : null, $"Track down {i.Label}", "cat:uniques"));
        foreach (var (_, i) in CatItems(r, "aspects").Where(x => !x.i.Done))
            acts.Add(new GuideStep(1, "IMPRINT", i.Label, "at the Occultist", $"Imprint the {i.Label}", "cat:aspects"));
        // skills & paragon are vision-gated, one-time menu setup (not farm targets) -> trail the gear plan
        foreach (var (grp, i) in CatItems(r, "skills").Where(x => !x.i.Done))
            acts.Add(new GuideStep(4, "SKILL", i.Label, grp, $"Set up {i.Label} ({grp})", "cat:skills"));
        foreach (var (grp, i) in CatItems(r, "paragon").Where(x => !x.i.Done))
            acts.Add(new GuideStep(4, "PARAGON", i.Label, grp, $"Work your paragon: {i.Label}", "cat:paragon"));

        return acts.OrderBy(a => a.Tier).ToList();
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
        "paragon" => "Paragon", "aspects" => "Aspects", _ => id,
    };

    static IEnumerable<(string grp, ReqItem i)> CatItems(DiffReport r, string id)
    {
        var c = r.Categories.FirstOrDefault(x => x.Id == id);
        if (c == null) yield break;
        foreach (var g in c.Groups)
            foreach (var i in g.Items)
                yield return (g.Name, i);
    }
}
