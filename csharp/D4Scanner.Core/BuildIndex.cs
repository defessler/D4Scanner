using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace D4Scanner.Core;

/// <summary>A single Maxroll build-guide, for the import autocomplete.</summary>
public sealed record BuildEntry(string Slug, string Title, string Class)
{
    public string Display => string.IsNullOrEmpty(Class) ? Title : $"{Title}  ·  {Class}";
}

/// <summary>
/// Fetches and caches the full list of Maxroll D4 build guides so the app can offer fuzzy
/// autocomplete. Source is maxroll.gg/d4/sitemap.xml (every guide URL, ~180 entries — far more
/// complete than the JS-rendered index page); a readable title + class is derived from each slug.
/// Cached under %LOCALAPPDATA%\d4scanner\cache, refreshed at most once a day.
/// </summary>
public static class BuildIndex
{
    static readonly HttpClient Http = Create();
    static HttpClient Create()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        return h;
    }

    const string SitemapUrl = "https://maxroll.gg/d4/sitemap.xml";
    static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    static readonly string[] Classes =
        { "Barbarian", "Druid", "Necromancer", "Rogue", "Sorcerer", "Spiritborn", "Paladin", "Warlock" };
    static readonly HashSet<string> Small =
        new(StringComparer.OrdinalIgnoreCase) { "of", "the", "and", "to", "in", "on", "a" };

    static string CacheDir => AppPaths.CacheDir;
    /// <summary>Public so the Settings cache section can clear THIS file (it used to delete only a
    /// legacy index path and leave the real cache behind).</summary>
    public static string CachePath => Path.Combine(CacheDir, "build_index.json");

    /// <summary>Returns the guide list, preferring a fresh cache and falling back to a stale one if offline.</summary>
    public static async Task<List<BuildEntry>> LoadAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        var cached = ReadCache();
        bool fresh = cached is { Count: > 0 } && File.Exists(CachePath) &&
                     DateTime.UtcNow - File.GetLastWriteTimeUtc(CachePath) < MaxAge;
        if (fresh && !forceRefresh) return cached!;

        try
        {
            var xml = await Http.GetStringAsync(SitemapUrl, ct);
            var list = Parse(xml);
            if (list.Count > 0) { WriteCache(list); return list; }
        }
        catch { /* offline — fall through to whatever cache we have */ }
        return cached ?? new List<BuildEntry>();
    }

    static List<BuildEntry> Parse(string text)
    {
        var seen = new HashSet<string>();
        var list = new List<BuildEntry>();
        foreach (Match m in Regex.Matches(text, @"/d4/build-guides/([a-z0-9][a-z0-9\-]+)"))
        {
            var slug = m.Groups[1].Value;
            if (slug is "planners" or "tier-list" || !slug.Contains('-')) continue;  // sub-routes, not guides
            if (!seen.Add(slug)) continue;
            var (title, klass) = Derive(slug);
            list.Add(new BuildEntry(slug, title, klass));
        }
        return list.OrderBy(b => b.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static (string title, string klass) Derive(string slug)
    {
        var words = slug.Split('-', StringSplitOptions.RemoveEmptyEntries).ToList();
        words.RemoveAll(w => w.Equals("guide", StringComparison.OrdinalIgnoreCase));
        bool leveling = words.RemoveAll(w => w.Equals("leveling", StringComparison.OrdinalIgnoreCase)) > 0;

        string klass = "";
        for (int i = words.Count - 1; i >= 0; i--)
        {
            var match = Classes.FirstOrDefault(c => c.Equals(words[i], StringComparison.OrdinalIgnoreCase));
            if (match != null) { klass = match; words.RemoveAt(i); break; }
        }

        var title = TitleCase(words);
        if (title.Length == 0) title = TitleCase(slug.Split('-'));
        if (leveling) title += " (Leveling)";
        return (title, klass);
    }

    static string TitleCase(IEnumerable<string> words)
    {
        var list = words.Where(w => w.Length > 0).ToList();
        return string.Join(' ', list.Select((w, i) =>
            i > 0 && Small.Contains(w) ? w.ToLowerInvariant() : char.ToUpperInvariant(w[0]) + w[1..]));
    }

    // ---- fuzzy search ----

    /// <summary>Ranks guides against a free-text query; returns the best <paramref name="max"/> matches.</summary>
    public static List<BuildEntry> Search(IReadOnlyList<BuildEntry> all, string query, int max = 8)
    {
        var tokens = (query ?? "")
            .Split(new[] { ' ', '-', '\t', '_' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(Norm).Where(t => t.Length > 0).ToArray();
        if (tokens.Length == 0) return all.Take(max).ToList();

        var q = string.Concat(tokens);
        return all
            .Select(b => (b, score: Score(b, q, tokens)))
            .Where(t => t.score > 0)
            .OrderByDescending(t => t.score)
            .ThenBy(t => t.b.Title.Length)
            .Take(max)
            .Select(t => t.b)
            .ToList();
    }

    static int Score(BuildEntry b, string q, string[] tokens)
    {
        var title = Norm(b.Title);
        var all = Norm(b.Title + b.Class + b.Slug);
        if (title.StartsWith(q)) return 4000 - title.Length;          // best: title starts with the query
        if (title.Contains(q)) return 3000 - title.Length;           // query is contiguous in the title
        if (all.Contains(q)) return 2000 - all.Length;               // contiguous somewhere (class/slug)
        if (tokens.All(all.Contains)) return 1000 - all.Length;      // every word appears (any order)
        return 0;
    }

    static string Norm(string s) => new((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    // ---- cache ----

    static List<BuildEntry>? ReadCache()
    {
        try
        {
            if (!File.Exists(CachePath)) return null;
            return JsonSerializer.Deserialize<List<BuildEntry>>(File.ReadAllText(CachePath));
        }
        catch { return null; }
    }

    static void WriteCache(List<BuildEntry> list)
    {
        try { Directory.CreateDirectory(CacheDir); File.WriteAllText(CachePath, JsonSerializer.Serialize(list)); }
        catch { }
    }
}
