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

    /// <summary>Raised (on a background thread) when a new icon finishes downloading.</summary>
    public static event Action? Changed;

    public static async Task LoadIndexAsync(CancellationToken ct = default)
    {
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

    /// <summary>
    /// Returns a local file path for the item's icon if it is already cached; otherwise returns
    /// null and (if the name is known) kicks off a background download that fires <see cref="Changed"/>.
    /// </summary>
    public static string? Get(string? name, string? klass = null)
    {
        if (!_loaded || string.IsNullOrWhiteSpace(name)) return null;
        var repoPath = Resolve(name!, klass);
        if (repoPath == null) return null;
        var local = Path.Combine(IconDir, repoPath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(local)) return local;
        Download(repoPath, local);
        return null;
    }

    static string? Resolve(string name, string? klass)
    {
        if (!_byName.TryGetValue(Norm(name), out var paths) || paths.Count == 0) return null;
        // prefer the class-agnostic "General" art, then the build's class folder, else whatever exists
        string? cls = klass == null ? null : paths.FirstOrDefault(p => Norm(p.Split('/')[0]).StartsWith(Norm(klass)[..Math.Min(4, Norm(klass).Length)]));
        return paths.FirstOrDefault(p => p.StartsWith("General/", StringComparison.OrdinalIgnoreCase)) ?? cls ?? paths[0];
    }

    static void Download(string repoPath, string local)
    {
        lock (_lock) { if (!_inflight.Add(repoPath)) return; }
        _ = Task.Run(async () =>
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(local)!);
                var url = RawBase + string.Join("/", repoPath.Split('/').Select(Uri.EscapeDataString));
                var bytes = await Http.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(local, bytes);
                Changed?.Invoke();
            }
            catch { /* leave uncached; UI keeps the silhouette */ }
            finally { lock (_lock) { _inflight.Remove(repoPath); } }
        });
    }
}
