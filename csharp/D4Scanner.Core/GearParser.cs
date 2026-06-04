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

    public static string Clean(string s)
    {
        s = WebUtility.HtmlDecode(s ?? "");
        s = s.Replace('’', '\'').Replace('‘', '\'').Replace('“', '"').Replace('”', '"');
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
            var af = ParseAffix(ln);
            if (af != null) { item.Affixes.Add(af); continue; }
            if (ln.Any(char.IsLower) && ln.Length > 8) item.PowerText.Add(ln);
        }
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

    void Start(string nc) { _name = nc; _body = new(); _blockEquip = _equip; _equip = false; }

    /// <summary>Feed one raw log line; returns a completed Item when a tooltip block ends (else null).</summary>
    public Item? Feed(string raw)
    {
        var ln = Clean(raw);
        if (ln.Length == 0) return null;
        if (ln.Equals("EQUIPPED", StringComparison.OrdinalIgnoreCase)) { _equip = true; return null; }
        var low = ln.ToLowerInvariant();
        var nc = NameCandidate(ln);
        if (_name == null) { if (nc != null) Start(nc); return null; }
        if (EndMarkers.Any(low.Contains))
        {
            var item = ParseBlock(_name, _body);
            item.Equipped = _blockEquip;
            _name = null; _body = new(); _blockEquip = false;
            return LooksLikeItem(item) ? item : null;   // drop menu/map noise
        }
        if (nc != null) { Start(nc); return null; }
        _body.Add(ln);
        if (_body.Count > 60) { _name = null; _body = new(); }   // runaway guard
        return null;
    }
}
