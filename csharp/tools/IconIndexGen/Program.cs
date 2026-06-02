using System.Collections.Concurrent;
using System.Text.Json;
using CASCLib;

// Regenerates the bundled handle -> atlas map for GameDataIcons.
//   maxroll item `image` == hImageHandle. Each 2DInventory_* atlas's d4data texture-def lists its
//   frames' hImageHandles, so scanning every atlas builds handle -> atlas. We only persist that map
//   (UV/format/dims are fetched fresh per-atlas at runtime), keeping the bundle small + re-bake-resilient.

string gameDir = args.Length > 0 ? args[0] : @"D:\Games\Blizzard\Diablo IV";
string outFile = args.Length > 1 ? args[1]
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "D4Scanner.Core", "Assets", "icon_atlas_map.json"));
const string defBase = "https://raw.githubusercontent.com/DiabloTools/d4data/master/json/base/meta/Texture/";

Console.WriteLine($"opening D4 CASC at {gameDir} …");
CASCConfig.ThrowOnFileNotFound = false;
CASCConfig.ValidateData = false;
var cdn = FindCdnConfigKey(gameDir);
if (cdn != null) CASCConfig.CDNConfigKeyOverride = cdn;
var casc = CASCHandler.OpenLocalStorage(gameDir, "fenris");
var toc = ((D4RootHandler)casc.Root).TocParser!;

var names = toc.SnoData.Values
    .Where(s => s.GroupId == SNOGroupD4.Texture && s.Name.StartsWith("2DInventory", StringComparison.OrdinalIgnoreCase))
    .Select(s => s.Name).Distinct().ToList();
Console.WriteLine($"{names.Count} 2DInventory atlases; fetching d4data defs …");

var cacheDir = Path.Combine(Path.GetTempPath(), "d4scanner_iconidx_defs");
Directory.CreateDirectory(cacheDir);
using var http = new HttpClient();
http.DefaultRequestHeaders.UserAgent.ParseAdd("d4scanner-iconindexgen");
var handleToAtlas = new ConcurrentDictionary<uint, string>();
int fetched = 0, missing = 0;
var sem = new SemaphoreSlim(12);
await Task.WhenAll(names.Select(async name =>
{
    await sem.WaitAsync();
    try
    {
        var cache = Path.Combine(cacheDir, name + ".json");
        string? json = File.Exists(cache) ? await File.ReadAllTextAsync(cache) : null;
        if (json == null)
        {
            try { json = await http.GetStringAsync(defBase + Uri.EscapeDataString(name) + ".tex.json"); await File.WriteAllTextAsync(cache, json); Interlocked.Increment(ref fetched); }
            catch { Interlocked.Increment(ref missing); return; }
        }
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("ptFrame", out var pf) && pf.ValueKind == JsonValueKind.Array)
            foreach (var f in pf.EnumerateArray())
                if (f.TryGetProperty("hImageHandle", out var h) && h.TryGetUInt32(out var hv) && hv != 0)
                    handleToAtlas[hv] = name;
    }
    catch { }
    finally { sem.Release(); }
}));

var atlases = handleToAtlas.Values.Distinct().OrderBy(x => x).ToList();
var idx = atlases.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i);
var map = new Dictionary<string, object>
{
    ["atlases"] = atlases,
    ["map"] = handleToAtlas.ToDictionary(kv => kv.Key.ToString(), kv => idx[kv.Value]),
};
Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
await File.WriteAllTextAsync(outFile, JsonSerializer.Serialize(map));
Console.WriteLine($"fetched {fetched}, missing {missing}, {handleToAtlas.Count} handles -> {atlases.Count} atlases");
Console.WriteLine($"wrote {outFile} ({new FileInfo(outFile).Length / 1024}KB)");

static string? FindCdnConfigKey(string gameDir)
{
    var cfg = Path.Combine(gameDir, "Data", "config");
    if (!Directory.Exists(cfg)) return null;
    foreach (var f in Directory.EnumerateFiles(cfg, "*", SearchOption.AllDirectories))
        try { using var sr = new StreamReader(f); if (sr.ReadLine()?.StartsWith("# CDN Configuration", StringComparison.OrdinalIgnoreCase) == true) return Path.GetFileName(f); }
        catch { }
    return null;
}
