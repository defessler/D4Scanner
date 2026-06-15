namespace D4Scanner.Core;

/// <summary>One ordered step toward perfecting an equipped item: a crafting verb, what to do, the rough
/// cost, and any caution. Cost/Warning are null when not applicable.</summary>
public sealed record PathStep(string Verb, string Text, string? Cost, string? Warning);

/// <summary>
/// The "how do I upgrade what I'm wearing" plan for a single equipped slot: the ordered crafting actions that
/// move the item toward the build's target, in the Season-13 canonical order
/// (temper → enchant → masterwork → capstone → socket → imprint). Costs/material sources come from
/// <see cref="SeasonPack"/>. UI-free / headlessly testable. An empty result = the item is already there.
/// </summary>
public static class UpgradePath
{
    static bool IsTwoHand(Item it)
    {
        var t = (it.ItemType ?? "").ToLowerInvariant();
        // All staffs are 2H, INCLUDING the Spiritborn Quarterstaff (matches DiffEngine.IsTwoHandedType) — the
        // old "&& !quarter" exclusion mis-costed its masterwork (omitted the ×2) and dropped the 2H imprint note.
        return t.Contains("two-hand") || t.Contains("two hand") || t.Contains("polearm")
            || t.Contains("staff") || t.Contains("bow");   // bows/crossbows are 2H
    }

    public static List<PathStep> ForSlot(TargetGear g, Item it)
    {
        var pack = SeasonPack.Current;
        var steps = new List<PathStep>();
        var rows = DiffEngine.EvalSlot(g, it, out _);
        bool anc = it.IsAncestral || (it.Rarity ?? "").Contains("ancestral", StringComparison.OrdinalIgnoreCase);
        var sb = DiffEngine.SlotBaseName(it.Slot);

        // 1. TEMPER — a wanted tempered affix the item lacks or rolled under (uniques/mythics temperable now).
        var temperNeed = rows.FirstOrDefault(r => r.Tempered && r.Status != "met");
        if (temperNeed != null)
        {
            // Open question: the Tempers x/y numerator (used vs remaining) is unverified, so show it verbatim
            // rather than computing charges-left, and mention the Scroll of Restoration as the un-brick path.
            string status = it.TemperMax.HasValue ? $"  ·  Tempers {it.TemperUsed}/{it.TemperMax}" : "";
            steps.Add(new PathStep("TEMPER", $"Temper {temperNeed.Label} onto it at the Blacksmith{status}",
                "Legendary Salvage + Forgotten Souls",
                "out of temper rolls? a Scroll of Restoration (Dark Citadel / Infernal Hordes / World Bosses) refills them"));
        }

        // 2. ENCHANT — exactly one non-tempered wanted affix missing ⇒ one enchant from the full set.
        var enchantMissing = rows.Where(r => !r.Tempered && r.Status == "missing").ToList();
        if (enchantMissing.Count == 1)
        {
            bool hasGA = (it.GreaterAffixCount ?? 0) > 0;
            steps.Add(new PathStep("ENCHANT", $"Enchant a wrong affix → {enchantMissing[0].Label} at the Occultist",
                "gold + Veiled Crystals",
                "binds the item; enchanting can't make or keep a Greater Affix"
                    + (hasGA ? " — and this item HAS one, so reroll the right line" : "")));
        }
        else if (enchantMissing.Count >= 2)
        {
            steps.Add(new PathStep("ENCHANT",
                $"{enchantMissing.Count} affixes are off-build — replace the base, or use a Horadric Cube Focused Reroll",
                null, "enchanting only ever fixes one affix per item; a Focused Reroll (Tuning Prism) is the expensive fallback"));
        }

        // 3. MASTERWORK — an Ancestral item below the Quality cap.
        int q = it.Quality ?? 0;
        if (anc && q < pack.Masterwork.QualityCap)
        {
            int ob = pack.ObduciteToCap(q, IsTwoHand(it));
            steps.Add(new PathStep("MASTERWORK", $"Masterwork to Quality {pack.Masterwork.QualityCap}"
                + (q > 0 ? $" (now Q{q})" : ""),
                $"~{ob:#,0} Obducite — Nightmare Dungeons / Kurast Undercity", null));
        }
        // 4. CAPSTONE — at the cap but the +50% landed off your build.
        else if (anc && q >= pack.Masterwork.QualityCap && it.CapstoneAffix != null
                 && !rows.Any(r => r.Status != "missing" && DiffEngine.PhraseMatch(r.Label, it.CapstoneAffix!)))
        {
            steps.Add(new PathStep("MASTERWORK",
                $"Reroll the Quality-{pack.Masterwork.QualityCap} Capstone — its +50% is on {it.CapstoneAffix}, off your build",
                "Neathiron + gold", null));
        }

        // 5. SOCKET — empty sockets, or fewer sockets than the slot can hold.
        int capSockets = pack.SocketsFor(sb);
        if (it.EmptySockets > 0)
            steps.Add(new PathStep("SOCKET", $"Fill {it.EmptySockets} empty socket{(it.EmptySockets == 1 ? "" : "s")} at the Jeweler",
                "a gem or rune each", null));
        else if (capSockets > 0 && (it.SocketCount ?? 0) < capSockets)
            steps.Add(new PathStep("SOCKET", $"Add a socket (this slot holds {capSockets}) at the Jeweler",
                "1 Scattered Prism", null));

        // 6. IMPRINT — the slot wants an aspect the item doesn't carry (Legendary only; uniques can't be imprinted).
        if (!string.IsNullOrEmpty(g.Aspect) && !it.IsUnique && !DiffEngine.ItemCarriesAspect(g.Aspect!, it))
        {
            string amp = sb == "amulet" ? "  (×1.5 power on an amulet)" : IsTwoHand(it) ? "  (×2 power on a 2H weapon)" : "";
            steps.Add(new PathStep("IMPRINT", $"Imprint {g.Aspect} from your Codex at the Occultist{amp}",
                "gold + materials", null));
        }

        return steps;
    }

    /// <summary>One equipped slot's crafting plan: which item is in the slot, and the ordered steps to perfect it.</summary>
    public sealed record SlotPlan(string SlotLabel, string ItemName, List<PathStep> Steps);

    /// <summary>The whole build's "what can I craft right now" overview: every equipped slot that has at least one
    /// available crafting step, in target-slot order. Uses the SAME slot assignment as the diff/upgrade scorer,
    /// so the item shown for each slot is the one the rest of the app treats as equipped there.</summary>
    public static List<SlotPlan> ForBuild(TargetBuild target, LiveBuild live)
    {
        var assigned = DiffEngine.AssignSlots(target, live);
        var plans = new List<SlotPlan>();
        for (int gi = 0; gi < target.Gear.Count; gi++)
        {
            if (!assigned.TryGetValue(gi, out var item) || item == null) continue;
            var steps = ForSlot(target.Gear[gi], item);
            if (steps.Count > 0)
                plans.Add(new SlotPlan(target.Gear[gi].Label ?? target.Gear[gi].Slot ?? "Slot", item.Name ?? "", steps));
        }
        return plans;
    }
}
