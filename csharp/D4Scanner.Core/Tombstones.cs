using System.Text.Json;

namespace D4Scanner.Core;

/// <summary>
/// Account-wide record of items the player removed from the "All Items" list that the app can't otherwise
/// know were salvaged / traded / dropped. Without it, a per-item delete RESURRECTS on the next log poll
/// (LogWatcher re-emits its whole accumulated inventory and the merge treats that as the channel's truth).
///
/// Keyed by NAME + base-slot (not fingerprint): fingerprints rot when an item is masterworked/tempered or
/// re-parsed slightly differently by OCR, and the codebase already uses name|slot as physical identity.
/// Each tombstone stores the delete time; an item is hidden only while its newest sighting is no NEWER than
/// the tombstone — so re-hovering or re-equipping an item the player demonstrably still owns auto-resurrects
/// it (and drops the tombstone). Account-wide (one store, not per-profile) because the stash is shared, so a
/// deleted item shouldn't reappear from another character's older sighting.
///
/// Edge case (documented, accepted): un-prefixed log lines (older shim) get a "now" scan tick on replay, so
/// after an app restart a replayed sighting can look newer than the tombstone and resurrect the item. Current
/// shims stamp an [ISO] time, and log replay is bounded by the session marker, so this is rare.
/// </summary>
public sealed class TombstoneStore
{
    const int Cap = 500;
    readonly string _path;
    Dictionary<string, long> _stones;   // name|slot -> delete UTC ticks
    bool _dirty;

    public TombstoneStore(string path)
    {
        _path = path;
        _stones = Load(path);
    }

    static Dictionary<string, long> Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(path))
                       ?? new Dictionary<string, long>(StringComparer.Ordinal);
        }
        catch { }
        return new Dictionary<string, long>(StringComparer.Ordinal);
    }

    public int Count => _stones.Count;

    /// <summary>Physical identity used for tombstoning — matches the merge/dedup key elsewhere.</summary>
    public static string KeyFor(Item it) => DiffEngine.Normalize(it.Name) + "|" + DiffEngine.SlotBaseName(it.Slot);

    /// <summary>Tombstone an item. The tombstone time is at least one tick past the item's own sighting, so a
    /// stale re-emission of the SAME sighting stays hidden; a genuinely newer sighting later un-hides it.</summary>
    public void Add(Item it, long? nowUtcTicks = null)
    {
        long now = nowUtcTicks ?? DateTime.UtcNow.Ticks;
        long tick = Math.Max(now, GearList.AcquiredTicks(it) + 1);
        _stones[KeyFor(it)] = tick;
        _dirty = true;
        if (_stones.Count > Cap)
            foreach (var k in _stones.OrderBy(kv => kv.Value).Take(_stones.Count - Cap).Select(kv => kv.Key).ToList())
                _stones.Remove(k);
    }

    /// <summary>True when the item is tombstoned and its newest sighting is no newer than the tombstone.</summary>
    public bool ShouldHide(Item it) =>
        _stones.TryGetValue(KeyFor(it), out var tick) && GearList.AcquiredTicks(it) <= tick;

    /// <summary>Drop tombstones for any item sighted MORE recently than the tombstone — the player clearly
    /// still owns it (re-hovered or re-equipped). Returns how many were purged.</summary>
    public int ObserveSightings(IEnumerable<Item> items)
    {
        int n = 0;
        foreach (var it in items)
            if (_stones.TryGetValue(KeyFor(it), out var tick) && GearList.AcquiredTicks(it) > tick)
            { _stones.Remove(KeyFor(it)); _dirty = true; n++; }
        return n;
    }

    /// <summary>Observe sightings, then filter out the still-tombstoned items. Saves if anything changed.
    /// During a full TTS-log replay the caller passes <paramref name="observe"/>=false: un-prefixed (older
    /// shim) lines take "now" ticks on replay, so observing them would resurrect long-deleted items.</summary>
    public List<Item> Apply(List<Item> items, bool observe = true)
    {
        if (observe) ObserveSightings(items);
        var kept = items.Where(it => !ShouldHide(it)).ToList();
        if (_dirty) Save();
        return kept;
    }

    /// <summary>Hygiene: drop tombstones older than <paramref name="age"/> (by then the log can no longer
    /// resurrect the item — replay is bounded by the session marker). Returns how many were dropped.</summary>
    public int PurgeOlderThan(TimeSpan age, long nowUtcTicks)
    {
        long cutoff = nowUtcTicks - age.Ticks;
        var old = _stones.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList();
        foreach (var k in old) _stones.Remove(k);
        if (old.Count > 0) { _dirty = true; Save(); }
        return old.Count;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_stones));
            File.Move(tmp, _path, overwrite: true);
            _dirty = false;
        }
        catch { }
    }
}
