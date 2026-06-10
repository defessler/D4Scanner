using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace D4Scanner.Core;

public enum GearSortMode { Slot, RecentlyAcquired, ItemPower, Name, Upgrade, Rarity }

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

    /// <summary>An item in the shared pool + which OTHER character is holding it (null = the active one).
    /// <paramref name="OwnerSlug"/> is that character's profile slug, so deletes can reach its profile.</summary>
    public sealed record OwnedItem(Item Item, string? Owner, string? OwnerSlug = null);

    /// <summary>
    /// The cross-character candidate pool for the "All Items" view: gear is shared via the stash, so it
    /// includes everything known from the active character (bags/stash) AND every other saved character
    /// (their bags and even their equipped pieces — they can hand items over), EXCLUDING only what the
    /// active character is wearing right now, and anything the active class can't equip
    /// (<see cref="ClassRules.CanEquip"/>). De-duplicated by <see cref="Fingerprint"/>; the active
    /// character's instance wins so its delete button stays usable.
    /// </summary>
    public static List<OwnedItem> SharedCandidates(LiveBuild current, IEnumerable<CharacterProfile> otherProfiles, string? activeClass)
    {
        var equippedNow = new HashSet<string>((current.Gear ?? new()).Select(Fingerprint), StringComparer.Ordinal);
        // Stale-copy guard: the SAME physical item re-scanned after masterworking / tempering / an enchant
        // rolls a different fingerprint, so an old capture of the currently-equipped piece would slip past
        // the fingerprint exclusion and list "an upgrade" that is really the item you're wearing. Same
        // name + same slot as an equipped piece ⇒ treat as the equipped item. (Cost: a true duplicate of
        // an equipped item is hidden too — better than flagging your own gear as its own upgrade.)
        var equippedNameSlot = new HashSet<string>((current.Gear ?? new())
            .Select(i => DiffEngine.Normalize(i.Name) + "|" + DiffEngine.SlotBaseName(i.Slot)), StringComparer.Ordinal);

        var pool = new List<OwnedItem>();
        foreach (var it in Build(current))
            pool.Add(new OwnedItem(it, null));
        foreach (var p in otherProfiles)
        {
            var label = p.Name + (p.Class != null ? " · " + p.Class : "");
            foreach (var it in (p.Live.Gear ?? new()).Concat(p.Live.Inventory ?? new()))
                pool.Add(new OwnedItem(it, label, p.Slug));
        }

        return pool
            .Where(o => !equippedNow.Contains(Fingerprint(o.Item)))
            .Where(o => !equippedNameSlot.Contains(DiffEngine.Normalize(o.Item.Name) + "|" + DiffEngine.SlotBaseName(o.Item.Slot)))
            .Where(o => ClassRules.CanEquip(activeClass, o.Item))
            .GroupBy(o => Fingerprint(o.Item))
            .Select(g => g.OrderBy(o => o.Owner == null ? 0 : 1)               // active character's copy wins…
                          .ThenByDescending(o => AcquiredTicks(o.Item)).First()) // …else the freshest sighting
            // Cross-profile stale-copy collapse: the SAME physical item re-scanned after masterworking /
            // tempering rolls a different fingerprint, so an old sighting saved on ANOTHER character would
            // list beside the fresh one as a phantom duplicate. Same name + same slot ⇒ one row, the active
            // character's (else freshest) copy. (Cost: two genuinely distinct same-named rolls collapse
            // too — better than phantom duplicates of every reworked item.)
            .GroupBy(o => DiffEngine.Normalize(o.Item.Name) + "|" + DiffEngine.SlotBaseName(o.Item.Slot), StringComparer.Ordinal)
            .Select(g => g.OrderBy(o => o.Owner == null ? 0 : 1)
                          .ThenByDescending(o => AcquiredTicks(o.Item)).First())
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

    /// <summary>Filter (by one-or-more affixes + free-text) then sort. An item must carry EVERY selected
    /// affix (intersection / narrowing). Pure — the UI owns the widget state.</summary>
    public static List<Item> Apply(IEnumerable<Item> items, IReadOnlyCollection<string>? affixKeys, string? search, GearSortMode sort)
    {
        var q = items;
        if (affixKeys is { Count: > 0 })
            q = q.Where(i => affixKeys.All(k => HasAffix(i, k)));
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(i => MatchesSearch(i, search!));
        return Sort(q, sort);
    }

    /// <summary>Rarity ordering for sorting/tiebreaks: Mythic > Unique > Legendary > Rare > Magic > rest.</summary>
    public static int RarityRank(string? rarity)
    {
        var r = (rarity ?? "").ToLowerInvariant();
        return r.Contains("mythic") ? 5 : r.Contains("unique") ? 4 : r.Contains("legendary") ? 3
             : r.Contains("rare") ? 2 : r.Contains("magic") ? 1 : 0;
    }

    public static List<Item> Sort(IEnumerable<Item> items, GearSortMode sort) => sort switch
    {
        GearSortMode.RecentlyAcquired => items.OrderByDescending(AcquiredTicks).ThenBy(i => i.Name).ToList(),
        GearSortMode.ItemPower        => items.OrderByDescending(i => i.ItemPower ?? 0)
                                              .ThenByDescending(i => RarityRank(i.Rarity)).ThenBy(i => i.Name).ToList(),
        GearSortMode.Rarity           => items.OrderByDescending(i => RarityRank(i.Rarity))
                                              .ThenByDescending(i => i.ItemPower ?? 0).ThenBy(i => i.Name).ToList(),
        GearSortMode.Name             => items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList(),
        _                             => items.OrderBy(i => i.Slot ?? "")
                                              .ThenByDescending(i => i.ItemPower ?? 0)
                                              .ThenBy(i => i.Name).ToList(),
    };
}
