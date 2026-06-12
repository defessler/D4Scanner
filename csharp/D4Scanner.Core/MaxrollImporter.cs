using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace D4Scanner.Core;

/// <summary>
/// Port of parser/d4_maxroll_import.py — fetches a Maxroll build-guide/planner URL and builds a
/// <see cref="TargetBuild"/>. Maxroll embeds the build in the page as window.__remixContext ->
/// plannerProfile -> data.profiles[]; IDs resolve via Maxroll's data.min.json + the D4Companion
/// affix map (both cached under %LOCALAPPDATA%\d4scanner\cache).
/// </summary>
public static class MaxrollImporter
{
    static readonly HttpClient Http = Create();
    static HttpClient Create()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        return h;
    }

    const string DataUrl = "https://assets-ng.maxroll.gg/d4-tools/game/data.min.json";
    const string DcAffixUrl = "https://raw.githubusercontent.com/josdemmers/Diablo4Companion/master/D4Companion/Data/Affixes.enUS.json";

    static string CacheDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "d4scanner", "cache");

    static readonly (string key, string slot)[] TypeSlot =
    {
        ("ChestArmor","chest"),("Chest","chest"),("Helm","helm"),("Gloves","gloves"),
        ("Legs","pants"),("Pants","pants"),("Boots","boots"),("Amulet","amulet"),("Ring","ring"),
        ("Focus","offhand"),("Shield","offhand"),("Totem","offhand"),("OffHand","offhand"),
        ("Sword","weapon"),("Mace","weapon"),("Axe","weapon"),("Dagger","weapon"),("Bow","weapon"),
        ("Crossbow","weapon"),("Wand","weapon"),("Staff","weapon"),("Polearm","weapon"),("Scythe","weapon"),
        ("Glaive","weapon"),("Quarterstaff","weapon"),("Spear","weapon"),("Weapon","weapon"),
    };

    /// <summary>Reconstruct the Maxroll URL from a slug / planner-code / full URL (mirrors ImportAsync's rule):
    /// a hyphenated slug → /d4/build-guides/&lt;slug&gt;, a short token → /d4/planner/&lt;code&gt;. Null if empty.</summary>
    public static string? BuildUrl(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var s = input.Trim();
        if (s.Contains("://")) return s;
        s = s.Trim('/');
        if (s.Length == 0) return null;
        return s.Contains('-')
            ? "https://maxroll.gg/d4/build-guides/" + s
            : "https://maxroll.gg/d4/planner/" + s;
    }

    public static async Task<TargetBuild> ImportAsync(string url, string? profileName = null,
        Action<string>? log = null, CancellationToken ct = default)
    {
        log ??= _ => { };
        url = (url ?? "").Trim();
        if (url.Contains('<') || url.Contains('>'))
            throw new InvalidOperationException("That looks like a placeholder, not a real Maxroll URL.");
        if (url.Length == 0) throw new InvalidOperationException("Enter a Maxroll build URL.");
        if (!url.Contains("://"))
        {
            // bare input: a build-guide slug is wordy with hyphens (dance-of-knives-rogue-guide);
            // a planner code is a short single token (e.g. xQ2p0aBc).
            var slug = url.Trim().Trim('/');
            url = slug.Contains('-')
                ? "https://maxroll.gg/d4/build-guides/" + slug
                : "https://maxroll.gg/d4/planner/" + slug;
        }

        log($"fetching {url} …");
        string html;
        try { html = await Http.GetStringAsync(url, ct); }
        catch (HttpRequestException e)
        {
            throw new InvalidOperationException(
                $"Couldn't fetch the build ({(e.StatusCode != null ? (int)e.StatusCode : 0)}). " +
                "Make sure it's a real Maxroll build URL you can open in a browser.", e);
        }

        using var ctx = ExtractRemixContext(html)
            ?? throw new InvalidOperationException("Couldn't find planner data on that page. Use a Maxroll build-guide or planner URL.");
        var pp = FindPlannerProfile(ctx.RootElement)
            ?? throw new InvalidOperationException("No build (plannerProfile) found on that page.");

        var (data, dataDoc) = GetData(pp);
        log("loading game data …");
        using var dm = await LoadJsonCached(DataUrl, "maxroll_data.min.json", ct);
        using var dc = await LoadJsonCached(DcAffixUrl, "d4companion_affixes.json", ct);
        var maps = BuildMaps(dm.RootElement, dc.RootElement);

        try
        {
            var profiles = data.GetProperty("profiles");
            var prof = ChooseProfile(profiles, profileName);
            var target = BuildTarget(pp, data, prof, dm.RootElement, maps);
            target.Profiles = profiles.EnumerateArray()
                .Select(p => p.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "")
                .Where(s => s.Length > 0).ToList();
            target.Profile = prof.TryGetProperty("name", out var pnm) ? pnm.GetString() : null;
            log($"imported: {target.Name} — {target.Gear.Count} gear, {target.Uniques.Count} uniques, " +
                $"{target.Skills.Count} skills, {target.Paragon?.Boards.Count ?? 0} boards");
            return target;
        }
        finally { dataDoc?.Dispose(); }
    }

    // ---- page extraction ----

    static JsonDocument? ExtractRemixContext(string html, string varName = "window.__remixContext")
    {
        int i = html.IndexOf(varName, StringComparison.Ordinal);
        if (i < 0) return null;
        int start = html.IndexOf('{', i);
        if (start < 0) return null;
        int depth = 0; bool instr = false, esc = false;
        for (int j = start; j < html.Length; j++)
        {
            char c = html[j];
            if (instr)
            {
                if (esc) esc = false;
                else if (c == '\\') esc = true;
                else if (c == '"') instr = false;
            }
            else
            {
                if (c == '"') instr = true;
                else if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) return JsonDocument.Parse(html.Substring(start, j - start + 1)); }
            }
        }
        return null;
    }

    static JsonElement? FindPlannerProfile(JsonElement root)
    {
        JsonElement? found = null;
        void Walk(JsonElement e)
        {
            if (found != null) return;
            if (e.ValueKind == JsonValueKind.Object)
            {
                if (e.TryGetProperty("plannerProfile", out var pp) && pp.ValueKind == JsonValueKind.Object) { found = pp; return; }
                foreach (var p in e.EnumerateObject()) { Walk(p.Value); if (found != null) return; }
            }
            else if (e.ValueKind == JsonValueKind.Array)
                foreach (var it in e.EnumerateArray()) { Walk(it); if (found != null) return; }
        }
        Walk(root);
        return found;
    }

    static (JsonElement data, JsonDocument? owned) GetData(JsonElement pp)
    {
        var d = pp.GetProperty("data");
        if (d.ValueKind == JsonValueKind.String)
        {
            var doc = JsonDocument.Parse(d.GetString() ?? "{}");
            return (doc.RootElement, doc);
        }
        return (d, null);
    }

    static JsonElement ChooseProfile(JsonElement profiles, string? name)
    {
        var arr = profiles.EnumerateArray().ToArray();
        if (arr.Length == 0) throw new InvalidOperationException("This Maxroll build has no profiles.");
        if (!string.IsNullOrEmpty(name))
            foreach (var p in arr)
                if (p.TryGetProperty("name", out var nm) && (nm.GetString() ?? "").Contains(name, StringComparison.OrdinalIgnoreCase))
                    return p;
        return arr[^1];  // default: last profile (usually endgame)
    }

    // ---- id -> name maps ----

    sealed record Maps(Dictionary<long, string> AffixKeyById, Dictionary<string, string> Token,
                       Dictionary<long, string> AspectNameById);

    static Maps BuildMaps(JsonElement dm, JsonElement dc)
    {
        var byId = new Dictionary<long, string>();
        var aspectById = new Dictionary<long, string>();
        if (dm.TryGetProperty("affixes", out var affixes))
            foreach (var p in affixes.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number)
                {
                    byId[id.GetInt64()] = p.Name;
                    // Legendary-aspect affixes carry the in-game item NAME modifier ("of Mending Obscurity" /
                    // "Edgemaster's") in suffix/prefix — the only human-readable aspect name in the data.
                    // Token descriptions never cover legendary_* keys, so without this the aspect's display
                    // name degraded to the raw key ("legendary rogue 011") and could never match owned items.
                    string? mod = p.Value.TryGetProperty("suffix", out var sx) && sx.ValueKind == JsonValueKind.String ? sx.GetString()
                                : p.Value.TryGetProperty("prefix", out var px) && px.ValueKind == JsonValueKind.String ? px.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(mod) && p.Name.Contains("legendary", StringComparison.OrdinalIgnoreCase))
                        aspectById[id.GetInt64()] = AspectDisplayName(mod!);
                }
        var tok = new Dictionary<string, string>();
        if (dc.ValueKind == JsonValueKind.Array)
            foreach (var el in dc.EnumerateArray())
            {
                string? desc = el.TryGetProperty("Description", out var de) ? de.GetString() : null;
                string idn = el.TryGetProperty("IdName", out var idne) ? (idne.GetString() ?? "") : "";
                foreach (var t in idn.Split(';'))
                {
                    var tt = t.Trim();
                    if (tt.Length > 0 && !tok.ContainsKey(tt)) tok[tt] = desc ?? "";
                }
            }
        return new Maps(byId, tok, aspectById);
    }

    static string? AffixName(long nid, Maps m)
    {
        if (!m.AffixKeyById.TryGetValue(nid, out var key)) return null;
        string? raw = m.Token.TryGetValue(key, out var d) ? d : null;
        return CleanAffix(raw) ?? CleanAffix(Humanize(key));
    }

    /// <summary>Canonical display name for an aspect from its item-name modifier:
    /// "of Mending Obscurity" → "Aspect of Mending Obscurity"; "Edgemaster's" → "Edgemaster's Aspect".</summary>
    public static string AspectDisplayName(string nameModifier)
    {
        var s = Regex.Replace(nameModifier, @"\s+", " ").Trim();
        if (s.Contains("aspect", StringComparison.OrdinalIgnoreCase)) return s;
        return s.StartsWith("of ", StringComparison.OrdinalIgnoreCase) ? "Aspect " + s : s + " Aspect";
    }

    static string? AspectName(long nid, Maps m) =>
        m.AspectNameById.TryGetValue(nid, out var n) ? n : AffixName(nid, m);

    /// <summary>True when a resolved affix name is really an unmapped item-id key (a unique's inherent power
    /// fell through to Humanize(itemId), e.g. "1HDagger Unique Rogue 003 2") rather than a real rollable affix.
    /// A genuine affix never carries a rarity word alongside a number.</summary>
    static bool LooksLikeItemId(string name) =>
        Regex.IsMatch(name, @"\b(Unique|Legendary|Mythic|Set)\b", RegexOptions.IgnoreCase)
        && Regex.IsMatch(name, @"\d");

    static string? CleanAffix(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var s = Regex.Replace(name, @"\{[^}]*\}", "");
        s = Regex.Replace(s, @"\[[^\]]*\]", "");
        s = Regex.Replace(s, @"[#%+]|(?<![A-Za-z])x(?![A-Za-z])", " ");
        s = Regex.Replace(s, @"\s+", " ").Trim(' ', '.', ',');
        s = Regex.Replace(s, @"\bMax\b", "Maximum");
        return s.Length > 0 ? s : null;
    }

    static string Humanize(string key)
    {
        var s = Regex.Replace(key, "_+", " ");
        s = Regex.Replace(s, "(?<=[a-z])(?=[A-Z])", " ");
        s = Regex.Replace(s, @"\b(Generic|Tier\d+|Greater|Single|Core ?Stat|Resource|S\d+)\b", " ");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    static string? SlotForType(string? t)
    {
        if (string.IsNullOrEmpty(t)) return null;
        foreach (var ts in TypeSlot)
            if (t.Contains(ts.key, StringComparison.OrdinalIgnoreCase)) return ts.slot;
        return null;
    }

    // resolve a socketed gem/rune id ("Gem_Diamond_03", "Rune_Effect_MobilityBuff") to a readable name
    static string SocketName(string id, JsonElement dm)
    {
        var n = Lookup(dm, "items", id, "");
        if (n.Length > 0) return (id.StartsWith("Rune", StringComparison.OrdinalIgnoreCase) ? "Rune: " : "Gem: ") + n;
        var s = Regex.Replace(id, @"^(Gem|Rune)_(Condition|Effect)?_?", "");
        s = Regex.Replace(s, @"_\d+$", "");
        s = Regex.Replace(s, @"_", " ");
        s = Regex.Replace(s, @"(?<=[a-z])(?=[A-Z])", " ").Trim();
        return (id.StartsWith("Rune", StringComparison.OrdinalIgnoreCase) ? "Rune: " : "Gem: ") + s;
    }

    static string Lookup(JsonElement dm, string map, string id, string fallback)
    {
        if (dm.TryGetProperty(map, out var m) && m.TryGetProperty(id, out var e) && e.TryGetProperty("name", out var n))
            return n.GetString() ?? fallback;
        return fallback;
    }

    static long? Nid(JsonElement af)
    {
        if (af.TryGetProperty("nid", out var n) && n.ValueKind == JsonValueKind.Number) return n.GetInt64();
        if (af.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.Number) return i.GetInt64();
        return null;
    }

    // ---- build the target ----

    static TargetBuild BuildTarget(JsonElement pp, JsonElement data, JsonElement prof, JsonElement dm, Maps m)
    {
        var itemsDb = data.GetProperty("items");

        var skillBar = prof.TryGetProperty("skillBar", out var sb) && sb.ValueKind == JsonValueKind.Array
            ? sb.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0).ToList()
            : new List<string>();
        string? klass = skillBar.Count > 0 ? skillBar[0].Split('_')[0] : null;

        var gear = new List<TargetGear>();
        var uniques = new List<TargetUnique>();
        var aspects = new List<string>();
        int ringN = 0;

        // parse an item's wanted affixes (explicits + tempered) — shared by gear and unique items
        List<TargetAffix> ParseItemAffixes(JsonElement itemEl)
        {
            var affs = new List<TargetAffix>();
            var seen = new HashSet<string>();
            foreach (var (coll, tempered) in new[] { ("explicits", false), ("tempered", true) })
                if (itemEl.TryGetProperty(coll, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    foreach (var af in arr.EnumerateArray())
                    {
                        var nid = Nid(af);
                        if (nid == null) continue;
                        var an = AffixName(nid.Value, m);
                        // a unique's inherent power sits in `explicits` but its nid has no affix token, so AffixName
                        // falls back to the humanized item-id key ("1HDagger Unique Rogue 003 2"). Skip those —
                        // they're the unique's fixed power, not a rollable secondary affix the player chases.
                        if (an != null && !LooksLikeItemId(an) && seen.Add(an))
                            affs.Add(new TargetAffix { Name = an, Tempered = tempered });
                    }
            return affs;
        }

        if (prof.TryGetProperty("items", out var pitems) && pitems.ValueKind == JsonValueKind.Object)
            foreach (var slotProp in pitems.EnumerateObject())
            {
                var iv = slotProp.Value;
                string instStr = iv.ValueKind == JsonValueKind.Number ? iv.GetInt64().ToString() : (iv.GetString() ?? "");
                if (!itemsDb.TryGetProperty(instStr, out var item)) continue;

                string itemId = item.TryGetProperty("id", out var iid) ? (iid.GetString() ?? "") : "";
                bool hasDef = dm.TryGetProperty("items", out var dmItems) && dmItems.TryGetProperty(itemId, out var idef);
                idef = hasDef ? dm.GetProperty("items").GetProperty(itemId) : default;
                string name = hasDef && idef.TryGetProperty("name", out var nm) ? (nm.GetString() ?? itemId) : itemId;
                string? type = hasDef && idef.TryGetProperty("type", out var ty) ? ty.GetString() : null;
                string slot = SlotForType(type) ?? SlotForType(itemId) ?? "unknown";
                int magicType = hasDef && idef.TryGetProperty("magicType", out var mt) && mt.ValueKind == JsonValueKind.Number ? mt.GetInt32() : -1;
                long? image = hasDef && idef.TryGetProperty("image", out var im) && im.ValueKind == JsonValueKind.Number ? im.GetInt64() : null;
                bool isUnique = itemId.Contains("Unique", StringComparison.OrdinalIgnoreCase) || magicType is 4 or 5 or 6;
                bool isMythic = itemId.Contains("Mythic", StringComparison.OrdinalIgnoreCase) || itemId.ToUpperInvariant().Contains("UBER");

                if (isUnique) { uniques.Add(new TargetUnique { Name = name, Slot = slot, Mythic = isMythic, Image = image, ItemId = itemId, Affixes = ParseItemAffixes(item) }); continue; }

                // the planner stores the imprinted aspect under "aspects" (an ARRAY) in the current format;
                // older payloads used a single "aspect" object — accept both (the object-only read silently
                // imported ZERO aspects from every modern build)
                string? aspectName = null;
                var aspEls = new List<JsonElement>();
                if (item.TryGetProperty("aspect", out var aspObj) && aspObj.ValueKind == JsonValueKind.Object)
                    aspEls.Add(aspObj);
                else if (item.TryGetProperty("aspects", out var aspArr) && aspArr.ValueKind == JsonValueKind.Array)
                    aspEls.AddRange(aspArr.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.Object));
                foreach (var asp in aspEls)
                {
                    var anid = Nid(asp);
                    if (anid == null) continue;
                    var an = AspectName(anid.Value, m);
                    if (an != null) { aspects.Add(an); aspectName ??= an; }
                }

                var affixes = ParseItemAffixes(item);

                var sockets = new List<string>();
                if (item.TryGetProperty("sockets", out var sk) && sk.ValueKind == JsonValueKind.Array)
                    foreach (var so in sk.EnumerateArray())
                        if (so.ValueKind == JsonValueKind.String)
                        { var soid = so.GetString(); if (!string.IsNullOrEmpty(soid)) sockets.Add(SocketName(soid!, dm)); }

                if ((affixes.Count > 0 || aspectName != null || sockets.Count > 0) && slot != "unknown")
                {
                    string sid = slot, label = char.ToUpper(slot[0]) + slot[1..];
                    if (slot == "ring") { ringN++; sid = "ring" + ringN; label = "Ring #" + ringN; }
                    gear.Add(new TargetGear { Slot = sid, Label = label, Affixes = affixes, Aspect = aspectName, Sockets = sockets, Image = image, ItemId = itemId });
                }
            }

        var skills = skillBar.Select(k => new TargetSkill { Name = Lookup(dm, "skills", k, Humanize(k)) }).ToList();

        var boards = new List<string>();
        var glyphs = new List<TargetGlyph>();
        if (prof.TryGetProperty("paragon", out var para) && para.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
        {
            var stepArr = steps.EnumerateArray().ToArray();
            if (stepArr.Length > 0 && stepArr[^1].TryGetProperty("data", out var bd) && bd.ValueKind == JsonValueKind.Array)
                foreach (var b in bd.EnumerateArray())
                {
                    string bid = b.TryGetProperty("id", out var bi) ? (bi.GetString() ?? "") : "";
                    boards.Add(Lookup(dm, "paragonBoards", bid, Humanize(bid)));
                    if (b.TryGetProperty("glyph", out var gl) && gl.ValueKind == JsonValueKind.String)
                    {
                        int? lvl = b.TryGetProperty("glyphLevel", out var gv) && gv.ValueKind == JsonValueKind.Number ? gv.GetInt32() : (int?)null;
                        if (lvl != null && !(lvl > 0 && lvl <= 100)) lvl = null;  // Maxroll's internal sentinel
                        glyphs.Add(new TargetGlyph { Name = Lookup(dm, "paragonGlyphs", gl.GetString() ?? "", Humanize(gl.GetString() ?? "")), Level = lvl });
                    }
                }
        }

        string? source = pp.TryGetProperty("metadata", out var md) && md.TryGetProperty("maxrollId", out var mr)
            ? mr.GetString() : "maxroll";

        // mercenary (id + support reinforcement); talismans are not present in Maxroll planner data
        TargetMercenary? merc = null;
        if (prof.TryGetProperty("mercenary", out var mc) && mc.ValueKind == JsonValueKind.Object)
        {
            string? Resolve(string key) => mc.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
                ? Lookup(dm, "mercenaries", v.GetString() ?? "", Humanize(v.GetString() ?? "")) : null;
            var main = Resolve("id");
            var supp = Resolve("support");
            var sk = new List<string>();
            if (mc.TryGetProperty("supportSkills", out var ss) && ss.ValueKind == JsonValueKind.Array)
                foreach (var s in ss.EnumerateArray())
                    if (s.ValueKind == JsonValueKind.String) { var sn = s.GetString()!; sk.Add(Lookup(dm, "skills", sn, Humanize(sn))); }
            if (!string.IsNullOrEmpty(main) || !string.IsNullOrEmpty(supp)) merc = new TargetMercenary { Main = main, Support = supp, SupportSkills = sk };
        }

        return new TargetBuild
        {
            Name = pp.TryGetProperty("name", out var pn) ? (pn.GetString() ?? "Maxroll Build") : "Maxroll Build",
            Class = klass,
            Source = source,
            Gear = gear,
            Uniques = uniques,
            Aspects = aspects.Distinct().OrderBy(x => x).ToList(),
            Skills = skills,
            KeyPassives = new(),
            Paragon = new TargetParagon { Boards = boards, Glyphs = glyphs },
            Mercenary = merc,
        };
    }

    static async Task<JsonDocument> LoadJsonCached(string url, string fname, CancellationToken ct)
    {
        Directory.CreateDirectory(CacheDir);
        var path = Path.Combine(CacheDir, fname);
        if (!File.Exists(path))
        {
            var bytes = await Http.GetByteArrayAsync(url, ct);
            await File.WriteAllBytesAsync(path, bytes, ct);
        }
        return JsonDocument.Parse(await File.ReadAllBytesAsync(path, ct));
    }
}
