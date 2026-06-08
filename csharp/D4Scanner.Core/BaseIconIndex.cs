using System.Text.Json;

namespace D4Scanner.Core;

/// <summary>
/// Maps a live item's base type (Sword / Helm / Boots / …) to a representative Diablo IV image
/// handle, so equipped legendaries and rares — which carry no handle of their own — still resolve a
/// REAL game-data icon (<see cref="GameDataIcons"/> extracts the art locally from the handle). The
/// type→handle lookup is read once from the cached <c>maxroll_data.min.json</c> item DB (its per-item
/// <c>type</c> + <c>image</c> fields); the icon art itself always comes from the local D4 install,
/// never from a network source.
/// </summary>
public static class BaseIconIndex
{
    static Dictionary<string, long>? _byType;
    static readonly object _gate = new();

    static string DataPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "d4scanner", "cache", "maxroll_data.min.json");

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

    static Dictionary<string, long> Index()
    {
        if (_byType != null) return _byType;
        lock (_gate)
        {
            if (_byType != null) return _byType;
            var best = new Dictionary<string, (int score, long handle)>();
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
                            if (id.Contains(' ') || id.StartsWith("zz", StringComparison.OrdinalIgnoreCase)) continue; // junk/placeholder rows
                            string key = Norm(ty.GetString());
                            if (key.Length == 0) continue;
                            // prefer a plain base icon: Generic > Normal-rarity > anything else
                            int score = (id.Contains("Generic", StringComparison.OrdinalIgnoreCase) ? 2 : 0)
                                      + (id.Contains("Normal", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
                            if (!best.TryGetValue(key, out var cur) || score > cur.score) best[key] = (score, handle);
                        }
                }
            }
            catch { /* no cached data yet → empty index → silhouettes exactly as before, no new failure mode */ }
            _byType = best.ToDictionary(kv => kv.Key, kv => kv.Value.handle);
            return _byType;
        }
    }

    /// <summary>Representative image handle for a live item's base type (or its slot as a fallback); null if unknown.</summary>
    public static long? HandleForType(string? itemType, string? slot = null)
    {
        var idx = Index();
        foreach (var cand in new[] { itemType, slot })
        {
            if (string.IsNullOrWhiteSpace(cand)) continue;
            var key = Norm(cand);
            if (key.Length > 0 && idx.TryGetValue(key, out var h)) return h;
        }
        return null;
    }

    /// <summary>Drop the cached index (e.g. after a cache clear or a fresh Maxroll import).</summary>
    public static void Reset() { lock (_gate) _byType = null; }
}
