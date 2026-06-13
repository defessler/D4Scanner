using System.Text;

namespace D4Scanner.Core;

/// <summary>
/// Exports the target build as a loot filter / shopping list: a human-readable per-slot checklist of wanted
/// affixes, uniques, aspects and sockets, plus a best-effort Diablo4Companion-shaped affix-preset object (their
/// <c>AffixPreset</c> schema: ItemAffixes / ItemAspects / ItemUniques of {Id, Type, IsTempered, …}). The JSON
/// matches the shape; affix Ids are the readable names, which a user may need to reconcile with D4Companion's
/// own affix ids — the markdown is the always-useful artifact.
/// </summary>
public static class LootFilter
{
    static bool SlotEq(string? a, string? b) => DiffEngine.SlotBaseName(a ?? "") == DiffEngine.SlotBaseName(b ?? "");
    static string Cap(string? slot) { var s = DiffEngine.SlotBaseName(slot ?? ""); return s.Length == 0 ? "" : char.ToUpperInvariant(s[0]) + s[1..]; }

    /// <summary>Aspects the build wants that aren't pinned to a specific gear slot — present in
    /// <see cref="TargetBuild.Aspects"/> but on no gear piece's <see cref="TargetGear.Aspect"/>. Both exports
    /// previously derived aspects ONLY from per-gear Aspect, silently dropping these loose aspects (e.g. a
    /// flex/utility aspect not bound to a slot), so the exported filter listed fewer aspects than the build wants.</summary>
    static List<string> LooseAspects(TargetBuild t)
    {
        var bound = new HashSet<string>(
            t.Gear.Where(g => !string.IsNullOrEmpty(g.Aspect)).Select(g => g.Aspect!), StringComparer.OrdinalIgnoreCase);
        return (t.Aspects ?? new()).Where(a => !string.IsNullOrEmpty(a) && !bound.Contains(a)).ToList();
    }

    /// <summary>A readable per-slot loot checklist (markdown).</summary>
    public static string Markdown(TargetBuild t)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {t.Name} — Loot Filter");
        sb.AppendLine($"_Class: {t.Class ?? "?"}{(string.IsNullOrEmpty(t.Profile) ? "" : "  ·  Profile: " + t.Profile)}_");
        sb.AppendLine();

        foreach (var g in t.Gear)
        {
            sb.AppendLine($"## {g.Label ?? g.Slot}");
            var uni = t.Uniques.FirstOrDefault(u => SlotEq(u.Slot, g.Slot));
            if (uni != null) sb.AppendLine($"- **Unique:** {uni.Name}{(uni.Mythic ? " (Mythic)" : "")}");
            if (!string.IsNullOrEmpty(g.Aspect)) sb.AppendLine($"- **Aspect:** {g.Aspect}");
            foreach (var a in g.Affixes)
            {
                string thr = a.Min != null ? $"≥ {a.Min:#,0.##}" : a.MinPercent != null ? $"roll ≥ {a.MinPercent:0}%" : "";
                sb.AppendLine($"- {a.Name}{(thr.Length > 0 ? $"  ({thr})" : "")}{(a.Tempered ? "  _[temper]_" : "")}");
            }
            if (g.Sockets.Count > 0) sb.AppendLine($"- **Sockets:** {string.Join(", ", g.Sockets)}");
            sb.AppendLine();
        }

        var loose = t.Uniques.Where(u => !t.Gear.Any(g => SlotEq(u.Slot, g.Slot))).ToList();
        if (loose.Count > 0)
        {
            sb.AppendLine("## Other Uniques");
            foreach (var u in loose) sb.AppendLine($"- {u.Name}{(u.Mythic ? " (Mythic)" : "")}");
            sb.AppendLine();
        }

        var looseAspects = LooseAspects(t);
        if (looseAspects.Count > 0)
        {
            sb.AppendLine("## Other Aspects");
            foreach (var a in looseAspects) sb.AppendLine($"- {a}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>A Diablo4Companion-shaped affix preset (serialize with System.Text.Json). Color is omitted so
    /// the importer applies its default; Type is the capitalized slot.</summary>
    public static object CompanionPreset(TargetBuild t)
    {
        object Affix(string id, string? slot, bool tempered) =>
            new { Id = id, Type = Cap(slot), IsAnyType = false, IsGreater = false, IsImplicit = false, IsTempered = tempered };

        var affixes = new List<object>();
        foreach (var g in t.Gear)
            foreach (var a in g.Affixes)
                affixes.Add(Affix(a.Name, g.Slot, a.Tempered));

        var aspects = t.Gear.Where(g => !string.IsNullOrEmpty(g.Aspect)).Select(g => Affix(g.Aspect!, g.Slot, false)).ToList();
        aspects.AddRange(LooseAspects(t).Select(a => Affix(a, null, false)));   // slot-less aspects the build wants but doesn't pin to a piece
        var uniques = t.Uniques.Select(u => Affix(u.Name, u.Slot, false)).ToList();

        return new
        {
            Name = t.Name,
            ItemAffixes = affixes,
            ItemAspects = aspects,
            ItemSigils = new List<object>(),
            ItemUniques = uniques,
            ItemRunes = new List<object>(),
            ParagonBoardsList = new List<object>(),
        };
    }
}
