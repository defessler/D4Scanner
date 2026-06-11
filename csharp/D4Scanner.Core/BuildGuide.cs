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
                    {
                        // Tempered affixes that rolled low need a re-temper at the Blacksmith, not an enchant
                        string uVerb = i.Tempered ? "RE-TEMPER" : "IMPROVE";
                        string uStation = i.Tempered ? "at the Blacksmith" : (i.Val != null ? i.Val + " → " : "") + i.Need;
                        acts.Add(new GuideStep(3, uVerb, $"{g.Name} — {i.Label}", uStation,
                            $"Improve {i.Label} on your {g.Name} — at {i.Val} ({i.Need})", key));
                    }
                }
            }

        // tier 1 — build-defining: missing uniques, aspects, skills/passives, paragon
        var belial = SeasonPack.Current.BossLadder.Universal.Name;
        string uniqueHint = string.IsNullOrEmpty(belial) ? "its dedicated Lair Boss · Obol gamble"
                                                          : $"its dedicated Lair Boss · {belial} (any table) · Obol gamble";
        foreach (var (_, i) in CatItems(r, "uniques").Where(x => !x.i.Done))
            acts.Add(new GuideStep(1, "FIND", i.Label, i.Have != null ? "have: " + i.Have + " — equip it" : uniqueHint, $"Track down {i.Label}", "cat:uniques"));
        foreach (var (_, i) in CatItems(r, "aspects").Where(x => !x.i.Done))
            acts.Add(new GuideStep(1, "IMPRINT", i.Label, "at the Occultist", $"Imprint the {i.Label}", "cat:aspects"));
        // Note: seals/charms have no target in the build schema yet — when added, route here
        // skills/paragon/mercenary are intentionally hidden from the UI for now (vision-gated, not yet robust
        // enough for a good user experience). Keep this call-site intact so they can be re-enabled cleanly.
        // AddVisionCategory(r, acts, "skills",    "SKILL",  "Set up",   "skills & passives");
        // AddVisionCategory(r, acts, "paragon",   "PARAGON","Work on",  "paragon boards & glyphs");
        // AddVisionCategory(r, acts, "mercenary", "MERC",   "Hire your","mercenary");

        // Deduplicate: if multiple slots require the exact same affix action (e.g. three weapons all
        // need "Damage Over Time Multiplier"), merge them into one step with a combined slot label.
        var deduped = acts
            .GroupBy(a => (a.Tier, a.Verb, AfxLabel(a.Text)))
            .SelectMany(g =>
            {
                if (g.Count() == 1) return g.AsEnumerable();
                var merged = g.First();
                var slots = string.Join(" / ", g.Select(s => s.Text.Split(" — ")[0]).Distinct());
                return new[] { merged with { Text = $"{slots} — {AfxLabel(merged.Text)}", Detail = merged.Detail } };
            })
            .OrderBy(a => a.Tier)
            .ToList();
        return deduped;
    }

    // Vision-gated category (skills/paragon): if nothing is confirmed yet, we can't know what's actually
    // missing — emit ONE "capture to verify" step instead of flooding DO NEXT with false-missing entries.
    static void AddVisionCategory(DiffReport r, List<GuideStep> acts, string id, string verb, string verbWord, string label)
    {
        var cat = r.Categories.FirstOrDefault(c => c.Id == id);
        if (cat == null) return;
        var missing = CatItems(r, id).Where(x => !x.i.Done).ToList();
        if (missing.Count == 0) return;
        if (cat.Matched == 0)
            acts.Add(new GuideStep(4, "CAPTURE", label, "screenshot to verify",
                $"Capture your {label} with a vision screenshot so the app can track them", "cat:" + id));
        else
            foreach (var (grp, i) in missing)
                acts.Add(new GuideStep(4, verb, i.Label, grp, $"{verbWord} {i.Label} ({grp})", "cat:" + id));
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


