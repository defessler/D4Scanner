using System.Text;
using System.Text.Json;

namespace D4Scanner.Core;

/// <summary>
/// Tails the D4 screen-reader log and maintains the current equipped <see cref="LiveBuild"/>,
/// raising <see cref="Updated"/> whenever new gear is parsed. Polls (robust for append-only logs).
/// </summary>
public sealed class LogWatcher : IDisposable
{
    readonly string _path;
    readonly bool _equippedOnly;
    readonly GearParser _seg = new();
    readonly Dictionary<string, Item> _items = new();
    readonly Dictionary<string, Item> _inv = new();
    long _pos;
    string _buf = "";
    System.Threading.Timer? _timer;

    public LiveBuild Build { get; private set; } = new();
    public event Action<LiveBuild>? Updated;

    public LogWatcher(string path, bool equippedOnly = true)
    {
        _path = path; _equippedOnly = equippedOnly;
    }

    public void Start(int pollMs = 500)
    {
        Poll();
        _timer = new System.Threading.Timer(_ => Poll(), null, pollMs, pollMs);
    }

    void Poll()
    {
        try
        {
            if (!File.Exists(_path)) return;
            long size = new FileInfo(_path).Length;
            if (size < _pos) { _pos = 0; _buf = ""; _items.Clear(); _inv.Clear(); }  // log cleared/rotated
            if (size <= _pos) return;

            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            fs.Seek(_pos, SeekOrigin.Begin);
            var bytes = new byte[size - _pos];
            int read = fs.Read(bytes, 0, bytes.Length);
            _pos = fs.Position;
            _buf += Encoding.UTF8.GetString(bytes, 0, read);

            var lines = _buf.Split('\n');
            _buf = lines[^1];   // keep the (possibly partial) last line for next poll
            bool changed = false;
            for (int i = 0; i < lines.Length - 1; i++)
            {
                var item = _seg.Feed(lines[i]);
                if (item == null) continue;
                // equipped items are the live build; non-equipped (bags/stash) feed upgrade-finding
                if (item.Equipped || !_equippedOnly) _items[(item.Slot ?? "?") + ":" + item.RawName] = item;
                else _inv[(item.Slot ?? "?") + ":" + item.RawName] = item;
                changed = true;
            }
            if (changed)
            {
                Build = new LiveBuild { Gear = LatestPerSlot(_items.Values), Inventory = _inv.Values.ToList() };
                Updated?.Invoke(Build);
            }
        }
        catch { /* file was mid-write; retry on the next tick */ }
    }

    /// <summary>For each slot base name keep only the N most recently logged items (last entries in the
    /// append-only log). This drops stale items when the player swaps gear mid-session — the old item
    /// stays in the log file but its entry is older, so it loses to the newer one here.
    /// N = 2 for rings (two ring slots), up to 4 for weapons (Barbarian has 4), 1 for everything else.</summary>
    static List<Item> LatestPerSlot(IEnumerable<Item> items)
    {
        return items
            .GroupBy(it => SlotBaseName(it.Slot ?? ""))
            .SelectMany(g =>
            {
                int max = g.Key == "ring" ? 2 : g.Key == "weapon" ? 4 : 1;
                return g.Reverse().Take(max);   // Reverse: last-in = most recently scanned = currently equipped
            })
            .ToList();
    }

    static string SlotBaseName(string slot) => System.Text.RegularExpressions.Regex.Replace(
        System.Text.RegularExpressions.Regex.Replace((slot).ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim(),
        @"\s*\d+$", "").Trim();

    public void Dispose() => _timer?.Dispose();

    /// <summary>One-shot parse of an entire log file into a LiveBuild (used by the CLI / tests).</summary>
    public static LiveBuild BuildFromFile(string path, bool equippedOnly = true)
    {
        var seg = new GearParser();
        var items = new Dictionary<string, Item>();
        foreach (var raw in File.ReadLines(path))
        {
            var item = seg.Feed(raw);
            if (item == null) continue;
            if (equippedOnly && !item.Equipped) continue;
            items[(item.Slot ?? "?") + ":" + item.RawName] = item;
        }
        return new LiveBuild { Gear = LatestPerSlot(items.Values) };
    }
}

public static class TargetLoader
{
    public static TargetBuild Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<TargetBuild>(json, Json.Opts) ?? new TargetBuild();
    }

    public static string DefaultLogPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "d4scanner", "d4_tts.log");
}
