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

    // Panel context state machine: tracks the active D4 UI panel from voiced navigation lines.
    // Provides richer UiContext classification (e.g. TakeAction from Stash vs. Inventory).
    string? _currentPanel;
    static readonly Dictionary<string, string> PanelMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Equipment"] = "Character",   ["Head"] = "Character",   ["Torso"] = "Character",
        ["Stash"]     = "Stash",       ["Inventory"] = "Inventory",
        ["Vendor"]    = "Vendor",      ["Purveyor of Curiosities"] = "Vendor",
        ["BUYBACK"]   = "Vendor",      ["Paragon"] = "Paragon",
        ["Available Points"] = "Paragon", ["Refund All"] = "Paragon",
        ["Skill Tree"] = "Skills",     ["MODIFIERS"] = "Skills",
        ["Talisman"] = "Talisman",     ["Seals"] = "Seals",
        ["Charms"] = "Charms",
    };

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
                // Panel state machine: update current panel from navigation lines
                var rawLine = GearParser.Clean(lines[i]);
                if (PanelMarkers.TryGetValue(rawLine, out var panel)) _currentPanel = panel;

                var item = _seg.Feed(lines[i]);
                if (item == null) continue;
                item.UiPanel = _currentPanel;   // attach the active panel to the item for richer context
                ClassifyContext(item, lines, i, lines.Length - 1);
                var key = (item.Slot ?? "?") + ":" + item.RawName;
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

    /// <summary>Classify an item's UiContext and fix its Equipped flag using in-body signals and a post-end-marker
    /// lookahead. All decisions are driven by TTS text content — no memory reads, no cursor position needed.</summary>
    static void ClassifyContext(Item item, string[] lines, int i, int lineCount)
    {
        // Fast path 1: "Properties lost when equipped:" was seen inside the body — this is a bag/stash
        // comparison item even if the EQUIPPED marker appeared before its name.
        if (item.IsComparison)
        {
            item.Equipped = false;
            item.Context = UiContext.BagItem;
            return;
        }
        // Fast path 2: slot header (Head/Torso/Ring/Main Hand/…) appeared immediately before the EQUIPPED
        // block — definitively the character panel; override any subsequent misleading Store/Take signal.
        if (item.FromCharPanel)
        {
            item.Equipped = true;
            item.Context = UiContext.WornGear;
            return;
        }
        // Fallback: scan up to 12 post-end-marker lines for the action verb.
        if (item.Equipped)
        {
            for (int look = i + 1; look < Math.Min(i + 12, lineCount); look++)
            {
                var ctx = lines[look].Trim().ToLowerInvariant();
                if (ctx.Contains("unequip"))   { item.Context = UiContext.WornGear;  return; }
                if (ctx.Contains("store") || ctx.Contains("mark as junk") || ctx.Contains("salvage"))
                    { item.Equipped = false; item.Context = UiContext.BagItem;  return; }
                if (ctx.Contains("take"))      { item.Equipped = false; item.Context = UiContext.StashItem; return; }
                if (ctx.Contains("buy"))       { item.Equipped = false; item.Context = UiContext.VendorItem; return; }
                if (ctx.Contains("unlock") || ctx.Contains("refund"))
                    { item.Equipped = false; item.Context = UiContext.ParagonNode; return; }
            }
            // No confirming signal within the window — trust the EQUIPPED marker as before
            item.Context = UiContext.WornGear;
        }
        else
        {
            // Not initially flagged as equipped; classify via post-end action if visible
            for (int look = i + 1; look < Math.Min(i + 8, lineCount); look++)
            {
                var ctx = lines[look].Trim().ToLowerInvariant();
                if (ctx.Contains("take"))  { item.Context = UiContext.StashItem; return; }
                if (ctx.Contains("store")) { item.Context = UiContext.BagItem;   return; }
                if (ctx.Contains("buy"))   { item.Context = UiContext.VendorItem; return; }
            }
        }
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
                // Reverse (most-recently-scanned first) then deduplicate before taking N.
                // Dedup key logic:
                //   - Item scanned from the character panel (SlotPosition > 0): key = "Name:Position"
                //     → two rings/weapons with the same name in different panel positions are DISTINCT
                //       (player genuinely has two of that item equipped in different slots)
                //   - Item scanned without a panel position (SlotPosition == 0, e.g. bag hover): key = "Name"
                //     → re-hovering the same item collapses to one entry
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                return g.Reverse()
                        .Where(it =>
                        {
                            var name = it.RawName.Length > 0 ? it.RawName : it.Name;
                            var key = it.SlotPosition > 0 ? $"{name}:{it.SlotPosition}" : name;
                            return seen.Add(key);
                        })
                        // Stable ordering so ring/weapon assignment doesn't flip with scan order:
                        // items scanned from the character panel (SlotPosition > 0) sort by their
                        // known panel position; items without position sort after, alphabetically.
                        .OrderBy(it => it.SlotPosition > 0 ? it.SlotPosition : 999)
                        .ThenBy(it => it.RawName.Length > 0 ? it.RawName : it.Name, StringComparer.OrdinalIgnoreCase)
                        .Take(max);
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
        var allLines = File.ReadAllLines(path);
        for (int i = 0; i < allLines.Length; i++)
        {
            var item = seg.Feed(allLines[i]);
            if (item == null) continue;
            ClassifyContext(item, allLines, i, allLines.Length);
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
