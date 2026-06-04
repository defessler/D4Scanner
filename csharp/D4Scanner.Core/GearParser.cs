using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace D4Scanner.Core;

/// <summary>
/// Port of parser/d4_gear_capture.py — turns D4 screen-reader (TTS) log lines into
/// structured equipped-gear items. Stateful: feed lines via <see cref="Feed"/>.
/// </summary>
public class GearParser
{
    static readonly string[] EndMarkers = { "mouse button", "action button" };

    // longest/most-specific first so "Two-Handed Sword" wins over "Sword"
    static readonly (string key, string slot)[] TypeSlot =
    {
        ("Chest Armor", "chest"),
        ("Two-Handed Sword", "weapon"), ("Two-Handed Mace", "weapon"), ("Two-Handed Axe", "weapon"),
        ("Helm", "helm"), ("Gloves", "gloves"), ("Pants", "pants"), ("Boots", "boots"),
        ("Amulet", "amulet"), ("Ring", "ring"),
        ("Sword", "weapon"), ("Mace", "weapon"), ("Axe", "weapon"), ("Dagger", "weapon"),
        ("Bow", "weapon"), ("Crossbow", "weapon"), ("Wand", "weapon"), ("Staff", "weapon"),
        ("Polearm", "weapon"), ("Scythe", "weapon"), ("Glaive", "weapon"),
        ("Quarterstaff", "weapon"), ("Spear", "weapon"),
        ("Focus", "offhand"), ("Shield", "offhand"), ("Totem", "offhand"),
    };
    static readonly string[] Rarities =
        { "Mythic Unique", "Mythic", "Unique", "Legendary", "Rare", "Magic", "Common" };

    static readonly Regex ReItemPower = new(@"([\d,]+)\s+Item Power", RegexOptions.IgnoreCase);
    static readonly Regex ReDps = new(@"([\d,]+(?:\.\d+)?)\s+Damage Per Second", RegexOptions.IgnoreCase);
    static readonly Regex ReMasterwork = new(@"Masterwork[:\s]+(\d+)\s*/\s*(\d+)", RegexOptions.IgnoreCase);
    static readonly Regex ReTemper = new(@"Tempers?[:\s]+(\d+)\s*/\s*(\d+)", RegexOptions.IgnoreCase);
    static readonly Regex ReReqLevel = new(@"Requires Level\s+(\d+)", RegexOptions.IgnoreCase);
    static readonly Regex ReBracket = new(@"\[\s*([\d,.]+)\s*%?\s*(?:-\s*([\d,.]+)\s*%?\s*)?\]");
    static readonly Regex ReAffix = new(@"^\s*([+x]?)\s*([\d,.]+)\s*(%?)\s+(.+?)\s*$");
    static readonly Regex ReNameMarker = new(@"^\s*(EQUIPPED|\[FAVORITED ITEM\]\.?|\[.*?\]\.?)\s*", RegexOptions.IgnoreCase);
    static readonly Regex ReImprinted = new(@"^Imprinted:\s*(.+)", RegexOptions.IgnoreCase);
    // Runeword notation from sockets: "NeoVex (200/100) - Graceful Heart of the Oak"
    // Group 1 = rune-pair code (e.g. "NeoVex"), group 2 = runeword name after the dash
    static readonly Regex ReRuneword = new(@"^([A-Z][a-zA-Z]{1,8})\s*\(\d+/\d+\)\s*-\s*(.+)", RegexOptions.None);

    public static string Clean(string s)
    {
        s = WebUtility.HtmlDecode(s ?? "");
        // Strip optional ISO timestamp prefix added by the enhanced DLL: [2026-06-04T00:30:15Z]
        // Format is exactly 22 chars: [YYYY-MM-DDTHH:MM:SSZ]
        if (s.Length > 22 && s[0] == '[' && s[21] == ']')
            s = s.Substring(22);
        s = s.Replace((char)0x2018, (char)0x27).Replace((char)0x2019, (char)0x27).Replace((char)0x201C, (char)0x22).Replace((char)0x201D, (char)0x22);
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s;
    }

    static string StripNameMarkers(string s)
    {
        string prev;
        do { prev = s; s = ReNameMarker.Replace(s, "").Trim(' ', '.'); } while (prev != s);
        return s;
    }

    /// <summary>Returns the cleaned ALL-CAPS item name if this line starts an item, else null.</summary>
    static string? NameCandidate(string s)
    {
        s = StripNameMarkers(s);
        var letters = s.Where(char.IsLetter).ToList();
        if (letters.Count < 2 || s.Length > 64) return null;
        if (!letters.All(char.IsUpper)) return null;
        return s;
    }

    static string DisplayName(string raw) =>
        Regex.Replace(CultureInfo.CurrentCulture.TextInfo.ToTitleCase(raw.ToLowerInvariant()),
            @"'(\w)", m => "'" + m.Groups[1].Value.ToLowerInvariant());

    static double? ToNum(string? x)
    {
        if (string.IsNullOrEmpty(x)) return null;
        x = x.Replace(",", "");
        return double.TryParse(x, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : (double?)null;
    }

    static bool DetectRarityType(string ln, Item item)
    {
        (string key, string slot)? hit = null;
        foreach (var ts in TypeSlot)
            if (Regex.IsMatch(ln, @"\b" + Regex.Escape(ts.key) + @"\b", RegexOptions.IgnoreCase)) { hit = ts; break; }
        if (hit == null) return false;
        item.ItemType = hit.Value.key; item.Slot = hit.Value.slot;
        foreach (var r in Rarities)
            if (Regex.IsMatch(ln, @"\b" + Regex.Escape(r) + @"\b", RegexOptions.IgnoreCase))
            {
                item.Rarity = r;
                if (r.StartsWith("Mythic", StringComparison.OrdinalIgnoreCase)) { item.IsMythic = true; item.IsUnique = true; }
                else if (r.Equals("Unique", StringComparison.OrdinalIgnoreCase)) item.IsUnique = true;
                break;
            }
        if (Regex.IsMatch(ln, @"\bAncestral\b", RegexOptions.IgnoreCase)) item.IsAncestral = true;
        return true;
    }

    static Affix? ParseAffix(string ln)
    {
        double? vmin = null, vmax = null;
        string core = ln;
        var rng = ReBracket.Match(ln);
        if (rng.Success)
        {
            vmin = ToNum(rng.Groups[1].Value);
            vmax = rng.Groups[2].Success && rng.Groups[2].Value != "" ? ToNum(rng.Groups[2].Value) : null;
            core = ln[..rng.Index].Trim();
        }
        var m = ReAffix.Match(core);
        if (!m.Success) return null;
        string sign = m.Groups[1].Value, num = m.Groups[2].Value, pct = m.Groups[3].Value, text = m.Groups[4].Value.Trim();
        var value = ToNum(num);
        if (value == null || text.Length == 0 || text.Equals("Item Power", StringComparison.OrdinalIgnoreCase)) return null;
        return new Affix
        {
            Text = text, Value = value, Min = vmin, Max = vmax,
            IsPercent = pct == "%", IsMultiplier = sign == "x", IsGreater = !rng.Success,
        };
    }

    static Item ParseBlock(string name, List<string> body)
    {
        var item = new Item { Name = DisplayName(name), RawName = name };
        bool afterPropertiesLost = false;   // skip comparison-diff stats after this marker
        foreach (var ln in body)
        {
            // "Properties lost when equipped:" marks the start of comparison diff stats (e.g. "+925 Armor"
            // showing what you'd lose from the current item). Skip everything after this line so those
            // fake stats don't get parsed as the NEW item's affixes.
            if (ln.Contains("Properties lost when equipped", StringComparison.OrdinalIgnoreCase)) { afterPropertiesLost = true; continue; }
            if (afterPropertiesLost) continue;
            var mp = ReItemPower.Match(ln);
            if (mp.Success && item.ItemPower == null && !ln.ToLowerInvariant().Contains("per second"))
            { item.ItemPower = (int?)ToNum(mp.Groups[1].Value); continue; }
            var md = ReDps.Match(ln);
            if (md.Success && item.Dps == null) { item.Dps = ToNum(md.Groups[1].Value); continue; }
            var mm = ReMasterwork.Match(ln);
            if (mm.Success) { item.MasterworkRank = int.Parse(mm.Groups[1].Value); item.MasterworkMax = int.Parse(mm.Groups[2].Value); continue; }
            var mt = ReTemper.Match(ln);
            if (mt.Success) { item.TemperUsed = int.Parse(mt.Groups[1].Value); item.TemperMax = int.Parse(mt.Groups[2].Value); continue; }
            var mr = ReReqLevel.Match(ln);
            if (mr.Success) { item.RequiresLevel = int.Parse(mr.Groups[1].Value); continue; }
            if (item.Rarity == null && DetectRarityType(ln, item)) continue;
            var mi = ReImprinted.Match(ln);
            if (mi.Success) { item.Aspect = mi.Groups[1].Value.Trim(); item.PowerText.Add(ln); continue; }
            // Runeword notation in PowerText: "NeoVex (200/100) - Graceful Heart of the Oak"
            var rw = ReRuneword.Match(ln);
            if (rw.Success)
            {
                // Split the pair code into individual rune names by camel-case boundary
                var pair = rw.Groups[1].Value;
                // Simple split: find the first upper-case letter after position 0 that starts the second rune
                for (int ri = 1; ri < pair.Length; ri++)
                    if (char.IsUpper(pair[ri])) { item.SocketedRunes.Add(pair[..ri]); item.SocketedRunes.Add(pair[ri..]); break; }
                if (item.SocketedRunes.Count == 0) item.SocketedRunes.Add(pair);
                item.RunewordName = rw.Groups[2].Value.Trim();
                item.PowerText.Add(ln); continue;
            }
            var af = ParseAffix(ln);
            if (af != null) { item.Affixes.Add(af); continue; }
            if (ln.Any(char.IsLower) && ln.Length > 8) item.PowerText.Add(ln);
        }
        item.IsComparison = afterPropertiesLost;   // bag/stash comparison — never worn
        return item;
    }

    public static bool LooksLikeItem(Item? it)
    {
        if (it == null) return false;
        if (it.ItemPower != null) return true;
        return it.Rarity != null && it.Affixes.Count > 0;
    }

    // ---- stateful segmenter ----
    string? _name;
    List<string> _body = new();
    bool _equip, _blockEquip;
    bool _seenSlotHeader, _blockFromCharPanel;
    string? _currentSlotHeader;   // most recent character-panel slot header
    int _blockSlotPosition;       // 1-based position within a multi-slot category (rings, weapons)

    // D4 character-panel slot headers — voiced when the player opens the character sheet.
    // If one of these immediately precedes an EQUIPPED block it confirms the item is definitively worn.
    static readonly HashSet<string> SlotHeaders = new(StringComparer.OrdinalIgnoreCase)
        { "Head", "Torso", "Hands", "Legs", "Feet", "Ring", "Neck",
          "Main Hand", "Off-Hand", "Ranged", "Ranged Weapon" };

    // Position counters: D4 repeats the same slot header for each position (e.g. "Ring" twice).
    // Tracking counts let us distinguish Ring #1 from Ring #2, etc.
    readonly Dictionary<string, int> _slotPositionCounts = new(StringComparer.OrdinalIgnoreCase);

    void Start(string nc)
    {
        _name = nc; _body = new();
        _blockEquip = _equip;
        _blockFromCharPanel = _seenSlotHeader;
        _blockSlotPosition = _currentSlotHeader != null
            ? _slotPositionCounts.GetValueOrDefault(_currentSlotHeader, 0)
            : 0;
        _equip = false; _seenSlotHeader = false;
        // Don't clear _currentSlotHeader or position counts — they persist across items in the same panel view
    }

    /// <summary>Feed one raw log line; returns a completed Item when a tooltip block ends (else null).</summary>
    public Item? Feed(string raw)
    {
        var ln = Clean(raw);
        if (ln.Length == 0) return null;
        // Slot headers confirm character panel; track position for multi-slot categories (Ring, weapon)
        if (SlotHeaders.Contains(ln))
        {
            // Reset position counter if this is a different slot type (e.g. moved from Ring to Feet)
            if (!string.Equals(ln, _currentSlotHeader, StringComparison.OrdinalIgnoreCase))
            {
                // Only reset the counter for the NEW slot; leave other slots intact
                // (user can open char panel showing different slots in sequence)
            }
            _currentSlotHeader = ln;
            _slotPositionCounts[ln] = _slotPositionCounts.GetValueOrDefault(ln, 0) + 1;
            _seenSlotHeader = true;
            return null;
        }
        // Non-slot-header lines outside a block reset the positional counters for ALL slots
        // so a fresh panel view starts fresh (rough heuristic: reset on navigation noise)
        if (!_seenSlotHeader && _name == null && SlotHeaders.Count > 0 && ln.Length > 2
            && !ln.Equals("EQUIPPED", StringComparison.OrdinalIgnoreCase))
        {
            // Reset only if we see a clear "not a slot context" signal (e.g. player name or zone)
            // Conservative: only reset on "=== " separator lines (session restart)
            if (ln.StartsWith("=== d4scanner", StringComparison.OrdinalIgnoreCase))
                _slotPositionCounts.Clear();
        }
        if (ln.Equals("EQUIPPED", StringComparison.OrdinalIgnoreCase)) { _equip = true; return null; }
        var low = ln.ToLowerInvariant();
        var nc = NameCandidate(ln);
        if (_name == null) { if (nc != null) Start(nc); return null; }
        if (EndMarkers.Any(low.Contains))
        {
            var item = ParseBlock(_name, _body);
            item.Equipped = _blockEquip;
            item.FromCharPanel = _blockFromCharPanel;
            item.SlotPosition = _blockSlotPosition;   // 1-based: ring:1 / ring:2, weapon:1 / weapon:2 / weapon:3
            _name = null; _body = new(); _blockEquip = false; _blockFromCharPanel = false; _blockSlotPosition = 0;
            return LooksLikeItem(item) ? item : null;   // drop menu/map noise
        }
        if (nc != null) { Start(nc); return null; }
        _body.Add(ln);
        if (_body.Count > 60) { _name = null; _body = new(); }   // runaway guard
        return null;
    }
}
