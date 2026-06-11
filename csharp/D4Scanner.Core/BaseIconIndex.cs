using System.Text.Json;

namespace D4Scanner.Core;

/// <summary>
/// Resolves a live item's Diablo IV image handle so <see cref="GameDataIcons"/> can extract the REAL art
/// locally. Two layers, read once from the cached <c>maxroll_data.min.json</c> item DB (per-item
/// <c>name</c> + <c>type</c> + <c>image</c>):
///   • a NAME index — the player's actual item by name, even when it isn't in the loaded build. Handles the
///     three D4 naming shapes: uniques/mythics are exact ("El'Druin, Sword of Justice"); legendaries are an
///     aspect-prefixed base name ("Crushing <b>Obsidian Blade</b>"); magic items are "<b>Tunic</b> of …".
///   • a TYPE index — a representative handle per base type (Sword/Helm/…), the fallback for rares (random
///     names) and anything the catalog doesn't list.
/// Same-name catalog entries (charm/currency twins, seasonal variants) are disambiguated by
/// <see cref="Pick"/>. The icon art always comes from the local D4 install, never a network source.
/// </summary>
public static class BaseIconIndex
{
    sealed record Entry(long Handle, string TypeKey, string Id, bool IsEquipment);

    static Dictionary<string, long>? _byType;
    static Dictionary<string, List<Entry>>? _byName;
    static readonly Dictionary<string, long?> _memo = new();   // (normName|normType) -> resolved handle (incl. null)
    static readonly object _gate = new();

    /// <summary>Test/inject hook for "is this handle extractable" (defaults to the real atlas map).</summary>
    public static Func<long, bool> HasMapping { private get; set; } = h => GameDataIcons.HasMapping(h);

    static string DataPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "d4scanner", "cache", "maxroll_data.min.json");

    // catalog id substrings that mark a NON-equipment entry (a charm/currency/quest twin of a real item name)
    static readonly string[] NonEquipMarkers =
        { "Charm", "Currency", "Quest", "Trophy", "Lorebook", "TemperManual", "Cache", "Gambling", "Talisman" };

    /// <summary>Reduce a GearParser ItemType or a data.min.json <c>type</c> to a common key
    /// (e.g. "Sword2H"/"1HSword" → "sword", "ChestArmor"/"Pants" → "chest"/"legs").</summary>
    static string Norm(string? s)
    {
        s = (s ?? "").ToLowerInvariant();
        foreach (var q in new[] { "1h", "2h", "offhand", "off hand", "book" }) s = s.Replace(q, "");
        s = new string(s.Where(char.IsLetter).ToArray());
        if (s.EndsWith("armor")) s = s[..^5];
        return s switch
        {
            "pant" or "pants" or "trousers" => "legs",
            "torso" or "tunic" => "chest",
            "focusbookoffhand" or "focusbook" => "focus",
            _ => s,
        };
    }

    static bool IsEquipmentId(string id) =>
        !NonEquipMarkers.Any(m => id.Contains(m, StringComparison.OrdinalIgnoreCase));

    static void Build()
    {
        if (_byType != null) return;
        lock (_gate)
        {
            if (_byType != null) return;
            var bestType = new Dictionary<string, (int score, long handle)>();
            var byName = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
            try
            {
                if (File.Exists(DataPath))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(DataPath));
                    if (doc.RootElement.TryGetProperty("items", out var items))
                        foreach (var it in items.EnumerateObject())
                        {
                            var v = it.Value;
                            if (!v.TryGetProperty("type", out var ty) || ty.ValueKind != JsonValueKind.String) continue;
                            if (!v.TryGetProperty("image", out var im) || im.ValueKind != JsonValueKind.Number) continue;
                            long handle = im.GetInt64();
                            if (handle <= 0) continue;
                            string id = it.Name;
                            string typeKey = Norm(ty.GetString());

                            // NAME index: every named item, for resolving the player's actual gear by name
                            if (v.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String)
                            {
                                var key = DiffEngine.Normalize(nm.GetString());
                                if (key.Length > 0)
                                {
                                    if (!byName.TryGetValue(key, out var lst)) byName[key] = lst = new();
                                    lst.Add(new Entry(handle, typeKey, id, IsEquipmentId(id)));
                                }
                            }

                            // TYPE index: a representative base handle per type (Generic > Normal > other)
                            if (id.Contains(' ') || id.StartsWith("zz", StringComparison.OrdinalIgnoreCase)) continue;
                            if (typeKey.Length == 0) continue;
                            int score = (id.Contains("Generic", StringComparison.OrdinalIgnoreCase) ? 2 : 0)
                                      + (id.Contains("Normal", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
                            if (!bestType.TryGetValue(typeKey, out var cur) || score > cur.score) bestType[typeKey] = (score, handle);
                        }
                }
            }
            catch { /* no cached data yet → empty indices → silhouettes exactly as before, no new failure mode */ }
            _byName = byName;
            _byType = bestType.ToDictionary(kv => kv.Key, kv => kv.Value.handle);
        }
    }

    /// <summary>Choose the best catalog entry for a resolved name, given the live item's type/slot.
    /// Order: type-match → extractable (in the atlas map) → equipment over charm/currency → non-seasonal,
    /// non-gambling, then "Generic" in the id (matching the type-index scoring style).</summary>
    static long? Pick(List<Entry> cands, string? itemType, string? slot)
    {
        if (cands.Count == 1) return cands[0].Handle;
        var wantType = Norm(itemType);
        if (wantType.Length == 0) wantType = Norm(slot);
        bool Seasonal(string id) => System.Text.RegularExpressions.Regex.IsMatch(id, @"^S\d+_");
        return cands
            .OrderByDescending(e => wantType.Length > 0 && e.TypeKey == wantType)
            .ThenByDescending(e => HasMapping(e.Handle))
            .ThenByDescending(e => e.IsEquipment)
            .ThenByDescending(e => !Seasonal(e.Id))
            .ThenByDescending(e => e.Id.Contains("Generic", StringComparison.OrdinalIgnoreCase))
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .First().Handle;
    }

    /// <summary>The image handle for a specific item, by NAME first (exact → affix-prefixed base name →
    /// magic "&lt;base&gt; of …" prefix), then the representative handle for its base TYPE. Null if unknown.</summary>
    public static long? HandleFor(string? name, string? itemType = null, string? slot = null)
    {
        Build();
        var norm = DiffEngine.Normalize(name);
        if (norm.Length == 0) return HandleForType(itemType, slot);

        string memoKey = norm + "|" + Norm(itemType);
        lock (_gate)
        {
            if (_memo.TryGetValue(memoKey, out var cached)) return cached;
            long? result = ResolveName(norm, itemType, slot) ?? HandleForType(itemType, slot);
            _memo[memoKey] = result;
            return result;
        }
    }

    // exact name, then longest word-boundary SUFFIX (legendary = "<aspect> <base>"), then longest word-boundary
    // PREFIX but only for the magic "<base> of <suffix>" shape. Longest-first so "obsidian blade" wins over "blade".
    static long? ResolveName(string norm, string? itemType, string? slot)
    {
        var idx = _byName!;
        if (idx.TryGetValue(norm, out var exact)) return Pick(exact, itemType, slot);

        var words = norm.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2) return null;

        // suffix probes: drop leading words one at a time (longest remaining first)
        for (int start = 1; start < words.Length; start++)
        {
            var key = string.Join(' ', words[start..]);
            if (idx.TryGetValue(key, out var cands)) return Pick(cands, itemType, slot);
        }
        // prefix probes: only when the name is the "<base> of <suffix>" magic shape
        int of = Array.IndexOf(words, "of");
        if (of >= 1)
            for (int end = of; end >= 1; end--)
            {
                var key = string.Join(' ', words[..end]);
                if (idx.TryGetValue(key, out var cands)) return Pick(cands, itemType, slot);
            }
        return null;
    }

    /// <summary>Representative image handle for a live item's base type (or its slot as a fallback); null if unknown.</summary>
    public static long? HandleForType(string? itemType, string? slot = null)
    {
        Build();
        var idx = _byType!;
        foreach (var cand in new[] { itemType, slot })
        {
            if (string.IsNullOrWhiteSpace(cand)) continue;
            var key = Norm(cand);
            if (key.Length > 0 && idx.TryGetValue(key, out var h)) return h;
        }
        return null;
    }

    /// <summary>For tests: build the indices from an inline data.min.json-shaped string (no disk read).</summary>
    public static void FromJson(string json)
    {
        lock (_gate)
        {
            var bestType = new Dictionary<string, (int score, long handle)>();
            var byName = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("items", out var items))
                foreach (var it in items.EnumerateObject())
                {
                    var v = it.Value;
                    if (!v.TryGetProperty("type", out var ty) || ty.ValueKind != JsonValueKind.String) continue;
                    if (!v.TryGetProperty("image", out var im) || im.ValueKind != JsonValueKind.Number) continue;
                    long handle = im.GetInt64();
                    if (handle <= 0) continue;
                    string id = it.Name; string typeKey = Norm(ty.GetString());
                    if (v.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String)
                    {
                        var key = DiffEngine.Normalize(nm.GetString());
                        if (key.Length > 0) { if (!byName.TryGetValue(key, out var lst)) byName[key] = lst = new(); lst.Add(new Entry(handle, typeKey, id, IsEquipmentId(id))); }
                    }
                    if (id.Contains(' ') || id.StartsWith("zz", StringComparison.OrdinalIgnoreCase)) continue;
                    if (typeKey.Length == 0) continue;
                    int score = (id.Contains("Generic", StringComparison.OrdinalIgnoreCase) ? 2 : 0)
                              + (id.Contains("Normal", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
                    if (!bestType.TryGetValue(typeKey, out var cur) || score > cur.score) bestType[typeKey] = (score, handle);
                }
            _byName = byName;
            _byType = bestType.ToDictionary(kv => kv.Key, kv => kv.Value.handle);
            _memo.Clear();
        }
    }

    /// <summary>Drop the cached indices (e.g. after a cache clear or a fresh Maxroll import).</summary>
    public static void Reset() { lock (_gate) { _byType = null; _byName = null; _memo.Clear(); } }
}
