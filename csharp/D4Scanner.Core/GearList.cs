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
    /// Stable identity for a specific rolled item: a hash of every piece of CONTENT the tooltip voiced —
    /// name, slot, full affix set ("name=value", order-independent), and the non-stateful metadata
    /// (item power, quality/masterwork, tempers, GA count, sockets/runes, aspect, set, dps, level/class
    /// requirement, ancestral, rarity). Two captures of the SAME physical item produce the same id;
    /// items whose tooltips differ in ANY content get different ids — so genuine duplicates of a
    /// same-named item are distinct. Stateful capture context (Equipped/Context/UiPanel/FromCharPanel/
    /// IsComparison/SlotPosition/scan time/Source) is deliberately excluded, as is PowerText (display
    /// prose; the parser already keeps durability/sell-value/menu noise out of it).
    /// NOT for icon lookup (icons key on base item type, not the roll).
    /// </summary>
    public static string Fingerprint(Item it)
    {
        var inv = CultureInfo.InvariantCulture;
        var affixes = (it.Affixes ?? new())
            .Select(a => DiffEngine.Normalize(a.Text) + "=" +
                         (a.Value?.ToString("0.###", inv) ?? ""))
            .OrderBy(s => s, StringComparer.Ordinal);
        var runes = string.Join(",", (it.SocketedRunes ?? new()).OrderBy(r => r, StringComparer.OrdinalIgnoreCase));
        var meta = string.Join("|", new[]
        {
            it.ItemPower?.ToString(inv), it.Quality?.ToString(inv),
            it.MasterworkRank?.ToString(inv), it.MasterworkMax?.ToString(inv),
            it.TemperUsed?.ToString(inv), it.TemperMax?.ToString(inv),
            it.GreaterAffixCount?.ToString(inv),
            it.SocketCount?.ToString(inv), it.EmptySockets.ToString(inv),
            it.RunewordName == null ? null : DiffEngine.Normalize(it.RunewordName),
            it.Aspect == null ? null : DiffEngine.Normalize(it.Aspect),
            it.SetName == null ? null : DiffEngine.Normalize(it.SetName),
            it.Dps?.ToString("0.###", inv), it.RequiresLevel?.ToString(inv), it.ClassLock,
            it.IsAncestral ? "1" : "0",
            it.Rarity == null ? null : DiffEngine.Normalize(it.Rarity),
        }.Select(s => s ?? ""));
        var canon = DiffEngine.Normalize(it.Name) + "|" + DiffEngine.Normalize(it.Slot) + "|" +
                    string.Join(";", affixes) + "|" + meta + "|" + runes;
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
        // Stale-copy guard: the SAME physical item re-scanned after masterworking rolls a different
        // fingerprint, so an OLDER capture of the currently-equipped piece would slip past the fingerprint
        // exclusion and list "an upgrade" that is really the item you're wearing. We only suppress a
        // candidate that is BOTH dominated by an equipped piece AND demonstrably an older masterwork
        // capture of it (a value pushed past its own [min..max] max) — see IsStaleEquippedRescan. A fresh
        // base-roll duplicate you genuinely own (values within range) is NOT hidden, so it shows for compare.
        var equippedList = (current.Gear ?? new());

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
            .Where(o => !IsStaleEquippedRescan(o.Item, equippedList))
            .Where(o => ClassRules.CanEquip(activeClass, o.Item))
            .GroupBy(o => Fingerprint(o.Item))
            .Select(g => g.OrderBy(o => o.Owner == null ? 0 : 1)               // active character's copy wins…
                          .ThenByDescending(o => AcquiredTicks(o.Item)).First()) // …else the freshest sighting
            // Cross-profile stale-copy collapse, content-aware: a same-name+slot entry is dropped ONLY
            // when it is provably a stale lesser capture of another copy — masterwork-inflated (some
            // value past its own captured max) AND STRICTLY dominated by it. Strictness (dominated and
            // not dominating back) makes a symmetric mutual drop impossible, so two genuinely distinct
            // rolls of the same item BOTH show — the v0.37 "collapse to one row" cost is repaid.
            .GroupBy(o => DiffEngine.Normalize(o.Item.Name) + "|" + DiffEngine.SlotBaseName(o.Item.Slot), StringComparer.Ordinal)
            .SelectMany(g =>
            {
                var copies = g.ToList();
                return copies.Where(o => !copies.Any(other =>
                    !ReferenceEquals(other, o) && IsStaleRescanOf(o.Item, other.Item, strict: true)));
            })
            .ToList();
    }

    /// <summary>
    /// True when <paramref name="cand"/> is an OLDER capture of an already-upgraded equipped piece — the
    /// SAME physical item re-scanned at a lower masterwork tier. Gate: it must be masterwork-INFLATED (some
    /// affix value pushed past its own captured [min..max] max) AND value-dominated by an equipped piece of
    /// the same name + slot (every affix matched there with value ≤, and nothing extra). A fresh / base-roll
    /// duplicate (values within range) is never flagged — a genuine spare you own still shows for comparison.
    /// </summary>
    public static bool IsStaleEquippedRescan(Item cand, IReadOnlyList<Item> equipped)
    {
        foreach (var e in equipped)
        {
            if (DiffEngine.Normalize(cand.Name) != DiffEngine.Normalize(e.Name)) continue;
            if (DiffEngine.SlotBaseName(cand.Slot) != DiffEngine.SlotBaseName(e.Slot)) continue;
            // NON-strict deliberately: a partial/OCR re-capture of the worn item can tie the equipped
            // copy value-for-value (mutually dominate) — under strict domination it would resurface as
            // a phantom "candidate" of the item you're wearing, the exact v0.37 false-upgrade bug.
            if (IsStaleRescanOf(cand, e, strict: false)) return true;
        }
        return false;
    }

    /// <summary>True when <paramref name="cand"/> is provably a STALE capture of <paramref name="by"/>
    /// (the same physical item seen at a lower masterwork state): it must be masterwork-INFLATED (some
    /// affix value pushed past its own captured [min..max] max — the evidence it isn't a base-roll spare)
    /// AND value-dominated by <paramref name="by"/>. With <paramref name="strict"/>, domination must be
    /// one-way (dominated and NOT dominating back) so a symmetric pair can never drop each other — use
    /// strict in pool collapses where both sides are candidates; non-strict for the equipped guard.</summary>
    public static bool IsStaleRescanOf(Item cand, Item by, bool strict)
    {
        bool inflated = (cand.Affixes ?? new()).Any(a => a.Value is double v && a.Max is double mx && mx > 0 && v > mx + 0.001);
        if (!inflated) return false;   // base/fresh roll — could be a genuine spare; never hide it
        if (!DominatedBy(cand, by)) return false;
        return !strict || !DominatedBy(by, cand);
    }

    /// <summary>Every affix on <paramref name="cand"/> is present on <paramref name="by"/> with a value no
    /// higher (so <paramref name="cand"/> is the same-or-lesser copy). An affix the equipped piece lacks, or
    /// any value that rolls higher, breaks domination — that's a genuinely different (possibly better) item.</summary>
    static bool DominatedBy(Item cand, Item by)
    {
        var byAffixes = by.Affixes ?? new();
        foreach (var a in cand.Affixes ?? new())
        {
            var key = DiffEngine.Normalize(a.Text);
            var match = byAffixes.FirstOrDefault(b => DiffEngine.Normalize(b.Text) == key);
            if (match == null) return false;                                   // candidate has an affix equipped lacks
            if ((a.Value ?? 0) > (match.Value ?? 0) + 0.001) return false;     // candidate rolls higher somewhere
        }
        return true;
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
        (it.Affixes ?? new()).Any(a => DiffEngine.AffixSatisfies(affixKey, a));

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
