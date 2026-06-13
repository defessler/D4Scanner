using System.Text.RegularExpressions;

namespace D4Scanner.Core;

/// <summary>
/// The player's captured Talisman pieces (Lord of Hatred): the seal(s), charms, and runes read from the live
/// loadout. Maxroll exports no talisman TARGETS, so this is a pure "what do I own" view — every captured
/// seal/charm/rune item, de-duplicated by name (most-recently-seen wins), grouped by kind and ordered
/// best-first. UI-free / headlessly testable; the App renders it as the paper-doll Talisman card.
/// </summary>
public static class TalismanView
{
    /// <summary>Seals, charms and runes the player has captured (each list deduped + ordered best-first).</summary>
    public sealed record Pieces(List<Item> Seals, List<Item> Charms, List<Item> Runes)
    {
        public int Total => Seals.Count + Charms.Count + Runes.Count;
        public bool Any => Total > 0;
    }

    public static Pieces From(LiveBuild? live)
    {
        var all = (live?.Gear ?? new()).Concat(live?.Inventory ?? new()).ToList();
        List<Item> Pick(string slot) => all
            .Where(i => string.Equals(i.Slot, slot, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(i.Name))
            .GroupBy(i => DiffEngine.Normalize(i.Name), StringComparer.Ordinal)   // collapse re-scans of the same piece
            .Select(g => g.OrderByDescending(GearList.AcquiredTicks).First())
            .OrderByDescending(i => GearList.RarityRank(i.Rarity))                // best-first
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new Pieces(Pick("seal"), Pick("charm"), Pick("rune"));
    }

    static readonly Regex ReCharmSlots = new(@"Unlocks\s+(\d+)\s+Charm\s+Slots", RegexOptions.IgnoreCase);

    /// <summary>A seal voices "Unlocks N Charm Slots" (its capacity — gated by seal rarity); pull N from the
    /// seal's captured power text, or 0 if absent. This is the talisman's charm capacity, build-relevant.</summary>
    public static int CharmSlots(Item seal)
    {
        foreach (var line in seal?.PowerText ?? new())
        {
            var m = ReCharmSlots.Match(line ?? "");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var n)) return n;
        }
        return 0;
    }
}
