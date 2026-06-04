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
    // scan-order lists: items appended on EVERY scan (including re-scans of the same item), so
    // Reverse().GroupBy().Take(N) gives the N most recently scanned items per slot — not the N
    // items whose key was first inserted earliest, which is the bug with dict insertion order.
    readonly List<Item> _itemsOrdered = new();
    readonly List<Item> _invOrdered = new();
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
                var key = (item.Slot ?? "?") + ":" + item.RawName;
                // equipped items are the live build; non-equipped (bags/stash) feed upgrade-finding
                if (item.Equipped || !_equippedOnly) { _items[key] = item; _itemsOrdered.Add(item); }
                else { _inv[key] = item; _invOrdered.Add(item); }
                changed = true;
            }
            if (changed)
            {
                Build = new LiveBuild { Gear = LatestPerSlot(_itemsOrdered), Inventory = LatestPerSlot(_invOrdered, 15) };
                Updated?.Invoke(Build);
            }
        }
        catch { /* file was mid-write; retry on the next tick */ }
    }

    /// <summary>For each slot base name keep only the N most recently logged items (last entries in the
    /// append-only log). This drops stale items when the player swaps gear mid-session — the old item
    /// stays in the log file but its entry is older, so it loses to the newer one here.
    /// For equipped gear: N = 2 for rings, up to 4 for weapons, 1 for everything else.
    /// For inventory: caller passes a higher limit (default 15) so bag items aren't over-pruned.</summary>
    static List<Item> LatestPerSlot(IEnumerable<Item> items, int overrideMax = 0)
    {
        return items
            .GroupBy(it => SlotBaseName(it.Slot ?? ""))
            .SelectMany(g =>
            {
                int max = overrideMax > 0 ? overrideMax : (g.Key == "ring" ? 2 : g.Key == "weapon" ? 4 : 1);
                return g.Reverse().Take(max);   // Reverse: last-in = most recently scanned
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
        // Use a list (not a dict) so items appear in FILE ORDER — re-scans of the same item
        // append a newer entry after the older one, letting LatestPerSlot correctly pick the
        // MOST RECENTLY SCANNED item per slot (via Reverse().Take(N)), not the one whose
        // slot:rawname key was first inserted earliest in the log.
        var ordered = new List<Item>();
        foreach (var raw in File.ReadLines(path))
        {
            var item = seg.Feed(raw);
            if (item == null) continue;
            if (equippedOnly && !item.Equipped) continue;
            ordered.Add(item);
        }
        return new LiveBuild { Gear = LatestPerSlot(ordered) };
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
