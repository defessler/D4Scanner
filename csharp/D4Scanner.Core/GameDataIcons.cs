using System.Net.Http;
using System.Text.Json;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using CASCLib;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace D4Scanner.Core;

/// <summary>
/// Extracts real Diablo IV item icons from the user's own install, on demand, in pure managed C#.
///
/// Maxroll's per-item <c>image</c> value is the item's UI image handle (<c>hImageHandle</c>). A small
/// bundled <c>handle → atlas</c> map (generated from the community DiabloTools/d4data project) says which
/// <c>2DInventory_*</c> atlas holds the icon; that atlas's texture definition (fetched + cached from d4data)
/// gives the UV rectangle, the BCn pixel format and the dimensions; and the raw atlas pixels are read from
/// the local CASC archive (<c>Base\payload\&lt;snoId&gt;</c>). The icon is BCn-decoded, cropped to the UV
/// rect and cached as a PNG. Nothing is hosted or shipped; if the game isn't installed (or anything fails)
/// the resolver falls back to its other sources.
/// </summary>
public static class GameDataIcons
{
    /// <summary>Raised (on a background thread) when a freshly extracted icon becomes available.</summary>
    public static event Action? Changed;

    /// <summary>Set by the app (e.g. from CaptureSetup.GameDir()); otherwise common install paths are probed.</summary>
    public static string? GameDir { get; set; }

    /// <summary>Master switch — when false the resolver skips game-data extraction entirely.</summary>
    public static bool Enabled { get; set; } = true;

    const string DefBase = "https://raw.githubusercontent.com/DiabloTools/d4data/master/json/base/meta/Texture/";

    static string CacheRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "d4scanner", "cache", "icons", "game");
    static string DefCacheDir => Path.Combine(CacheRoot, "defs");

    static readonly HttpClient Http = Create();
    static HttpClient Create()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("d4scanner");
        return h;
    }

    // ---- bundled handle -> atlas map (embedded resource) ----
    static string[] _atlases = Array.Empty<string>();
    static Dictionary<uint, int> _handleAtlas = new();
    static bool _mapLoaded;
    static readonly object _mapGate = new();

    static void LoadMap()
    {
        if (_mapLoaded) return;
        lock (_mapGate)
        {
            if (_mapLoaded) return;
            try
            {
                var asm = typeof(GameDataIcons).Assembly;
                var res = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("icon_atlas_map.json", StringComparison.OrdinalIgnoreCase));
                if (res != null)
                {
                    using var s = asm.GetManifestResourceStream(res)!;
                    using var doc = JsonDocument.Parse(s);
                    _atlases = doc.RootElement.GetProperty("atlases").EnumerateArray().Select(e => e.GetString() ?? "").ToArray();
                    var map = new Dictionary<uint, int>();
                    foreach (var p in doc.RootElement.GetProperty("map").EnumerateObject())
                        if (uint.TryParse(p.Name, out var h)) map[h] = p.Value.GetInt32();
                    _handleAtlas = map;
                }
            }
            catch { /* no map -> feature inert */ }
            _mapLoaded = true;
        }
    }

    // ---- local CASC (opened once, lazily, on a background thread) ----
    static D4RootHandler? _root;
    static Dictionary<string, int>? _atlasSno;   // atlas texture name -> snoId
    static volatile int _cascState;              // 0 unstarted, 1 (unused), 2 ready, 3 failed
    static long _cascFailedAtUtcTicks;           // UTC tick of the most recent failed open (0 = never)
    static readonly object _cascGate = new();
    static readonly object _readGate = new();    // serialize CASC reads (not guaranteed thread-safe)

    /// <summary>Minimum wait between CASC open retries. The open usually fails because Diablo IV itself
    /// is running and holds the storage — the NORMAL case for this app, which runs alongside the game —
    /// so a failure must never latch for the whole session (it used to: one bad open at startup meant
    /// silhouettes everywhere until the app was restarted, even long after the game closed).</summary>
    public static TimeSpan CascRetryBackoff { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Pure retry gate (extracted so the backoff arithmetic is headlessly testable):
    /// retry when there was no recorded failure, or the backoff has fully elapsed since it.</summary>
    public static bool ShouldRetryCasc(long nowUtcTicks, long failedAtUtcTicks, TimeSpan backoff) =>
        failedAtUtcTicks == 0 || nowUtcTicks - failedAtUtcTicks >= backoff.Ticks;

    // single dedicated extraction worker: the local CASC is opened once and icons are decoded serially,
    // so a screen full of slots can't spawn dozens of threads that all block on the one-time open.
    static readonly System.Collections.Concurrent.BlockingCollection<uint> _queue = new();
    static readonly HashSet<uint> _queued = new();
    static readonly object _qGate = new();
    static int _workerStarted;

    /// <summary>
    /// Returns a cached icon PNG path for a Maxroll <c>image</c> handle, or null if not yet available /
    /// not extractable. On a miss for a known item handle it kicks off a background extraction and raises
    /// <see cref="Changed"/> when the PNG lands (mirrors <see cref="IconResolver"/>'s download pattern).
    /// </summary>
    /// <summary>True when this image handle is in the bundled atlas map — i.e. its art CAN be extracted
    /// locally. Used to disambiguate same-name catalog entries toward one that can actually render.</summary>
    public static bool HasMapping(long? image)
    {
        if (image is not long handle || handle <= 0 || handle > uint.MaxValue) return false;
        LoadMap();
        return _handleAtlas.ContainsKey((uint)handle);
    }

    public static string? Get(long? image)
    {
        if (!Enabled || image is not long handle || handle <= 0 || handle > uint.MaxValue) return null;
        LoadMap();
        if (!_handleAtlas.ContainsKey((uint)handle)) return null;   // not an item icon we have a mapping for
        var png = Path.Combine(CacheRoot, handle + ".png");
        if (File.Exists(png)) return png;
        if (_cascState == 3)
        {
            // Earlier open failed — typically Diablo IV holding the storage. Re-probe after the
            // backoff (the actual open runs on the background worker, never the caller's thread).
            if (!ShouldRetryCasc(DateTime.UtcNow.Ticks, Interlocked.Read(ref _cascFailedAtUtcTicks), CascRetryBackoff))
                return null;
            lock (_cascGate) { if (_cascState == 3) _cascState = 0; }
        }
        Enqueue((uint)handle);
        return null;
    }

    static void Enqueue(uint handle)
    {
        lock (_qGate) { if (!_queued.Add(handle)) return; }
        if (Interlocked.CompareExchange(ref _workerStarted, 1, 0) == 0)
            new Thread(Worker) { IsBackground = true, Name = "d4-icon-extract" }.Start();
        _queue.Add(handle);
    }

    static void Worker()
    {
        if (!EnsureCasc())
        {
            // Open failed: don't strand the queued handles or the worker slot. Clearing the de-dup set
            // and freeing _workerStarted lets the retry path (Get → Enqueue after the backoff) spin up
            // a fresh worker; leaving them latched meant no icon could EVER extract again this session.
            lock (_qGate) { _queued.Clear(); }
            while (_queue.TryTake(out _)) { }
            Interlocked.Exchange(ref _workerStarted, 0);
            return;
        }
        foreach (var handle in _queue.GetConsumingEnumerable())
        {
            try
            {
                var png = Path.Combine(CacheRoot, handle + ".png");
                if (!File.Exists(png) && Extract(handle, png)) Changed?.Invoke();
            }
            catch { /* leave uncached; caller keeps the fallback icon */ }
            finally { lock (_qGate) { _queued.Remove(handle); } }
        }
    }

    static bool Extract(uint handle, string png)
    {
        if (!EnsureCasc()) return false;
        var atlas = _atlases[_handleAtlas[handle]];
        var def = GetDef(atlas);
        if (def == null) return false;
        if (!def.Uv.TryGetValue(handle, out var uv)) return false;  // version drift between d4data + install
        if (!_atlasSno!.TryGetValue(atlas, out var sno)) return false;

        byte[]? payload;
        lock (_readGate)
        {
            using var s = _root!.OpenByPath($@"Base\payload\{sno}");
            if (s == null) return false;
            using var ms = new MemoryStream(); s.CopyTo(ms); payload = ms.ToArray();
        }

        DecodeCrop(payload, def, uv, png);
        return File.Exists(png);
    }

    static bool EnsureCasc()
    {
        if (_cascState == 2) return true;
        if (_cascState == 3) return false;
        lock (_cascGate)
        {
            if (_cascState == 2) return true;
            if (_cascState == 3) return false;
            try
            {
                var dir = GameDir ?? ProbeGameDir();
                if (dir == null || !File.Exists(Path.Combine(dir, ".build.info")))
                { Interlocked.Exchange(ref _cascFailedAtUtcTicks, DateTime.UtcNow.Ticks); _cascState = 3; return false; }
                CASCConfig.ThrowOnFileNotFound = false;
                CASCConfig.ValidateData = false;
                var cdn = FindCdnConfigKey(dir);
                if (cdn != null) CASCConfig.CDNConfigKeyOverride = cdn;
                var casc = CASCHandler.OpenLocalStorage(dir, "fenris");
                if (casc.Root is not D4RootHandler root || root.TocParser == null)
                { Interlocked.Exchange(ref _cascFailedAtUtcTicks, DateTime.UtcNow.Ticks); _cascState = 3; return false; }
                var sno = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in root.TocParser.SnoData)
                    if (kv.Value.GroupId == SNOGroupD4.Texture && kv.Value.Name.StartsWith("2DInventory", StringComparison.OrdinalIgnoreCase))
                        sno[kv.Value.Name] = kv.Key;
                _root = root; _atlasSno = sno; _cascState = 2;
                return true;
            }
            catch { Interlocked.Exchange(ref _cascFailedAtUtcTicks, DateTime.UtcNow.Ticks); _cascState = 3; return false; }
        }
    }

    // ---- d4data texture definition (UV rects + format + dims), fetched + cached per atlas ----
    sealed class Def { public uint Fmt; public int W, H; public Dictionary<uint, (float u0, float v0, float u1, float v1)> Uv = new(); }
    static readonly Dictionary<string, Def?> _defs = new();
    static readonly object _defGate = new();

    static Def? GetDef(string atlas)
    {
        lock (_defGate) { if (_defs.TryGetValue(atlas, out var cached)) return cached; }
        Def? def = null;
        var file = Path.Combine(DefCacheDir, atlas + ".json");
        try
        {
            Directory.CreateDirectory(DefCacheDir);
            string? json = File.Exists(file) ? File.ReadAllText(file) : null;
            if (json == null)
            {
                json = Http.GetStringAsync(DefBase + Uri.EscapeDataString(atlas) + ".tex.json").GetAwaiter().GetResult();
                var tmp = file + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, file, overwrite: true);   // atomic — never leave a half-written def cache
            }
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            def = new Def { Fmt = r.GetProperty("eTexFormat").GetUInt32(), W = r.GetProperty("dwWidth").GetInt32(), H = r.GetProperty("dwHeight").GetInt32() };
            foreach (var f in r.GetProperty("ptFrame").EnumerateArray())
                if (f.TryGetProperty("hImageHandle", out var h) && h.TryGetUInt32(out var hv))
                    def.Uv[hv] = (f.GetProperty("flU0").GetSingle(), f.GetProperty("flV0").GetSingle(),
                                  f.GetProperty("flU1").GetSingle(), f.GetProperty("flV1").GetSingle());
        }
        catch
        {
            def = null;
            // A corrupt/partial cached def re-throws on every call; drop it so the next request re-fetches.
            try { if (File.Exists(file)) File.Delete(file); } catch { }
        }
        // Cache only SUCCESS: a one-time fetch failure (offline, GitHub hiccup) must not latch this atlas to
        // silhouettes for the whole process — leaving it uncached lets the next request retry.
        if (def != null) lock (_defGate) { _defs[atlas] = def; }
        return def;
    }

    static void DecodeCrop(byte[] payload, Def def, (float u0, float v0, float u1, float v1) uv, string png)
    {
        var fmt = MapFormat(def.Fmt);
        if (fmt == null) return;
        int aw = Align(def.W, def.Fmt is 9 or 10 or 46 or 47 ? 128 : 64);

        var px = new BcDecoder().DecodeRaw(payload, aw, def.H, fmt.Value);
        var bytes = new byte[px.Length * 4];
        for (int i = 0; i < px.Length; i++) { bytes[i * 4] = px[i].r; bytes[i * 4 + 1] = px[i].g; bytes[i * 4 + 2] = px[i].b; bytes[i * 4 + 3] = px[i].a; }

        using var img = Image.LoadPixelData<Rgba32>(bytes, aw, def.H);
        int x0 = (int)Math.Floor(uv.u0 * def.W), y0 = (int)Math.Floor(uv.v0 * def.H);
        int x1 = (int)Math.Ceiling(uv.u1 * def.W), y1 = (int)Math.Ceiling(uv.v1 * def.H);
        x0 = Math.Clamp(x0, 0, aw - 1); x1 = Math.Clamp(x1, x0 + 1, aw);
        y0 = Math.Clamp(y0, 0, def.H - 1); y1 = Math.Clamp(y1, y0 + 1, def.H);
        img.Mutate(c => c.Crop(new Rectangle(x0, y0, x1 - x0, y1 - y0)));

        Directory.CreateDirectory(CacheRoot);
        var tmp = png + ".tmp";
        img.SaveAsPng(tmp);
        File.Move(tmp, png, overwrite: true);
    }

    static CompressionFormat? MapFormat(uint e) => e switch
    {
        9 or 10 or 46 => CompressionFormat.Bc1,
        47 => CompressionFormat.Bc1WithAlpha,
        48 => CompressionFormat.Bc2,
        12 or 49 => CompressionFormat.Bc3,
        41 => CompressionFormat.Bc4,
        42 => CompressionFormat.Bc5,
        44 or 50 => CompressionFormat.Bc7,
        43 => CompressionFormat.Bc6S,
        51 => CompressionFormat.Bc6U,
        _ => null,
    };

    static int Align(int n, int a) { int r = n % a; return r == 0 ? n : n + (a - r); }

    // ---- install discovery + CASC config quirk ----
    static string? ProbeGameDir()
    {
        string[] candidates =
        {
            @"C:\Program Files (x86)\Diablo IV", @"C:\Program Files\Diablo IV",
            @"C:\Program Files (x86)\Battle.net\Games\Diablo IV", @"D:\Games\Blizzard\Diablo IV",
            @"D:\Diablo IV", @"E:\Diablo IV", @"C:\Games\Diablo IV",
        };
        return candidates.FirstOrDefault(d => File.Exists(Path.Combine(d, "Diablo IV.exe")));
    }

    // D4's .build.info CDN key can be stale on disk; the real CDN config is the Data\config file whose
    // first line is "# CDN Configuration". CascLib's CDNConfigKeyOverride lets us point at it.
    static string? FindCdnConfigKey(string gameDir)
    {
        var cfg = Path.Combine(gameDir, "Data", "config");
        if (!Directory.Exists(cfg)) return null;
        foreach (var f in Directory.EnumerateFiles(cfg, "*", SearchOption.AllDirectories))
            try { using var sr = new StreamReader(f); if (sr.ReadLine()?.StartsWith("# CDN Configuration", StringComparison.OrdinalIgnoreCase) == true) return Path.GetFileName(f); }
            catch { }
        return null;
    }
}
