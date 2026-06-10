namespace D4Scanner.Core;

/// <summary>
/// Pure (UI-free) live-gear resolution logic, lifted out of the WPF layer so it is headlessly
/// testable. Owns two concerns:
///   • <see cref="Merge"/> — fold a fresh scan batch into the persisted live state, Tts winning
///     over Ocr per slot base-name.
///   • <see cref="BuildShownWeaponNameSet"/> / <see cref="ShouldHideDuplicateWeapon"/> — the
///     paper-doll weapon de-duplication decision (the same unique weapon must not render in two
///     slots), expressed over plain strings so no UI type leaks into Core.
/// </summary>
public static class LiveGearResolver
{
    /// <summary>
    /// Merge a fresh batch of scanned gear into the persisted live state. Tts items win over Ocr
    /// items per slot base-name: if the incoming batch has only Ocr for a slot where the persisted
    /// state already holds a Tts item, the Tts item is kept; otherwise the fresh batch wins for that
    /// slot. Slots absent from the fresh batch are preserved unchanged. (Verbatim from the former
    /// MainWindow.MergeGear — do not "tidy" the comparisons; the casing is already normalized.)
    /// </summary>
    public static List<Item> Merge(List<Item> persisted, List<Item> fresh)
    {
        if (fresh.Count == 0) return persisted;
        var freshBySlot = fresh
            .GroupBy(it => DiffEngine.SlotBaseName(it.Slot ?? ""), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var result = new List<Item>();
        var handledSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in freshBySlot)
        {
            bool hasTtsFresh = kv.Value.Any(it => it.Source == ItemSource.Tts);
            var persistedForSlot = persisted.Where(it => DiffEngine.SlotBaseName(it.Slot ?? "") == kv.Key).ToList();
            bool hasTtsPersisted = persistedForSlot.Any(it => it.Source == ItemSource.Tts);
            if (!hasTtsFresh && hasTtsPersisted)
                result.AddRange(persistedForSlot);  // keep existing Tts, ignore incoming Ocr
            else
                result.AddRange(kv.Value);          // fresh wins (Tts fresh, or no Tts conflict)
            handledSlots.Add(kv.Key);
        }
        result.AddRange(persisted.Where(it => !handledSlots.Contains(DiffEngine.SlotBaseName(it.Slot ?? ""))));
        return result;
    }

    /// <summary>
    /// Merge a fresh INVENTORY batch into the persisted inventory. Each capture channel (TTS / OCR)
    /// publishes only its OWN accumulated list, so a wholesale replacement made items flip in and out
    /// of All Items depending on which channel scanned last. Rules:
    ///   • the fresh batch is its channel's full truth — same-channel persisted items are replaced;
    ///   • the OTHER channel's persisted items survive;
    ///   • on a name+slot collision across channels, Tts wins (same precedence as gear).
    /// An empty fresh batch carries no information and leaves the persisted list untouched.
    /// </summary>
    public static List<Item> MergeInventory(List<Item> persisted, List<Item> fresh)
    {
        if (fresh.Count == 0) return persisted;
        var channel = fresh.Any(i => i.Source == ItemSource.Tts) ? ItemSource.Tts : ItemSource.Ocr;
        static string Key(Item i) => DiffEngine.Normalize(i.Name) + "|" + DiffEngine.SlotBaseName(i.Slot);
        var freshKeys = new HashSet<string>(fresh.Select(Key), StringComparer.Ordinal);

        var result = new List<Item>();
        foreach (var p in persisted.Where(p => p.Source != channel))
            // other-channel survivor — unless a fresh Tts item covers the same item (Tts wins);
            // a persisted Tts item survives even when the fresh Ocr batch re-saw it
            if (p.Source == ItemSource.Tts || !freshKeys.Contains(Key(p)))
                result.Add(p);
        var ttsKept = new HashSet<string>(result.Where(p => p.Source == ItemSource.Tts).Select(Key), StringComparer.Ordinal);
        foreach (var f in fresh)
            if (channel == ItemSource.Tts || !ttsKept.Contains(Key(f)))
                result.Add(f);
        return result;
    }

    /// <summary>
    /// Equipped-gear sanity pass for a character whose class is known. Items that CANNOT be worn are
    /// demoted out of the equipped list (they're stale captures from another character's session, or a
    /// tooltip mis-stamped as worn): class-impossible items (a crossbow on a Barbarian), items with no
    /// slot at all (capture artifacts), and weapons beyond the class's actual carry capacity —
    /// Rogue: 2 melee + 1 ranged; Barbarian: 2 two-handers + 2 one-handers (newest sighting per bucket
    /// wins; an older capture of a swapped-out weapon drops off the doll instead of lingering forever).
    /// Demoted items are returned so the caller can keep them findable as owned inventory.
    /// </summary>
    public static List<Item> SanitizeEquipped(List<Item> gear, string? cls, out List<Item> demoted)
    {
        demoted = new List<Item>();
        var kept = new List<Item>();
        foreach (var it in gear)
        {
            bool slotless = string.IsNullOrWhiteSpace(it.Slot);
            bool wrongClass = cls != null && !ClassRules.CanEquip(cls, it);
            if (slotless || wrongClass) demoted.Add(it); else kept.Add(it);
        }

        // weapon capacity: only for the two classes whose arsenals we've verified on live data
        string? Bucket(Item it)
        {
            if (DiffEngine.SlotBaseName(it.Slot) != "weapon") return null;
            var t = (it.ItemType ?? "").ToLowerInvariant();
            return cls?.ToLowerInvariant() switch
            {
                "rogue"     => t.Contains("bow") ? "ranged" : "melee",
                "barbarian" => t.Contains("two-hand") || t.Contains("two hand") || t.Contains("polearm") ? "twohand" : "onehand",
                _ => null,
            };
        }
        static int Cap(string bucket) => bucket == "ranged" ? 1 : 2;

        foreach (var grp in kept.Where(i => Bucket(i) != null).GroupBy(Bucket!))
        {
            var stale = grp.OrderByDescending(GearList.AcquiredTicks).Skip(Cap(grp.Key!)).ToList();
            foreach (var s in stale) { kept.Remove(s); demoted.Add(s); }
        }
        return kept;
    }

    /// <summary>
    /// Build the canonical set of live weapon names already shown by paper-doll "gear:" slots, using
    /// the de-dup policy: case-insensitive (OrdinalIgnoreCase), null/empty names skipped. Pure data —
    /// the UI extracts the names off its private Section/Group types and passes them in.
    /// </summary>
    public static HashSet<string> BuildShownWeaponNameSet(IEnumerable<string?> shownWeaponNames)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (shownWeaponNames is null) return set;
        foreach (var name in shownWeaponNames)
            if (!string.IsNullOrEmpty(name)) set.Add(name);
        return set;
    }

    /// <summary>
    /// Decide whether a candidate paper-doll weapon section (e.g. a "uni:" unique weapon) should be
    /// hidden because the same live weapon is already shown via a "gear:" slot. True only when
    /// <paramref name="candidateLiveName"/> is non-empty and already present (case-insensitive) in
    /// <paramref name="alreadyShownWeaponNames"/>. A null/empty candidate (or null set) returns false,
    /// so an unidentifiable weapon still renders.
    /// </summary>
    public static bool ShouldHideDuplicateWeapon(IEnumerable<string> alreadyShownWeaponNames, string? candidateLiveName)
    {
        if (string.IsNullOrEmpty(candidateLiveName) || alreadyShownWeaponNames is null) return false;
        // Fast-path only when the set is genuinely case-insensitive; otherwise fall through to the explicit
        // scan so a caller's Ordinal-comparer set can't silently make this case-sensitive (the doc contract).
        if (alreadyShownWeaponNames is HashSet<string> set && Equals(set.Comparer, StringComparer.OrdinalIgnoreCase))
            return set.Contains(candidateLiveName);
        foreach (var n in alreadyShownWeaponNames)
            if (string.Equals(n, candidateLiveName, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
