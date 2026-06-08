using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace D4Scanner.Core;

public enum GearSortMode { Slot, RecentlyAcquired, ItemPower, Name }

/// <summary>
/// Headless helpers for the build-AGNOSTIC "All Items" table: a flat list of every captured item
/// (equipped + inventory) with by-affix / recently-acquired filtering and a stable per-item
/// fingerprint id. No target build is involved — "My Gear" stands on its own (the Maxroll build is
/// the source of truth for the Target only). UI-free so it is headlessly testable.
/// </summary>
public static class GearList
{
    /// <summary>
    /// Stable identity for a specific rolled item: a hash of its name + slot + full affix set
    /// ("name=value", order-independent). Two captures of the SAME physical item produce the same id;
    /// two items that merely share a name but rolled differently get different ids. Used for list
    /// de-duplication and row identity — NOT for icon lookup (icons key on base item type, not the roll).
    /// </summary>
    public static string Fingerprint(Item it)
    {
        var affixes = (it.Affixes ?? new())
            .Select(a => DiffEngine.Normalize(a.Text) + "=" +
                         (a.Value?.ToString("0.###", CultureInfo.InvariantCulture) ?? ""))
            .OrderBy(s => s, StringComparer.Ordinal);
        var canon = DiffEngine.Normalize(it.Name) + "|" + DiffEngine.Normalize(it.Slot) + "|" +
                    string.Join(";", affixes);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canon));
        return Convert.ToHexString(bytes, 0, 6);   // 12 hex chars — ample to avoid collisions
    }

    /// <summary>The best available "when did I see this" tick: the true in-game hover time from the
    /// log's [ISO] prefix when present, else the scan tick. Drives the "recently acquired" sort.
    /// (TTS has no true acquisition time; last-hover is the honest proxy.)</summary>
    public static long AcquiredTicks(Item it) =>
        it.LogTimeUtc?.UtcTicks ?? it.LastScannedTicks;

    /// <summary>Every captured item (equipped + inventory), de-duplicated by <see cref="Fingerprint"/>,
    /// keeping the most-recently-seen instance of each.</summary>
    public static List<Item> Build(LiveBuild live)
    {
        var all = (live.Gear ?? new()).Concat(live.Inventory ?? new());
        return all.GroupBy(Fingerprint)
                  .Select(g => g.OrderByDescending(AcquiredTicks).First())
                  .ToList();
    }

    /// <summary>Distinct affix display names present across the items, for the by-affix filter dropdown.</summary>
    public static List<string> AffixKeys(IEnumerable<Item> items) =>
        items.SelectMany(i => i.Affixes ?? new())
             .Select(a => a.Text?.Trim() ?? "")
             .Where(t => t.Length > 0)
             .Distinct(StringComparer.OrdinalIgnoreCase)
             .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
             .ToList();

    /// <summary>True when the item carries an affix matching <paramref name="affixKey"/> (fuzzy, via PhraseMatch).</summary>
    public static bool HasAffix(Item it, string affixKey) =>
        !string.IsNullOrWhiteSpace(affixKey) &&
        (it.Affixes ?? new()).Any(a => DiffEngine.PhraseMatch(affixKey, a.Text));

    /// <summary>Free-text match over name, item type, slot, runeword, runes, and every affix label (case-insensitive).</summary>
    public static bool MatchesSearch(Item it, string q)
    {
        if (string.IsNullOrWhiteSpace(q)) return true;
        var parts = new[] { it.Name, it.ItemType, it.Slot, it.RunewordName }
            .Concat((it.Affixes ?? new()).Select(a => a.Text))
            .Concat(it.SocketedRunes ?? new())
            .Where(s => !string.IsNullOrEmpty(s));
        return string.Join(" ", parts).Contains(q.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Filter (by affix + free-text) then sort. Pure — the UI owns the widget state.</summary>
    public static List<Item> Apply(IEnumerable<Item> items, string? affixKey, string? search, GearSortMode sort)
    {
        var q = items;
        if (!string.IsNullOrWhiteSpace(affixKey)) q = q.Where(i => HasAffix(i, affixKey!));
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(i => MatchesSearch(i, search!));
        return Sort(q, sort);
    }

    public static List<Item> Sort(IEnumerable<Item> items, GearSortMode sort) => sort switch
    {
        GearSortMode.RecentlyAcquired => items.OrderByDescending(AcquiredTicks).ThenBy(i => i.Name).ToList(),
        GearSortMode.ItemPower        => items.OrderByDescending(i => i.ItemPower ?? 0).ThenBy(i => i.Name).ToList(),
        GearSortMode.Name             => items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList(),
        _                             => items.OrderBy(i => i.Slot ?? "")
                                              .ThenByDescending(i => i.ItemPower ?? 0)
                                              .ThenBy(i => i.Name).ToList(),
    };
}
