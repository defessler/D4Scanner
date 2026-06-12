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

    // Cross-chunk classification: a tooltip block that ends near a poll-chunk edge has not seen its
    // action tail ("Unequip" / "Sell" / "Mark as Junk" …) yet — those lines arrive in the NEXT chunk.
    // Classifying one-shot against the truncated chunk silently hit the Equipped=true defaults (the
    // verified vendor-gear leak), so parsed items now wait in _pending until their lookahead window is
    // complete (or the game goes quiet for a couple of polls), reading from a rolling line buffer.
    readonly List<string> _recent = new();   // rolling raw-line buffer the lookahead scans
    long _recentStart;                        // absolute line number of _recent[0]
    long _lineNo;                             // absolute count of complete lines processed
    sealed class PendingItem { public Item Item = null!; public long EndLine; public int Polls; }
    readonly List<PendingItem> _pending = new();

    // Panel context state machine: tracks the active D4 UI panel from voiced navigation lines.
    // Provides richer UiContext classification (e.g. TakeAction from Stash vs. Inventory).
    string? _currentPanel;
    static readonly Dictionary<string, string> PanelMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        // Character panel headers — ONLY the anatomical slot names the character sheet itself voices.
        // Bare ITEM-TYPE words (Helm/Chest/Gloves/Pants/Boots/Amulet/Ring) are deliberately absent:
        // the Purveyor of Curiosities' gamble categories and the inventory screen's paper-doll labels
        // voice exactly those words, and mapping them here flipped the panel Vendor→Character one line
        // after "BUYBACK" (verified live) — every subsequent vendor hover then classified as worn gear.
        ["Equipment"]  = "Character",  ["Head"]    = "Character",  ["Torso"]   = "Character",
        ["Hands"]      = "Character",  ["Legs"]    = "Character",  ["Feet"]    = "Character",
        ["Neck"]       = "Character",  ["Main Hand"] = "Character",
        ["Off-Hand"]   = "Character",  ["Ranged"]  = "Character",  ["Ranged Weapon"] = "Character",
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
    /// <summary>True once the initial catch-up parse has reached the end of the log (the first poll runs on
    /// the thread pool, so the UI can show a "catching up…" state until this flips).</summary>
    public bool IsCaughtUp { get; private set; }
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
    /// <summary>Fires on a LIVE "shim detached" marker (the game just exited) — the safe moment to
    /// rotate the log: the session is complete and nothing new is being written. Never fired during
    /// the initial catch-up replay of historical sessions.</summary>
    public event Action? SessionEnded;

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
            _pending.Clear(); _recent.Clear(); _recentStart = _lineNo;
            _char.Reset(); _skills.Reset();
            CharacterSelectDetected?.Invoke();
        };
        _charSel.Confirmed += id => CharacterConfirmed?.Invoke(id);
    }

    public void Start(int pollMs = 500)
    {
        // The first poll runs on the timer (thread pool), NOT synchronously: the initial catch-up parse of
        // a large cumulative log would otherwise block the caller — the UI thread at startup — for seconds.
        _timer = new System.Threading.Timer(_ => Poll(), null, 0, pollMs);
    }

    /// <summary>Byte offset of the line holding the LAST "shim attached" session marker (0 when absent).
    /// Everything before it is prior-session history whose effects are already persisted (profiles /
    /// active pointer / last-known gear), and which the marker would wipe on replay anyway — so startup
    /// can begin reading there instead of re-parsing the whole cumulative log.</summary>
    public static long LastSessionStartPos(string path)
    {
        try
        {
            if (!File.Exists(path)) return 0;
            var bytes = File.ReadAllBytes(path);
            var marker = System.Text.Encoding.UTF8.GetBytes("=== d4scanner tts shim attached");
            int at = bytes.AsSpan().LastIndexOf(marker);
            if (at <= 0) return 0;
            int lineStart = at;
            while (lineStart > 0 && bytes[lineStart - 1] != (byte)'\n') lineStart--;   // include the [ISO] prefix
            return lineStart;
        }
        catch { return 0; }
    }

    int _polling;   // ticks can fire while a long catch-up parse is still running — skip, don't overlap

    void Poll()
    {
        if (System.Threading.Interlocked.Exchange(ref _polling, 1) == 1) return;
        try
        {
            if (!File.Exists(_path)) return;
            long size = new FileInfo(_path).Length;
            if (size < _pos)   // log cleared/rotated
            {
                _pos = 0; _buf = ""; _items.Clear(); _inv.Clear(); _itemsOrdered.Clear(); _invOrdered.Clear();
                _currentPanel = null; _seg = new GearParser(); _char.Reset(); _skills.Reset();
                _pending.Clear(); _recent.Clear(); _recentStart = 0; _lineNo = 0;
            }

            bool changed;
            if (size > _pos)
            {
                using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                fs.Seek(_pos, SeekOrigin.Begin);
                var bytes = new byte[size - _pos];
                int read = fs.Read(bytes, 0, bytes.Length);
                _pos = fs.Position;
                _buf += Encoding.UTF8.GetString(bytes, 0, read);

                var lines = _buf.Split('\n');
                _buf = lines[^1];   // keep the (possibly partial) last line for next poll
                changed = FeedChunk(lines, lines.Length - 1);
            }
            else
                // No new data — the game went quiet. Age the pending classifications so a block whose
                // action tail never arrives still resolves (with its safe default) within a few polls.
                changed = _pending.Count > 0 && ResolvePending();

            bool firstCatchUp = !IsCaughtUp;
            IsCaughtUp = true;
            if (changed || firstCatchUp)   // always emit once after catch-up so the UI can drop "catching up…"
            {
                RebuildSnapshot();
                Updated?.Invoke(Build);
            }
        }
        catch { /* file was mid-write; retry on the next tick */ }
        finally { System.Threading.Interlocked.Exchange(ref _polling, 0); }
    }

    /// <summary>
    /// Process one poll chunk of COMPLETE log lines (the live path; <see cref="Poll"/> excludes the
    /// trailing partial line via <paramref name="completeCount"/>). Public so tests can drive the real
    /// chunked pipeline — including cross-chunk classification — without a timer or file.
    /// </summary>
    public bool FeedChunk(IReadOnlyList<string> lines, int completeCount = -1)
    {
        if (completeCount < 0) completeCount = lines.Count;
        bool changed = false;
        for (int i = 0; i < completeCount; i++)
        {
            // Every complete line enters the rolling buffer FIRST so pending lookaheads see the same
            // stream the parser saw (markers, nameplates and menu noise included — they bound windows).
            _recent.Add(lines[i]);
            _lineNo++;

            var rawLine = GearParser.Clean(lines[i]);
            // New shim session appended to the same file: drop the prior session's accumulated gear so a
            // stale prior-session loadout doesn't linger on the HAVE side after a restart/relaunch.
            if (rawLine.StartsWith("=== d4scanner tts shim attached", StringComparison.OrdinalIgnoreCase))
            { _items.Clear(); _inv.Clear(); _itemsOrdered.Clear(); _invOrdered.Clear(); _currentPanel = null; _pending.Clear(); }
            // LIVE session end (game exited) — the rotation-safe moment; replayed history never fires it.
            if (IsCaughtUp && rawLine.StartsWith("=== d4scanner tts shim detached", StringComparison.OrdinalIgnoreCase))
                SessionEnded?.Invoke();

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
            _pending.Add(new PendingItem { Item = item, EndLine = _lineNo - 1 });
        }
        changed |= ResolvePending();
        TrimRecent();
        if (changed) RebuildSnapshot();
        return changed;
    }

    /// <summary>Classify every pending item whose lookahead window is now complete (a definitive token
    /// or block boundary arrived, or the window filled). After ~2 quiet polls a pending item is forced —
    /// classified with whatever lines exist — so a final hover before the game goes silent still lands.</summary>
    bool ResolvePending()
    {
        bool changed = false;
        for (int p = 0; p < _pending.Count;)
        {
            var pend = _pending[p];
            bool force = pend.Polls++ >= 2;
            int rel = (int)(pend.EndLine - _recentStart);
            if (!ClassifyContext(pend.Item, _recent, rel, _recent.Count, force)) { p++; continue; }
            Commit(pend.Item);
            changed = true;
            _pending.RemoveAt(p);
        }
        return changed;
    }

    /// <summary>Route a classified item into the gear/inventory accumulators (the equipped gate).</summary>
    void Commit(Item item)
    {
        var key = (item.Slot ?? "?") + ":" + item.RawName;
        if (item.Slot is "charm" or "seal" or "rune") { /* Season 8 items routed to dedicated collections in Build */ }
        else if (item.Equipped || !_equippedOnly) { _items[key] = item; _itemsOrdered.Add(item); }
        else
        {
            _inv[key] = item; _invOrdered.Add(item);
            // Self-heal: a fresh, correctly-classified NON-equipped sighting evicts an earlier
            // wrongly-equipped copy of the same item (the gate used to be a one-way ratchet — once a
            // vendor hover leaked into gear, no later sighting could ever displace it).
            _items.Remove(key);
            _itemsOrdered.RemoveAll(x => ((x.Slot ?? "?") + ":" + x.RawName) == key);
        }
    }

    void RebuildSnapshot()
    {
        // Trim ordered lists to avoid unbounded growth and slow LatestPerSlot scans
        if (_itemsOrdered.Count > 2000) _itemsOrdered.RemoveRange(0, _itemsOrdered.Count - 1000);
        if (_invOrdered.Count > 2000) _invOrdered.RemoveRange(0, _invOrdered.Count - 1000);
        // Inventory dedups by CONTENT fingerprint (TTS text is exact): genuine duplicates of a
        // same-named bag item stay distinct instead of collapsing to one entry.
        Build = new LiveBuild { Gear = LatestPerSlot(_itemsOrdered), Inventory = LatestPerSlot(_invOrdered, 15, contentIdentity: true), Character = _char.Character.Clone(), Skills = _skills.Skills, Roster = OwnRoster(_charSel) };
    }

    void TrimRecent()
    {
        long keepFrom = _lineNo - 256;
        foreach (var p in _pending) if (p.EndLine < keepFrom) keepFrom = p.EndLine;   // never trim an open window
        int drop = (int)(keepFrom - _recentStart);
        if (drop > 0) { _recent.RemoveRange(0, drop); _recentStart += drop; }
    }

    /// <summary>Max post-end-marker lines the action-tail scan inspects. The real bag-hover tail runs
    /// ~11 lines with "Mark as Junk" LAST, and keyword-explainer lines can push tokens further — the old
    /// 11-line window missed them and fell through to an Equipped=true default (the vendor-gear leak).</summary>
    internal const int LookaheadMax = 24;

    enum TailSignal { None, Incomplete, Worn, Bag, Stash, Vendor, Paragon }

    // Word-boundary action tokens. \b matters: "Restore +4 Primary Resource" must not match "store"
    // (a verified false demote of genuinely worn gear), and \bequip\b must not match "unequip"/"equipped".
    static readonly System.Text.RegularExpressions.Regex ReBagAction =
        new(@"\b(store|sell|drop|equip)\b", System.Text.RegularExpressions.RegexOptions.Compiled);
    static readonly System.Text.RegularExpressions.Regex ReTakeAction =
        new(@"\btake\b", System.Text.RegularExpressions.RegexOptions.Compiled);
    static readonly System.Text.RegularExpressions.Regex ReBuyAction =
        new(@"\bbuy(back)?\b", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Scan the lines after an item's end marker for the action verb that reveals where it sits:
    /// "Unequip" = worn; "Sell"/"Equip"/"Drop"/"Mark as Junk/Favorite"/comparison toggles = bag;
    /// "Take" = stash; "Buy" = vendor. The window ends at the next block boundary (the following hover's
    /// EQUIPPED/name/header — its tail can't belong to this item) or after <see cref="LookaheadMax"/>
    /// lines. Returns <see cref="TailSignal.Incomplete"/> when the window is still open at the end of the
    /// available lines and <paramref name="force"/> is false — the caller retries when more lines arrive.</summary>
    static TailSignal ScanTail(IReadOnlyList<string> lines, int i, int lineCount, bool force)
    {
        int seen = 0;
        for (int look = i + 1; look < lineCount && seen < LookaheadMax; look++, seen++)
        {
            var clean = GearParser.Clean(lines[look]);
            if (clean.Length == 0) continue;
            var ctx = clean.ToLowerInvariant();
            if (ctx.Contains("unequip")) return TailSignal.Worn;
            if (ctx.Contains("mark as junk") || ctx.Contains("mark as favorite") || ctx.Contains("salvage")
                || ctx.Contains("comparison") || ReBagAction.IsMatch(ctx))
                return TailSignal.Bag;
            if (ReBuyAction.IsMatch(ctx)) return TailSignal.Vendor;
            if (ReTakeAction.IsMatch(ctx)) return TailSignal.Stash;
            if (ctx.Contains("unlock") || ctx.Contains("refund")) return TailSignal.Paragon;
            if (PanelMarkers.ContainsKey(clean) || GearParser.IsBlockBoundary(clean)) return TailSignal.None;
        }
        if (seen >= LookaheadMax) return TailSignal.None;          // window filled — no action token exists
        return force ? TailSignal.None : TailSignal.Incomplete;    // ran out of lines mid-window
    }

    /// <summary>Classify an item's UiContext and fix its Equipped flag using in-body signals, the panel
    /// state machine, and a post-end-marker action-tail lookahead. All decisions are driven by TTS text
    /// content — no memory reads, no cursor position needed. Returns false when the classification needs
    /// more lines (tail still arriving across a poll-chunk edge) and <paramref name="force"/> is false.
    ///
    /// Bias note: D4 voices a standalone "EQUIPPED" line immediately BEFORE the name of nearly every
    /// comparison-enabled bag/stash/vendor hover (it labels the comparison overlay, not the hovered item),
    /// so the EQUIPPED pre-flag alone is NOT worn evidence. When no corroborating signal exists — no
    /// character-panel context, no slot header, no "Unequip" — the default is now NOT-equipped: a missed
    /// genuine item self-corrects on the next character-panel hover, but a vendor item stamped as worn
    /// silently replaces real gear and persists (the bug this rewrite fixes).</summary>
    static bool ClassifyContext(Item item, IReadOnlyList<string> lines, int i, int lineCount, bool force = true)
    {
        // Panel fast-path: if the active UI panel is known, pre-classify before any text signal.
        // Stash and Vendor items are always non-equipped; skip the lookahead scan entirely.
        if (item.UiPanel == "Stash"  && !item.FromCharPanel)
            { item.Equipped = false; item.Context = UiContext.StashItem; return true; }
        if (item.UiPanel == "Vendor" && !item.FromCharPanel)
            { item.Equipped = false; item.Context = UiContext.VendorItem; return true; }

        // Fast path 1: "Properties lost when equipped:" was seen inside the body — this is a bag/stash
        // comparison item even if the EQUIPPED marker appeared before its name.
        if (item.IsComparison)
        {
            item.Equipped = false;
            item.Context = UiContext.BagItem;
            return true;
        }

        // Seals/charms route to dedicated Talisman collections, never the gear list — classify directly
        // (previously this fired inside the lookahead loop, so it silently depended on window contents).
        if (item.Slot is "seal" or "charm")
        {
            item.Equipped = true;
            item.Context = item.Slot == "seal" ? UiContext.HoradricSeal : UiContext.Charm;
            return true;
        }

        // Fast path 2: slot header (Head/Torso/Ring/Main Hand/…) immediately preceded the block AND the
        // panel state agrees (Character, or unknown — headers themselves set the panel, so null only means
        // no marker was ever voiced). A header word with a CONFLICTING panel (Vendor gamble category
        // "Ring", inventory paper-doll label) is a hijack — fall through to the action-tail scan instead.
        if (item.FromCharPanel && item.UiPanel is "Character" or null)
        {
            item.Equipped = true;
            item.Context = UiContext.WornGear;
            return true;
        }

        var sig = ScanTail(lines, i, lineCount, force);
        if (sig == TailSignal.Incomplete) return false;
        switch (sig)
        {
            case TailSignal.Worn:    item.Equipped = true;  item.Context = UiContext.WornGear;    return true;
            case TailSignal.Bag:     item.Equipped = false; item.Context = UiContext.BagItem;     return true;
            case TailSignal.Stash:   item.Equipped = false; item.Context = UiContext.StashItem;   return true;
            case TailSignal.Vendor:  item.Equipped = false; item.Context = UiContext.VendorItem;  return true;
            case TailSignal.Paragon: item.Equipped = false; item.Context = UiContext.ParagonNode; return true;
        }

        // Window closed with no action token at all.
        if (item.UiPanel == "Character")
        {
            // The Character panel is now set ONLY by the sheet's own anatomical headers (bare item-type
            // words no longer poison it), so trusting it is safe — and required: D4 doesn't always voice
            // a standalone EQUIPPED line on the character sheet.
            item.Equipped = true;
            item.Context = UiContext.WornGear;
            return true;
        }
        if (item.Equipped)
        {
            // Only the EQUIPPED voice line said "worn" — see the bias note above. Fail safe: inventory.
            item.Equipped = false;
            item.Context = item.UiPanel switch
            {
                "Vendor" => UiContext.VendorItem,
                "Stash"  => UiContext.StashItem,
                _        => UiContext.BagItem,
            };
        }
        return true;
    }

    /// <summary>For each slot base name keep only the N most recently logged items (last entries in the
    /// append-only log). This drops stale items when the player swaps gear mid-session — the old item
    /// stays in the log file but its entry is older, so it loses to the newer one here.
    /// For equipped gear: N = 2 for rings, up to 4 for weapons, 1 for everything else.
    /// For inventory: caller passes a higher limit (default 15) so bag items aren't over-pruned.
    /// <paramref name="contentIdentity"/>: dedup by the item's CONTENT fingerprint instead of its name,
    /// so genuine duplicates of a same-named item (different rolls/metadata) survive as distinct entries.
    /// Use for TTS inventory only — exact text makes content identity trustworthy there.</summary>
    public static List<Item> LatestPerSlot(IEnumerable<Item> items, int overrideMax = 0, bool contentIdentity = false)
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
                //   - contentIdentity: key = content fingerprint, and the secondary name-level collapse
                //     is skipped — same-named items with different content are different items.
                var seen      = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var namesSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                return g.Reverse()
                        .Where(it =>
                        {
                            var name = it.RawName.Length > 0 ? it.RawName : it.Name;
                            var id   = contentIdentity ? GearList.Fingerprint(it) : name;
                            var key  = it.SlotPosition > 0 ? $"{id}:{it.SlotPosition}" : id;
                            if (!seen.Add(key)) return false;
                            // Secondary name-level dedup: if the same item was re-hovered at a
                            // different panel position (e.g. weapon at pos 1 then pos 2 after
                            // re-opening the char panel), collapse to the most-recent scan only.
                            return contentIdentity || namesSeen.Add(name);
                        })
                        // Stable ordering so ring/weapon assignment doesn't flip with scan order:
                        // items scanned from the character panel (SlotPosition > 0) sort by their
                        // known panel position. Ties (and position-less items) break by RECENCY, not
                        // alphabet — with an alphabetical tiebreak, one misclassified "Adventurer's …"
                        // could own a 1-cap slot for the whole session because the genuine item
                        // re-hovered LATER still lost the A-vs-C name comparison (verified live).
                        .OrderBy(it => it.SlotPosition > 0 ? it.SlotPosition : 999)
                        .ThenByDescending(it => it.LogTimeUtc?.UtcTicks ?? it.LastScannedTicks)
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

    /// <summary>Feed whole ARCHIVED log files through the live parsing pipeline before tailing begins —
    /// the full-replay path when rotation has split history across files (without this, "rebuild gear
    /// by replaying the log" would silently replay only the post-rotation tail). Call before
    /// <see cref="Start"/>; safe from any thread (Start only arms the timer).</summary>
    public void Prefeed(IEnumerable<string> archiveFiles)
    {
        foreach (var f in archiveFiles)
        {
            string[] lines;
            try { lines = File.ReadAllLines(f); } catch { continue; }
            FeedChunk(lines);
        }
    }

    /// <summary>One-shot parse of an entire log file into a LiveBuild (used by the CLI / tests).</summary>
    public static LiveBuild BuildFromFile(string path, bool equippedOnly = true) =>
        BuildFromLines(File.ReadAllLines(path), equippedOnly);

    /// <summary>One-shot parse of raw log LINES into a LiveBuild — the file-free core of
    /// <see cref="BuildFromFile"/>, so session slices from archived logs replay identically.</summary>
    public static LiveBuild BuildFromLines(string[] allLines, bool equippedOnly = true)
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
            if (!item.Equipped)
            {
                // Self-heal (mirrors Poll's Commit): a non-equipped sighting evicts an earlier
                // wrongly-equipped copy of the same item instead of leaving it ratcheted in.
                var key = (item.Slot ?? "?") + ":" + item.RawName;
                ordered.RemoveAll(x => x.Equipped && ((x.Slot ?? "?") + ":" + x.RawName) == key);
                if (equippedOnly) continue;
            }
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
