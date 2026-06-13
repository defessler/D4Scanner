namespace D4Scanner.Core;

/// <summary>The one-word answer for an owned, non-equipped item: should I equip it, fix it, keep it, or junk it?</summary>
public enum Verdict
{
    Equip,        // a straight upgrade over what's equipped — wear it now
    Fixable,      // one cheap craft (an enchant) away from being an upgrade
    KeepSalvage,  // salvage it: its aspect upgrades your Codex (a permanent, account-wide gain)
    KeepDupe,     // a build-relevant duplicate worth keeping (cube-recycle) or salvaging (Mythic → Spark)
    Stash,        // a tradeable god-roll or an alt's item — keep it unmodified
    Junk,         // nothing the build needs — salvage for materials
}

/// <summary>A verdict plus the human reason and the concrete next action (null when "just salvage").</summary>
public sealed record ItemVerdict(Verdict V, string Reason, string? Action);

/// <summary>Cross-item context a verdict needs beyond the single scored item: the build, the active class,
/// the owned pool (for duplicate counting), and — once known — the player's Torment tier.</summary>
public sealed record VerdictContext(TargetBuild? Target, string? ActiveClass, IReadOnlyList<Item> Owned, int? Torment = null);

/// <summary>
/// Classifies each owned, non-equipped item into a single actionable <see cref="Verdict"/> — the direct answer
/// to "which of my items are an upgrade, worth saving, or junk". Built on the <see cref="UpgradeScorer"/>
/// result (which already knows the equipped piece each item would displace) plus cross-pool context.
/// First matching rung wins. UI-free / headlessly testable.
/// </summary>
public static class Verdicts
{
    public static ItemVerdict For(ScoredItem s, VerdictContext ctx)
    {
        var it = s.Item;
        string slot = s.SlotLabel ?? (it.Slot ?? "slot");

        // 1. EQUIP — beats the equipped piece AS-IS (no enchant needed).
        if (s.RawUpgrade)
        {
            string why = s.EquippedEmpty ? $"fills an empty {slot}"
                : s.AffixDelta > 0 ? $"beats your equipped {slot} by {s.AffixDelta} affix{(s.AffixDelta == 1 ? "" : "es")}"
                : s.GreaterDelta > 0 ? $"matches your equipped {slot} but with {s.GreaterDelta} more Greater Affix{(s.GreaterDelta == 1 ? "" : "es")}"
                : $"beats your equipped {slot}";
            return new ItemVerdict(Verdict.Equip, why, "Equip it");
        }

        // 2. FIXABLE — one enchant tips it past the equipped piece (it only wins via the credit, not as-is).
        if (s.IsUpgrade && !s.RawUpgrade)
            return new ItemVerdict(Verdict.Fixable,
                $"one enchant from beating your equipped {slot}",
                "Enchant 1 affix at the Occultist" + (s.FixDestroysGA ? " — careful, it carries a Greater Affix" : ""));

        // 3. KEEP-SALVAGE — its aspect upgrades the Codex (permanent, account-wide), even if the gear isn't.
        if (s.SalvageAspect != null)
            return new ItemVerdict(Verdict.KeepSalvage,
                $"its aspect upgrades your Codex: {s.SalvageAspect}", "Salvage at the Blacksmith");

        // 4. KEEP-DUPE — duplicate Mythic → Spark; build-relevant duplicate Unique → cube-recycle.
        int copies = ctx.Owned.Count(o => DiffEngine.Normalize(o.Name) == DiffEngine.Normalize(it.Name));
        bool mythic = it.IsMythic || GearList.RarityRank(it.Rarity) == 5;
        if (mythic && copies >= 2)
            return new ItemVerdict(Verdict.KeepDupe, "a duplicate Mythic", "Salvage one for a Resplendent Spark");
        bool buildUnique = it.IsUnique && ctx.Target != null
            && ctx.Target.Uniques.Any(u => DiffEngine.PhraseMatch(u.Name, it.Name));
        if (buildUnique && copies >= 2)
            return new ItemVerdict(Verdict.KeepDupe,
                $"{copies} copies of a build Unique", "Keep — collect 3 to cube-recycle into a fresh roll");

        // 5. STASH — an unmodified tradeable god-roll, or an item locked to another class.
        bool unmodified = (it.TemperUsed ?? 0) == 0 && (it.Quality ?? 0) == 0;
        if (unmodified && s.GreaterCount >= 2)
            return new ItemVerdict(Verdict.Stash,
                $"an unmodified {s.GreaterCount}-GA roll — still tradeable", "Stash it — any craft binds it");
        if (!string.IsNullOrEmpty(it.ClassLock) && ctx.ActiveClass != null
            && !string.Equals(it.ClassLock, ctx.ActiveClass, StringComparison.OrdinalIgnoreCase) && s.GreaterCount >= 1)
            return new ItemVerdict(Verdict.Stash, $"a {it.ClassLock} item", $"Stash for your {it.ClassLock} alt");

        // 6. JUNK — nothing the build needs. In Torment, a sub-900 non-Ancestral item is below the endgame floor.
        bool belowFloor = ctx.Torment.HasValue && !it.IsAncestral && !it.IsMythic && !it.IsUnique && (it.ItemPower ?? 0) is > 0 and < 900;
        string junkReason = belowFloor ? "below the 900 Ancestral floor for Torment"
            : s.SlotTarget > 0 ? $"weaker than your equipped {slot}" : "off-build";
        return new ItemVerdict(Verdict.Junk, junkReason, "Salvage for materials");
    }
}
