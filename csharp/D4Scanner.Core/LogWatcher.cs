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
    GearParser _seg = new();
    readonly CharacterParser _char = new();   // total attributes + paragon level from the character sheet
    readonly SkillParser _skills = new();     // selected skills/passives + ranks from the skill tree
    readonly CharSelectParser _charSel = new();   // character-select screen: own characters + the picked name/class
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
        // Character panel headers — any of these set the context so Fast Path 3 fires
        ["Equipment"]  = "Character",  ["Head"]    = "Character",  ["Torso"]   = "Character",
        ["Hands"]      = "Character",  ["Legs"]    = "Character",  ["Feet"]    = "Character",
        ["Neck"]       = "Character",  ["Ring"]    = "Character",  ["Main Hand"] = "Character",
        ["Off-Hand"]   = "Character",  ["Ranged"]  = "Character",  ["Ranged Weapon"] = "Character",
        ["Helm"]       = "Character",  ["Chest"]   = "Character",  ["Gloves"]  = "Character",
        ["Pants"]      = "Character",  ["Boots"]   = "Character",  ["Amulet"]  = "Character",
        // Other panels
        ["Stash"]      = "Stash",      ["Inventory"] = "Inventory",
        ["Vendor"]     = "Vendor",     ["Purveyor of Curiosities"] = "Vendor",
        ["BUYBACK"]    = "Vendor",     ["Paragon"] = "Paragon",
        ["Available Points"] = "Paragon", ["Refund All"] = "Paragon",
        ["Skill Tree"] = "Skills",     ["MODIFIERS"] = "Skills",
        ["Talisman"]   = "Talisman",   ["Seals"]   = "Seals",
        ["Charms"]     = "Charms",     ["Runes"]   = "Runes",
        ["Gems"]       = "Gems",       ["Uniques"] = "Stash",
    };

    // Rarity words mirror GearParser.Rarities — a cleaned line equal to one of these (whole-line) is a
    // tooltip-shaped line even when the rest of the pipeline produced nothing.
    static readonly HashSet<string> RarityWords = new(StringComparer.OrdinalIgnoreCase)
        { "Mythic Unique", "Mythic", "Unique", "Legendary", "Rare", "Magic", "Common" };

    public LiveBuild Build { get; private set; } = new();
    public event Action<LiveBuild>? Updated;
    /// <summary>Fires when the TTS panel context first transitions to "Character" (user opened the character screen).
    /// Subscribe to auto-capture the portrait without requiring a manual button click.</summary>
    public event Action? CharacterPanelDetected;
    /// <summary>Fires when the character-select screen appears — i.e. the player left the game to switch
    /// characters. Subscribe to persist the current character's loadout and re-arm auto-identification.</summary>
    public event Action? CharacterSelectDetected;
    /// <summary>Fires when the player enters the world from character-select, carrying the picked
    /// character's identity (name; class/realm/paragon when the detail block was voiced).</summary>
    public event Action<CharSelectIdentity>? CharacterConfirmed;

    public LogWatcher(string path, bool equippedOnly = true, long startPos = 0)
    {
        _path = path; _equippedOnly = equippedOnly;
        _pos = startPos;   // non-zero to skip old log data (e.g. after a live-cache clear)
        _charSel.VisitStarted += () =>
        {
            // Player is back at character-select: drop EVERYTHING accumulated for the prior character —
            // gear, character sheet, and skills. Stale sheet/skills are not just cosmetic: they describe
            // the OLD character, and any class/paragon inference run on them after a switch would pull
            // identity back to the character just left (verified live before this reset existed).
            _items.Clear(); _inv.Clear(); _itemsOrdered.Clear(); _invOrdered.Clear();
            _seg = new GearParser(); _currentPanel = null;
            _char.Reset(); _skills.Reset();
            CharacterSelectDetected?.Invoke();
        };
        _charSel.Confirmed += id => CharacterConfirmed?.Invoke(id);
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
            if (size < _pos) { _pos = 0; _buf = ""; _items.Clear(); _inv.Clear(); _itemsOrdered.Clear(); _invOrdered.Clear(); _currentPanel = null; _seg = new GearParser(); _char.Reset(); _skills.Reset(); }  // log cleared/rotated
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
                // New shim session appended to the same file: drop the prior session's accumulated gear so a
                // stale prior-session loadout doesn't linger on the HAVE side after a restart/relaunch.
                if (rawLine.StartsWith("=== d4scanner tts shim attached", StringComparison.OrdinalIgnoreCase))
                { _items.Clear(); _inv.Clear(); _itemsOrdered.Clear(); _invOrdered.Clear(); _currentPanel = null; }

                // Character-select screen tracking (visit start → gear wipe + CharacterSelectDetected;
                // world entry → CharacterConfirmed with the picked name/class). Identity comes ONLY from
                // this screen — the in-game "Name | 70 (208) (VII)" lines are OTHER PLAYERS' nameplates.
                bool wasCharSel = _charSel.InCharSelect;
                _charSel.Feed(lines[i]);
                if (wasCharSel || _charSel.InCharSelect) changed = true;   // emit so the host sees Seen/identity progress
                // Character-select text is menu narration, not gameplay — keep it out of the gear/sheet/skill
                // parsers entirely (e.g. the detail block's own "Paragon N" line must not repopulate the sheet
                // that the visit just reset).
                if (_charSel.InCharSelect) continue;

                // Player-nameplate lines (incl. clan-tagged "<Tag> Name | …") are never gear and never identity.
                if (RosterParser.ParseLine(lines[i]) != null) continue;

                if (PanelMarkers.TryGetValue(rawLine, out var panel))
                {
                    var prev = _currentPanel;
                    _currentPanel = panel;
                    if (panel == "Character" && prev != "Character")
                    {
                        // Fresh panel session: reset position counters so hovering the same
                        // weapon again doesn't increment to position 2 and create a phantom duplicate.
                        _seg.ResetSlotPositions();
                        CharacterPanelDetected?.Invoke();
                    }
                }

                // capture total attributes + paragon level + skill ranks (independent of gear)
                if (_char.Feed(lines[i])) changed = true;
                if (_skills.Feed(lines[i])) changed = true;

                var item = _seg.Feed(lines[i]);
                if (item == null) continue;
                item.Source = ItemSource.Tts;
                item.UiPanel = _currentPanel;
                // Prefer the TRUE hover time from the line's '[ISO]' prefix so a replayed old loadout isn't
                // stamped 'now' at launch; fall back to the system clock only when the line was un-stamped.
                item.LastScannedTicks = (item.LogTimeUtc?.UtcTicks) ?? DateTime.UtcNow.Ticks;
                ClassifyContext(item, lines, i, lines.Length - 1);
                var key = (item.Slot ?? "?") + ":" + item.RawName;
                if (item.Slot is "charm" or "seal" or "rune") { /* Season 8 items routed to dedicated collections in Build */ }
                else if (item.Equipped || !_equippedOnly) { _items[key] = item; _itemsOrdered.Add(item); }
                else { _inv[key] = item; _invOrdered.Add(item); }
                changed = true;
            }
            if (changed)
            {
                // Trim ordered lists to avoid unbounded growth and slow LatestPerSlot scans
                if (_itemsOrdered.Count > 2000) _itemsOrdered.RemoveRange(0, _itemsOrdered.Count - 1000);
                if (_invOrdered.Count > 2000) _invOrdered.RemoveRange(0, _invOrdered.Count - 1000);
                Build = new LiveBuild { Gear = LatestPerSlot(_itemsOrdered), Inventory = LatestPerSlot(_invOrdered, 15), Character = _char.Character.Clone(), Skills = _skills.Skills, Roster = OwnRoster(_charSel) };
                Updated?.Invoke(Build);
            }
        }
        catch { /* file was mid-write; retry on the next tick */ }
    }

    /// <summary>Classify an item's UiContext and fix its Equipped flag using in-body signals and a post-end-marker
    /// lookahead. All decisions are driven by TTS text content — no memory reads, no cursor position needed.</summary>
    static void ClassifyContext(Item item, string[] lines, int i, int lineCount)
    {
        // Panel fast-path: if the active UI panel is known, pre-classify before any text signal.
        // Stash and Vendor items are always non-equipped; skip the lookahead scan entirely.
        if (item.UiPanel == "Stash"  && !item.FromCharPanel)
            { item.Equipped = false; item.Context = UiContext.StashItem; return; }
        if (item.UiPanel == "Vendor" && !item.FromCharPanel)
            { item.Equipped = false; item.Context = UiContext.VendorItem; return; }

        // Fast path 1: "Properties lost when equipped:" was seen inside the body — this is a bag/stash
        // comparison item even if the EQUIPPED marker appeared before its name.
        if (item.IsComparison)
        {
            item.Equipped = false;
            item.Context = UiContext.BagItem;
            return;
        }
        // Fast path 2: slot header (Head/Torso/Ring/Main Hand/…) appeared immediately before the block —
        // definitively the character panel; override any subsequent misleading Store/Take signal.
        if (item.FromCharPanel)
        {
            item.Equipped = true;
            item.Context = UiContext.WornGear;
            return;
        }
        // Fast path 3: item was hovered while the Character panel is active.
        // The IsComparison guard above already filters comparison tooltips.
        // If no definitive inventory/vendor/stash counter-signal fires, trust the panel context —
        // D4 Season 8 does not always emit a standalone EQUIPPED line, so requiring one is too strict.
        if (item.UiPanel == "Character" && !item.IsComparison)
        {
            for (int look = i + 1; look < Math.Min(i + 12, lineCount); look++)
            {
                var ctx = lines[look].Trim().ToLowerInvariant();
                if (ctx.Contains("store") || ctx.Contains("salvage") || ctx.Contains("mark as junk"))
                    { item.Equipped = false; item.Context = UiContext.BagItem; return; }
                if (ctx.Contains("buy"))  { item.Equipped = false; item.Context = UiContext.VendorItem; return; }
                if (ctx.Contains("take")) { item.Equipped = false; item.Context = UiContext.StashItem; return; }
            }
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
                if (item.Slot == "seal")  { item.Equipped = true;  item.Context = UiContext.HoradricSeal; return; }
                if (item.Slot == "charm") { item.Equipped = true;  item.Context = UiContext.Charm; return; }
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
    public static List<Item> LatestPerSlot(IEnumerable<Item> items, int overrideMax = 0)
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
                var seen      = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var namesSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                return g.Reverse()
                        .Where(it =>
                        {
                            var name = it.RawName.Length > 0 ? it.RawName : it.Name;
                            var key  = it.SlotPosition > 0 ? $"{name}:{it.SlotPosition}" : name;
                            if (!seen.Add(key)) return false;
                            // Secondary name-level dedup: if the same item was re-hovered at a
                            // different panel position (e.g. weapon at pos 1 then pos 2 after
                            // re-opening the char panel), collapse to the most-recent scan only.
                            return namesSeen.Add(name);
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

    // The player's OWN characters, as seen on the character-select screen (detail blocks carry the class).
    static List<RosterEntry> OwnRoster(CharSelectParser cs) =>
        cs.Seen.Select(s => new RosterEntry
        {
            Name = s.Name, Class = s.Class, Level = s.Level ?? 0, Paragon = s.Paragon ?? 0, Tier = s.Realm ?? "",
        }).ToList();

    public void Dispose() => _timer?.Dispose();

    /// <summary>One-shot parse of an entire log file into a LiveBuild (used by the CLI / tests).</summary>
    public static LiveBuild BuildFromFile(string path, bool equippedOnly = true)
    {
        var seg = new GearParser();
        var ch = new CharacterParser();
        var sk = new SkillParser();
        var charSel = new CharSelectParser();
        // Use a list (not a dict) so items appear in FILE ORDER — re-scans of the same item
        // append a newer entry after the older one, letting LatestPerSlot correctly pick the
        // MOST RECENTLY SCANNED item per slot (via Reverse().Take(N)), not the one whose
        // slot:rawname key was first inserted earliest in the log.
        var ordered = new List<Item>();
        // back at character-select: prior character's gear/sheet/skills are stale (matches Poll)
        charSel.VisitStarted += () => { ordered.Clear(); ch.Reset(); sk.Reset(); };
        var allLines = File.ReadAllLines(path);
        for (int i = 0; i < allLines.Length; i++)
        {
            // A new shim-session "attached" marker drops the prior session's accumulated gear (matches LogWatcher.Poll).
            if (GearParser.Clean(allLines[i]).StartsWith("=== d4scanner tts shim attached", StringComparison.OrdinalIgnoreCase)) ordered.Clear();
            charSel.Feed(allLines[i]);
            if (charSel.InCharSelect) continue;   // menu narration — never gear/sheet/skills (matches Poll)
            if (RosterParser.ParseLine(allLines[i]) != null) continue;   // player nameplate, never gear/identity
            ch.Feed(allLines[i]); sk.Feed(allLines[i]);
            var item = seg.Feed(allLines[i]);
            if (item == null) continue;
            ClassifyContext(item, allLines, i, allLines.Length);
            if (equippedOnly && !item.Equipped) continue;
            ordered.Add(item);
        }
        return new LiveBuild { Gear = LatestPerSlot(ordered), Character = ch.Character.Clone(), Skills = sk.Skills, Roster = OwnRoster(charSel) };
    }

    /// <summary>
    /// Re-runs the full TTS parse → classify → dedup pipeline over a log file and captures every
    /// intermediate stage, for the in-app diagnostics view. Faithful to the live <see cref="Poll"/>
    /// loop: same panel state machine, same <see cref="ClassifyContext"/>, same <see cref="LatestPerSlot"/>.
    /// </summary>
    public static TtsDiagReport Diagnose(string path, int rawTailLines = 60)
    {
        bool exists = File.Exists(path);
        var rep = DiagnoseLines(exists ? File.ReadAllLines(path) : Array.Empty<string>(), rawTailLines);
        rep.LogPath = path;
        rep.LogExists = exists;
        if (exists)
        {
            var fi = new FileInfo(path);
            rep.LogBytes = fi.Length;
            rep.LastModifiedUtc = fi.LastWriteTimeUtc;
        }
        return rep;
    }

    /// <summary>Pipeline-introspection core (file-free, so tests can feed raw lines directly).</summary>
    public static TtsDiagReport DiagnoseLines(string[] allLines, int rawTailLines = 60)
    {
        var rep = new TtsDiagReport { TotalLines = allLines.Length };
        rep.RawTail = allLines.Where(l => l.Trim().Length > 0).Reverse().Take(rawTailLines).Reverse().ToList();

        var seg = new GearParser();
        string? currentPanel = null;
        var ordered = new List<Item>();
        for (int i = 0; i < allLines.Length; i++)
        {
            var clean = GearParser.Clean(allLines[i]);
            if (clean.StartsWith("=== d4scanner", StringComparison.OrdinalIgnoreCase)) rep.SessionMarkers++;
            if (clean.Equals("EQUIPPED", StringComparison.OrdinalIgnoreCase)) rep.EquippedTokens++;
            bool isItemPower = clean.Contains("Item Power", StringComparison.OrdinalIgnoreCase);
            if (isItemPower) rep.ItemPowerLines++;
            if (isItemPower || RarityWords.Contains(clean)) rep.TooltipShapedLines++;
            if (PanelMarkers.TryGetValue(clean, out var panel))
            {
                var prev = currentPanel;
                currentPanel = panel;
                if (panel == "Character" && prev != "Character") seg.ResetSlotPositions();
            }
            var item = seg.Feed(allLines[i]);
            if (item == null) continue;
            item.Source = ItemSource.Tts;
            item.UiPanel = currentPanel;
            ClassifyContext(item, allLines, i, allLines.Length);
            ordered.Add(item);
        }

        var equipped = ordered.Where(it => it.Equipped).ToList();
        var final = LatestPerSlot(equipped);
        var finalSet = new HashSet<Item>(final);   // reference identity — final holds the same Item objects
        rep.FinalEquipped = final;
        foreach (var it in ordered)
        {
            bool inFinal = finalSet.Contains(it);
            rep.Items.Add(new TtsDiagItem
            {
                Name = it.Name,
                RawName = it.RawName,
                Slot = it.Slot ?? "?",
                SlotPosition = it.SlotPosition,
                ItemPower = it.ItemPower,
                Rarity = it.Rarity,
                Affixes = it.Affixes.Select(a => a.Text).ToList(),
                Panel = it.UiPanel,
                Equipped = it.Equipped,
                Context = it.Context.ToString(),
                InFinal = inFinal,
                DropReason = inFinal ? null
                    : !it.Equipped ? $"not equipped ({it.Context})"
                    : $"superseded — a newer scan of '{it.Slot}' replaced it",
            });
        }
        rep.CompletedBlocks = rep.Items.Count;
        AssignHealth(rep);
        return rep;
    }

    /// <summary>Derive the capture-health verdict from the raw + parsed signal counts. Kept in Core so it
    /// is unit-tested; the App only color-codes <see cref="TtsDiagReport.Health"/> into a banner.</summary>
    static void AssignHealth(TtsDiagReport rep)
    {
        const int TooltipFloor = 8;   // a few stray rarity words shouldn't trip the warning; a real sweep clears this
        if (rep.TotalLines == 0)
        {
            rep.Health = CaptureHealth.NoData;
            rep.HealthSummary = "No log data — TTS capture hasn't written anything yet.";
        }
        else if (rep.CompletedBlocks > 0)
        {
            rep.Health = CaptureHealth.Healthy;
            rep.HealthSummary = $"Looks healthy — {rep.CompletedBlocks} item(s) parsed, {rep.EquippedTokens} EQUIPPED token(s), {rep.FinalEquipped.Count} displayed.";
        }
        else if (rep.TooltipShapedLines >= TooltipFloor)
        {
            // Tooltip-shaped lines flowing in but NOTHING parsed (even with EQUIPPED tokens) is the strong
            // signal that a season string change broke the parser — not just "panel not opened yet".
            rep.Health = CaptureHealth.Warning;
            rep.HealthSummary = $"WARNING: {rep.TooltipShapedLines} tooltip-shaped lines but 0 parsed items — the TTS format may have changed.";
        }
        else
        {
            rep.Health = CaptureHealth.NoPanel;
            rep.HealthSummary = "No character panel opened yet — open D4's character sheet (press C) and hover your gear.";
        }
    }
}

/// <summary>Capture-health verdict for the TTS pipeline, derived in <see cref="LogWatcher.DiagnoseLines"/>.</summary>
public enum CaptureHealth
{
    NoData,    // log empty or missing — nothing to judge
    NoPanel,   // pipeline quiet AND no tooltip shapes — user just hasn't opened the character sheet
    Healthy,   // tooltip shapes present and the pipeline parsed items
    Warning,   // tooltip shapes present but 0 parsed items — format likely changed (even if EQUIPPED tokens appeared)
}

/// <summary>One parsed item with its full classification trail, for the TTS diagnostics view.</summary>
public sealed class TtsDiagItem
{
    public string Name { get; set; } = "";
    public string RawName { get; set; } = "";
    public string Slot { get; set; } = "";
    public int SlotPosition { get; set; }
    public int? ItemPower { get; set; }
    public string? Rarity { get; set; }
    public List<string> Affixes { get; set; } = new();
    public string? Panel { get; set; }      // active UI panel when parsed (null if no nav line seen)
    public bool Equipped { get; set; }
    public string Context { get; set; } = "";   // UiContext after classification
    public bool InFinal { get; set; }       // survived to the set the app actually displays
    public string? DropReason { get; set; } // why it didn't (null when InFinal)
}

/// <summary>Full TTS pipeline snapshot: raw → parsed → classified → final, for the diagnostics view.</summary>
public sealed class TtsDiagReport
{
    public string LogPath { get; set; } = "";
    public bool LogExists { get; set; }
    public long LogBytes { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public int TotalLines { get; set; }
    public int SessionMarkers { get; set; }
    public int TooltipShapedLines { get; set; }   // cleaned lines that are a rarity word or contain "Item Power"
    public int ItemPowerLines { get; set; }        // cleaned lines containing "Item Power"
    public int EquippedTokens { get; set; }        // standalone "EQUIPPED" lines (block-equip markers)
    public int CompletedBlocks { get; set; }       // tooltip blocks that parsed into an Item (== Items.Count)
    public CaptureHealth Health { get; set; }      // overall capture-health verdict
    public string HealthSummary { get; set; } = "";// one-line human-readable verdict for the banner
    public List<string> RawTail { get; set; } = new();
    public List<TtsDiagItem> Items { get; set; } = new();   // every parsed item, in file order
    public List<Item> FinalEquipped { get; set; } = new();  // what the app would display
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
