using System.Net.Http;
using System.Text.Json;

namespace D4Scanner.Core;

/// <summary>
/// Resolves real Diablo IV item art: first from the user's local game files (extracted via
/// GameDataIcons, keyed by Maxroll image handle), then from user-configured template sources.
/// Falls back to the slot silhouette when nothing is available. Raises <see cref="Changed"/>
/// when a newly extracted or downloaded icon is ready.
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

    public static string CacheDir => AppPaths.CacheDir;
    static string IconDir => Path.Combine(CacheDir, "icons");
    // IndexPath kept for cache-clear compatibility (file deleted by settings)
    public static string IndexPath => Path.Combine(CacheDir, "icon_index.json");

    static readonly object _lock = new();
    static readonly HashSet<string> _inflight = new();

    /// <summary>A user-configurable icon source: a URL template with a {key} placeholder, keyed by name|id|image.</summary>
    public sealed record TemplateSource(string Name, string UrlTemplate, string Key, bool Enabled);
    static List<TemplateSource> _templates = new();
    static string SourcesPath => Path.Combine(CacheDir, "icon_sources.json");

    /// <summary>Raised (on a background thread) when a new icon finishes downloading or extracting.</summary>
    public static event Action? Changed;

    static IconResolver()
    {
        GameDataIcons.Changed += () => Changed?.Invoke();
    }

    /// <summary>No-op — GitHub CDN removed. Template sources are still loaded.</summary>
    public static Task LoadIndexAsync(CancellationToken ct = default)
    {
        LoadSources();
        return Task.CompletedTask;
    }

    static string Safe(string s) => new(s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

    /// <summary>Collision-resistant cache filename for a per-source value. <see cref="Safe"/> folds every
    /// non-alphanumeric to '_', so "Andariel's Visage" and "Andariels Visage" would otherwise share one file
    /// (and one item's icon would show for the other). Appending a short hash of the ORIGINAL value keeps
    /// distinct names distinct. Public so the de-collision is headlessly unit-testable.</summary>
    public static string SafeFile(string val)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(val)));
        return Safe(val) + "_" + hash[..8] + ".img";
    }

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

    public static void ReloadSources() { LoadSources(); Changed?.Invoke(); }

    /// <summary>
    /// Source chain: (1) game-data PNG keyed by Maxroll image handle, (2) user-configured template
    /// sources. Returns a cached local path immediately or null while background extraction runs.
    /// </summary>
    public static string? Get(string? name, string? id, long? image)
    {
        // Source 1 (highest priority): real icon extracted from the user's local D4 install.
        // Keyed by Maxroll's hImageHandle. Returns cached PNG path or null while extracting.
        if (GameDataIcons.Get(image) is string gamePath) return gamePath;

        // Source 2+: user-configured template sources
        foreach (var s in _templates)
        {
            var val = s.Key.ToLowerInvariant() switch { "name" => name, "id" => id, "image" => image?.ToString(), _ => null };
            if (string.IsNullOrWhiteSpace(val)) continue;
            var cacheFile = Path.Combine(IconDir, "src", Safe(s.Name), SafeFile(val!));
            if (File.Exists(cacheFile)) return cacheFile;
            Download(s.UrlTemplate.Replace("{key}", Uri.EscapeDataString(val!)), cacheFile);
        }
        return null;
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
            catch { }
            finally { lock (_lock) { _inflight.Remove(url); } }
        });
    }
}
