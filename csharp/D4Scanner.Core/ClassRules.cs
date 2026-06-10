namespace D4Scanner.Core;

/// <summary>
/// Per-class equipment rules, for filtering the shared (cross-character) item pool down to what the
/// active character can actually use. Two signals: the item's voiced class restriction ("Rogue Only"),
/// and the weapon-type tables (each class equips a different weapon set — and a different NUMBER of
/// weapon slots: Barbarian carries 4, Rogue 3, most others 2).
/// Deliberately conservative: an item is only excluded when we KNOW it can't be equipped — unknown
/// item types, unknown classes (new seasonal classes whose tables aren't verified), and armor/jewelry
/// always pass.
/// </summary>
public static class ClassRules
{
    // weapon-type keyword → classes that can equip it (the six verified classes; matched as substrings
    // of the voiced ItemType, longest keyword first so "two-handed sword" wins over "sword" and
    // "crossbow" over "bow"). Two-handed bladed/blunt weapons have their OWN class sets — a Rogue uses
    // one-handed swords but can never lift a two-hander (caught by live verification on real gear).
    static readonly (string kw, string[] classes)[] WeaponUse =
    {
        ("two-handed sword", new[] { "Barbarian", "Necromancer" }),
        ("two-handed mace",  new[] { "Barbarian", "Druid" }),
        ("two-handed axe",   new[] { "Barbarian", "Druid" }),
        ("two-handed scythe", new[] { "Necromancer" }),
        ("crossbow",     new[] { "Rogue" }),
        ("quarterstaff", new[] { "Spiritborn" }),
        ("glaive",       new[] { "Spiritborn" }),
        ("polearm",      new[] { "Barbarian", "Spiritborn" }),
        ("scythe",       new[] { "Necromancer" }),
        ("dagger",       new[] { "Rogue", "Sorcerer", "Necromancer" }),
        ("sword",        new[] { "Barbarian", "Rogue", "Necromancer", "Paladin" }),
        ("axe",          new[] { "Barbarian", "Druid", "Paladin" }),
        ("mace",         new[] { "Barbarian", "Druid", "Paladin" }),
        ("staff",        new[] { "Sorcerer", "Druid" }),
        ("wand",         new[] { "Sorcerer", "Necromancer" }),
        ("bow",          new[] { "Rogue" }),
        ("shield",       new[] { "Necromancer", "Paladin" }),
        ("focus",        new[] { "Sorcerer", "Necromancer" }),
        ("totem",        new[] { "Druid" }),
    };

    // classes whose weapon tables are verified; others (new seasonal classes) bypass the weapon filter
    static readonly HashSet<string> VerifiedClasses = new(StringComparer.OrdinalIgnoreCase)
        { "Barbarian", "Druid", "Necromancer", "Rogue", "Sorcerer", "Spiritborn" };

    /// <summary>How many weapons the class carries at once (Barbarian's arsenal is 4, Rogue 3, others 2).</summary>
    public static int WeaponSlots(string? cls) => (cls ?? "").ToLowerInvariant() switch
    {
        "barbarian" => 4,
        "rogue" => 3,
        _ => 2,
    };

    /// <summary>Can a character of <paramref name="cls"/> equip <paramref name="item"/>? Unknown class or
    /// unknown item type → true (never hide something we can't rule out).</summary>
    public static bool CanEquip(string? cls, Item item)
    {
        if (string.IsNullOrEmpty(cls)) return true;

        // explicit voiced restriction is authoritative
        if (!string.IsNullOrEmpty(item.ClassLock))
            return string.Equals(item.ClassLock, cls, StringComparison.OrdinalIgnoreCase);

        if (!VerifiedClasses.Contains(cls)) return true;   // unverified class table — don't filter

        var hay = ((item.ItemType ?? "") + " " + (item.Slot ?? "")).ToLowerInvariant();
        foreach (var (kw, classes) in WeaponUse)
            if (hay.Contains(kw))
                return classes.Contains(cls, StringComparer.OrdinalIgnoreCase);
        return true;   // armor / jewelry / unrecognized type
    }
}

/// <summary>One row of the hovered-item vs equipped-item comparison: shared affixes get a numeric delta.</summary>
public sealed record CompareRow(string Label, string CandidateText, string EquippedText, double? Delta, bool DeltaIsPercent);

/// <summary>Pairs a candidate item's affixes against the equipped item's for the hover compare panel.
/// Rows are the union of both affix sets — candidate's order first, then equipped-only lines.</summary>
public static class ItemCompare
{
    static string Fmt(Affix a) => a.Value == null ? "—"
        : (a.IsMultiplier ? "x" : "+") + a.Value.Value.ToString("#,0.##", System.Globalization.CultureInfo.InvariantCulture) + (a.IsPercent ? "%" : "");

    public static List<CompareRow> Rows(Item candidate, Item? equipped)
    {
        var rows = new List<CompareRow>();
        var eqPool = equipped?.Affixes ?? new List<Affix>();
        var used = new bool[eqPool.Count];

        foreach (var a in candidate.Affixes)
        {
            Affix? match = null;
            for (int i = 0; i < eqPool.Count; i++)
            {
                if (used[i]) continue;
                if (DiffEngine.PhraseMatch(a.Text, eqPool[i].Text)) { match = eqPool[i]; used[i] = true; break; }
            }
            double? delta = a.Value != null && match?.Value != null ? a.Value - match.Value : null;
            rows.Add(new CompareRow(a.Text, Fmt(a), match != null ? Fmt(match) : "—", delta, a.IsPercent));
        }
        for (int i = 0; i < eqPool.Count; i++)
            if (!used[i])
                rows.Add(new CompareRow(eqPool[i].Text, "—", Fmt(eqPool[i]), null, eqPool[i].IsPercent));
        return rows;
    }
}
