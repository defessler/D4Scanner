using System.Net.Http;
using System.Text.Json;

namespace D4Scanner.Core;

/// <summary>
/// Resolves real Diablo IV item art at runtime from the community "diablo4icons" repo
/// (uniques are filed by their display name, e.g. General/Doombringer.webp). Icons are
/// downloaded and cached under %LOCALAPPDATA%\d4scanner\cache\icons and never bundled in
/// the release. A name index is fetched once (cached weekly). Raises <see cref="Changed"/>
/// when a newly downloaded icon becomes available so the UI can refresh.
/// </summary>
public static class IconResolver
{
    static readonly HttpClient Http = Create();
    static HttpClient Create()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("d4scanner");
        return h;
    }

    const string TreeUrl = "https://api.github.com/repos/Howard-Starfield/diablo4icons/git/trees/main?recursive=1";
    const string RawBase = "https://raw.githubusercontent.com/Howard-Starfield/diablo4icons/main/";
    static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    static string CacheDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "d4scanner", "cache");
    static string IconDir => Path.Combine(CacheDir, "icons");
    static string IndexPath => Path.Combine(CacheDir, "icon_index.json");

    static Dictionary<string, List<string>> _byName = new();   // normalized item name -> repo paths
    static volatile bool _loaded;
    static readonly object _lock = new();
    static readonly HashSet<string> _inflight = new();

    /// <summary>A user-configurable icon source: a URL template with a {key} placeholder, keyed by name|id|image.</summary>
    public sealed record TemplateSource(string Name, string UrlTemplate, string Key, bool Enabled);
    static List<TemplateSource> _templates = new();
    static string SourcesPath => Path.Combine(CacheDir, "icon_sources.json");

    /// <summary>Raised (on a background thread) when a new icon finishes downloading.</summary>
    public static event Action? Changed;

    public static async Task LoadIndexAsync(CancellationToken ct = default)
    {
        LoadSources();
        try
        {
            if (File.Exists(IndexPath) && DateTime.UtcNow - File.GetLastWriteTimeUtc(IndexPath) < MaxAge)
            {
                var cached = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(await File.ReadAllTextAsync(IndexPath, ct));
                if (cached is { Count: > 0 }) { _byName = cached; _loaded = true; return; }
            }
            var json = await Http.GetStringAsync(TreeUrl, ct);
            using var doc = JsonDocument.Parse(json);
            var map = new Dictionary<string, List<string>>();
            foreach (var n in doc.RootElement.GetProperty("tree").EnumerateArray())
            {
                var p = n.TryGetProperty("path", out var pe) ? pe.GetString() : null;
                if (p == null || !p.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) continue;
                var file = Path.GetFileNameWithoutExtension(p);
                var key = Norm(file);
                if (key.Length == 0) continue;
                if (!map.TryGetValue(key, out var list)) map[key] = list = new();
                list.Add(p);
            }
            if (map.Count > 0)
            {
                _byName = map; _loaded = true;
                Directory.CreateDirectory(CacheDir);
                await File.WriteAllTextAsync(IndexPath, JsonSerializer.Serialize(map), ct);
            }
        }
        catch { /* offline / rate-limited — icons just won't resolve */ }
    }

    static string Norm(string s) => new((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    static string Safe(string s) => new(s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

    // Loads user-configurable icon sources from icon_sources.json (auto-created with disabled examples
    // documenting the format). These are template sources tried AFTER the built-in diablo4icons index.
    static void LoadSources()
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            if (!File.Exists(SourcesPath))
            {
                var examples = new List<TemplateSource>
                {
                    new("example-by-name",  "https://your-cdn.example/d4/icons/{key}.webp", "name",  false),
                    new("example-by-id",    "https://your-cdn.example/d4/items/{key}.webp", "id",    false),
                    new("example-by-image", "https://your-cdn.example/d4/img/{key}.webp",   "image", false),
                };
                File.WriteAllText(SourcesPath, JsonSerializer.Serialize(examples, new JsonSerializerOptions { WriteIndented = true }));
            }
            var list = JsonSerializer.Deserialize<List<TemplateSource>>(File.ReadAllText(SourcesPath));
            _templates = list?.Where(s => s.Enabled && !string.IsNullOrWhiteSpace(s.UrlTemplate) && s.UrlTemplate.Contains("{key}")).ToList() ?? new();
        }
        catch { _templates = new(); }
    }

    /// <summary>Re-read icon_sources.json (after the user edits it).</summary>
    public static void ReloadSources() { LoadSources(); Changed?.Invoke(); }

    public static string? Get(string? name, string? klass = null) => Get(name, null, null, klass);

    /// <summary>
    /// Resolve an item's icon across the source chain — built-in diablo4icons (by name), then each
    /// enabled template source (by name/id/image) — returning the first cached hit (by priority) and
    /// kicking off background downloads for the rest. Null until something lands; UI fires <see cref="Changed"/>.
    /// </summary>
    public static string? Get(string? name, string? id, long? image, string? klass)
    {
        // source 1 (built-in): diablo4icons index, keyed by item name
        if (_loaded && !string.IsNullOrWhiteSpace(name) && Resolve(name!, klass) is string repoPath)
        {
            var local = Path.Combine(IconDir, repoPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(local)) return local;
            Download(RawBase + string.Join("/", repoPath.Split('/').Select(Uri.EscapeDataString)), local);
        }
        // sources 2+ : configurable template sources, in order
        foreach (var s in _templates)
        {
            var val = s.Key.ToLowerInvariant() switch { "name" => name, "id" => id, "image" => image?.ToString(), _ => null };
            if (string.IsNullOrWhiteSpace(val)) continue;
            var cacheFile = Path.Combine(IconDir, "src", Safe(s.Name), Safe(val!) + ".img");
            if (File.Exists(cacheFile)) return cacheFile;
            Download(s.UrlTemplate.Replace("{key}", Uri.EscapeDataString(val!)), cacheFile);
        }
        return null;
    }

    static string? Resolve(string name, string? klass)
    {
        if (!_byName.TryGetValue(Norm(name), out var paths) || paths.Count == 0) return null;
        // prefer the class-agnostic "General" art, then the build's class folder, else whatever exists
        string? cls = klass == null ? null : paths.FirstOrDefault(p => Norm(p.Split('/')[0]).StartsWith(Norm(klass)[..Math.Min(4, Norm(klass).Length)]));
        return paths.FirstOrDefault(p => p.StartsWith("General/", StringComparison.OrdinalIgnoreCase)) ?? cls ?? paths[0];
    }

    static void Download(string url, string local)
    {
        lock (_lock) { if (!_inflight.Add(url)) return; }
        _ = Task.Run(async () =>
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(local)!);
                var bytes = await Http.GetByteArrayAsync(url);
                if (bytes.Length > 0) { await File.WriteAllBytesAsync(local, bytes); Changed?.Invoke(); }
            }
            catch { /* leave uncached; UI keeps the silhouette / next source */ }
            finally { lock (_lock) { _inflight.Remove(url); } }
        });
    }
}
