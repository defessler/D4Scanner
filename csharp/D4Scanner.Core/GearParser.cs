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
        // Season 8 (Vessel of Hatred) item types. Charms are detected by the ANCHORED ReCharmType
        // (see DetectRarityType) — NOT as unanchored TypeSlot keys — so Horadric Cube material/recipe
        // lines that merely CONTAIN "Set Charm" ("1x Set Charm", "Reroll Set Charm", "Transmutes a Set
        // Charm to a different Charm…") can't manufacture a phantom charm out of a crafting panel.
        ("Horadric Seal", "seal"),
        ("Rune of Invocation", "rune"), ("Rune of Ritual", "rune"),
    };
    static readonly string[] Rarities =
        { "Mythic Unique", "Mythic", "Unique", "Legendary", "Rare", "Magic", "Common" };

    static readonly Regex ReItemPower = new(@"([\d,]+)\s+Item Power", RegexOptions.IgnoreCase);
    static readonly Regex ReDps = new(@"([\d,]+(?:\.\d+)?)\s+Damage Per Second", RegexOptions.IgnoreCase);
    static readonly Regex ReMasterwork = new(@"Masterwork[:\s]+(\d+)\s*/\s*(\d+)", RegexOptions.IgnoreCase);
    // Masterwork Quality (S11 rework, capped /25): "50 (+50/25) Quality" or "50 +50/25 Quality" — score, optional bonus/max
    static readonly Regex ReQuality = new(@"^(\d+)\s*(?:\(\s*[+-]?\d+(?:/\d+)?\s*\)|[+-]?\d+(?:/\d+)?)?\s+Quality", RegexOptions.IgnoreCase);
    static readonly Regex ReTemper = new(@"Tempers?[:\s]+(\d+)\s*/\s*(\d+)", RegexOptions.IgnoreCase);
    static readonly Regex ReReqLevel = new(@"Requires Level\s+(\d+)", RegexOptions.IgnoreCase);
    // class restriction on the requires line — the class is glued to the preceding text by the reader:
    // "Requires Level 70. Account BoundRogue. Only. Vessel of Hatred Item"
    static readonly Regex ReClassLock = new(
        @"(Barbarian|Druid|Necromancer|Rogue|Sorcerer|Spiritborn|Paladin|Warlock)[.\s]*Only", RegexOptions.IgnoreCase);
    static readonly Regex ReBracket = new(@"\[\s*([\d,.]+)\s*%?\s*(?:-\s*([\d,.]+)\s*%?\s*)?\]");
    static readonly Regex ReAffix = new(@"^\s*([+x]?)\s*([\d,.]+)\s*(%?)\s+(.+?)\s*$");
    // S8 core-stat affixes voice the value twice: "+109 Dexterity +[83 - 99]". After the [range] is
    // stripped, a dangling '+' (and any stray trailing punctuation/space) is left on the affix text.
    // NOTE: applied in ParseAffix BEFORE the weapon-implicit/summary-total filters, which rely on the
    // sign+range guard — don't reorder the strip after them or a rolled summary affix could be dropped.
    static readonly Regex ReAffixTrailJunk = new(@"[+\s.,;:]+$");
    static readonly Regex ReNameMarker = new(@"^\s*(EQUIPPED|\[FAVORITED ITEM\]\.?|\[.*?\]\.?)\s*", RegexOptions.IgnoreCase);
    static readonly Regex ReImprinted = new(@"^Imprinted:\s*(.+)", RegexOptions.IgnoreCase);
    // Runeword notation from sockets: "NeoVex (200/100) - Graceful Heart of the Oak"
    // Group 1 = rune-pair code (e.g. "NeoVex"), group 2 = runeword name after the dash
    static readonly Regex ReInBags = new(@"^In bags:\s*(\d+)", RegexOptions.IgnoreCase);
    // Runeword notation from sockets: "NeoVex (200/100) - Graceful Heart of the Oak"
    // Group 1 = rune-pair code (e.g. "NeoVex"), group 2 = runeword name after the dash
    static readonly Regex ReRuneword = new(@"^([A-Z][a-zA-Z]{1,8})\s*\(\d+/\d+\)\s*-\s*(.+)", RegexOptions.None);
    // Weapon implicit stats (base damage / attack speed) — voiced like affixes but never rollable or
    // wanted. Dropped so a target "Damage" can't false-match a weapon's "Weapon Damage" implicit.
    static readonly Regex ReWeaponStat = new(@"^(Weapon Damage|Attacks? per Second|Damage per Hit)\b", RegexOptions.IgnoreCase);
    // Tooltip summary totals ("2,805 Armor", "157 All Resist"): the real rolled affix of the same name
    // carries a + sign and a [range]; the bare summary form (no sign, no range) is a derived total.
    static readonly Regex ReSummaryStat = new(@"^(Armor|All Resist(ance)?)$", RegexOptions.IgnoreCase);
    // Comparison-tooltip parenthetical: D4's "show comparison" overlay appends a delta like
    // "157 All Resist (-3.8% Toughness)" / "638 Armor (-4.2% Toughness)". This is NOT a real affix —
    // strip the trailing delta paren so the bare-summary filter (ReSummaryStat) can drop the stat,
    // and so a leaked Toughness pseudo-affix can't false-match a target's Armor/All-Res requirement.
    // Two forms: a numeric-delta-led paren ("(-3.8% Toughness)", "(-160)") or any letter-bearing
    // clarifier paren ("(0.8% at level 70)"). A value-only paren like "(+892)" has NO letter and no
    // leading sign+number+text shape we strip here only when it trails the whole core — and on a
    // bracketed affix that paren is already gone (core = text before the '['), so equipped rolls are safe.
    static readonly Regex ReCompareDelta = new(@"\s*\([+-]?[\d.,]+%?[^)]*\)\s*$");
    static readonly Regex ReTrailingClarifier = new(@"\s*\((?=[^)]*\p{L})[^)]*\)\s*$");
    // Skill-rank / dangling-connective noise: "+2 to Heartseeker" -> "Heartseeker"; "+3 Ranks to Core
    // Skill" -> "Core Skill". Strip a leading "to "/"Ranks to " so DiffEngine.PhraseMatch (substring) sees
    // the clean skill name — +Ranks is the game's #2 highest-leverage affix, it must match build targets.
    static readonly Regex ReLeadingConnective = new(@"^(?:ranks?\s+)?to\s+", RegexOptions.IgnoreCase);
    // Recover-dropped-affix fallback (conservative): only the clean "value-first" seal/charm power-name
    // shape is recovered, e.g. "+8%[x] [7-10]% Shadow Damage" -> {Shadow Damage, 8, x-mult}. The value MUST
    // be the leading token (after an optional "Name:." prefix); multi-clause powers / "Lucky Hit: Up to a…"
    // lines (value not first) route to PowerText instead, since their value can't be picked safely.
    static readonly Regex ReLeadValue = new(@"^([+x-]?)\s*([\d][\d,.]*)\s*(%?)\s*(\[x\]|\[\+\])?\s*", RegexOptions.IgnoreCase);
    static readonly Regex ReMarkerJunk = new(@"\[[x+]\]", RegexOptions.IgnoreCase);
    // A leading "Power Name:." or "Power Name:" prefix on a seal/charm affix line
    // ("Way of the Blurring Blade:. +22% [13-25]% Critical Strike Damage").
    static readonly Regex RePowerNamePrefix = new(@"^[^:.]{1,40}:\.?\s+");
    // Sockets: bare "Socket (2)" = TOTAL socket capacity (comparison/bag view); "Empty Socket" = one unfilled
    // socket (counted). A FILLED socket renders as the runeword line (ReRuneword) instead, never as "Socket (N)".
    static readonly Regex ReSocket = new(@"^Socket\s*\((\d+)\)\s*$", RegexOptions.IgnoreCase);
    static readonly Regex ReEmptySocket = new(@"^Empty Socket\s*$", RegexOptions.IgnoreCase);
    // Set Charm bonus header: "<SetName> (active/total). (T) Set:. <bonus...>". The FIRST (n/m) is the piece
    // count; the later (T) is a tier threshold. Member-name lines (e.g. "Phoba of Mastery") carry no (n/m) and
    // won't match, so this selects only the real header. Captures: 1=name, 2=active, 3=total.
    static readonly Regex ReSetName = new(@"^(.+?)\s*\((\d+)/(\d+)\)\.\s*\(\d+\)\s*Set:", RegexOptions.None);
    // Stateful tooltip lines — wear/economy/menu state that varies between captures of the SAME item
    // (durability drops in combat, sell value tracks gold, scroll hints depend on tooltip height).
    // Dropped before the PowerText catch-all so item identity is content-only.
    static readonly Regex ReStatefulInfo = new(@"^(Durability:|Sell Value:|Armory Loadout$|Mousewheel scroll|Scroll (Down|Up)$)", RegexOptions.IgnoreCase);
    // LoH Talisman CHARM type line: "<Rarity> Charm" / "Charm" / "Charm (Ancestral)" / "Ancestral Set Charm".
    // This is the SOLE charm detector (the "Set Charm"/"Unique Charm" TypeSlot keys were removed): being
    // ANCHORED (^…$) is what makes it precise. It matches a standalone charm-type designation but NOT a line
    // that merely contains those words — so the seal's "Unlocks N Charm Slots", all-caps charm NAMES
    // ("TRAPPER'S CHARM OF …"), and Horadric Cube material/recipe lines ("1x Set Charm", "Reroll Set Charm",
    // "Transmutes a Set Charm to a different Charm…") never manufacture a phantom charm. Rare/Magic/Legendary
    // charms — which have no literal TypeSlot key — are captured here too (verified on the real Talisman log).
    static readonly Regex ReCharmType = new(
        @"^(?:Ancestral\s+)?(?:Common|Magic|Rare|Legendary|Unique|Set|Mythic)?\s*Charm(?:\s*\(Ancestral\))?$", RegexOptions.IgnoreCase);

    public static string Clean(string s) => CleanWithTime(s, out _);

    /// <summary>Like <see cref="Clean"/> but also returns the UTC time parsed from the line's '[ISO]'
    /// prefix (the enhanced DLL stamps each line with the real hover time). <paramref name="logTime"/> is
    /// null when no parseable prefix is present. The returned string is byte-identical to <see cref="Clean"/>
    /// for every input, so existing callers are unaffected — only the out-time is new.</summary>
    public static string CleanWithTime(string s, out DateTimeOffset? logTime)
    {
        logTime = null;
        s = WebUtility.HtmlDecode(s ?? "");
        // Strip optional ISO timestamp prefix added by the enhanced DLL: [2026-06-04T00:30:15Z]
        // Format is exactly 22 chars: [YYYY-MM-DDTHH:MM:SSZ]
        if (s.Length > 22 && s[0] == '[' && s[21] == ']')
        {
            // Parse the 20-char inner ISO instant before stripping; on a fresh launch the whole
            // cumulative log replays, so this is the only way to know an item's TRUE age.
            if (DateTimeOffset.TryParse(s.AsSpan(1, 20), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t))
                logTime = t;
            s = s.Substring(22);
        }
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
        if (letters.Count < 2 || s.Length > 96) return null;   // affix-prefixed/suffixed names run long; the all-caps gate below still rejects non-names
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
        // Talisman charms are detected here (anchored) rather than as unanchored TypeSlot keys — see ReCharmType.
        // Keep the specific designation: "Set Charm"/"Unique Charm" drive downstream set/inventory logic (and the
        // parser tests), every other rarity collapses to plain "Charm" (as the prior fallback did).
        if (hit == null && ReCharmType.IsMatch(ln)) hit = (CanonCharmType(ln), "charm");
        if (hit == null) return false;
        item.ItemType = hit.Value.key; item.Slot = hit.Value.slot;
        foreach (var r in Rarities)
            if (Regex.IsMatch(ln, @"\b" + Regex.Escape(r) + @"\b", RegexOptions.IgnoreCase))
            {
                item.Rarity = r;
                if (r.StartsWith("Mythic", StringComparison.OrdinalIgnoreCase)) { item.IsMythic = true; item.IsUnique = true; item.IsAncestral = true; }   // Mythics are always Ancestral (doc §2), even when the tooltip doesn't voice the word
                else if (r.Equals("Unique", StringComparison.OrdinalIgnoreCase)) item.IsUnique = true;
                break;
            }
        if (Regex.IsMatch(ln, @"\bAncestral\b", RegexOptions.IgnoreCase)) item.IsAncestral = true;
        return true;
    }

    // Canonical ItemType for an anchored ReCharmType line: keep "Set Charm"/"Unique Charm" (set-bonus tracking,
    // inventory grouping and the parser tests depend on the exact designation); every other charm rarity
    // ("Rare Charm", "Charm (Ancestral)", bare "Charm") collapses to "Charm", matching the old fallback.
    static string CanonCharmType(string ln) =>
        Regex.IsMatch(ln, @"\bSet Charm\b", RegexOptions.IgnoreCase) ? "Set Charm"
        : Regex.IsMatch(ln, @"\bUnique Charm\b", RegexOptions.IgnoreCase) ? "Unique Charm"
        : "Charm";

    static Affix? ParseAffix(string ln)
    {
        double? vmin = null, vmax = null;
        string core = ln;
        var rng = ReBracket.Match(ln);
        string afterBracket = "";
        if (rng.Success)
        {
            vmin = ToNum(rng.Groups[1].Value);
            vmax = rng.Groups[2].Success && rng.Groups[2].Value != "" ? ToNum(rng.Groups[2].Value) : null;
            core = ln[..rng.Index].Trim();
            afterBracket = ln[(rng.Index + rng.Length)..].Trim();   // kept for the name-after-bracket fallback
        }
        // (2)/(3) Strip the comparison-tooltip delta and any letter-bearing clarifier from the END of the
        // pre-bracket text BEFORE the affix/summary tests run. Order: delta first (covers "(-3.8% Toughness)"),
        // then the generic letter-bearing clarifier ("(0.8% at level 70)"). A value-only "(+6)" survives —
        // it has no letter, and after a [range] it's already been dropped with the bracket.
        core = ReCompareDelta.Replace(core, "").Trim();
        core = ReTrailingClarifier.Replace(core, "").Trim();

        var m = ReAffix.Match(core);
        if (!m.Success)
            return RecoverMidStringAffix(core, afterBracket, vmin, vmax, rng.Success);   // (4) fallback

        string sign = m.Groups[1].Value, num = m.Groups[2].Value, pct = m.Groups[3].Value, text = m.Groups[4].Value.Trim();
        text = ReAffixTrailJunk.Replace(text, "").Trim();   // strip dangling '+' / stray trailing punctuation (S8 "+109 Dexterity +[..]")
        text = ReLeadingConnective.Replace(text, "").Trim(); // (3) "+2 to Heartseeker" -> "Heartseeker"
        var value = ToNum(num);
        if (value == null || text.Length == 0 || text.Equals("Item Power", StringComparison.OrdinalIgnoreCase)) return null;
        // Drop weapon implicits outright, and tooltip summary totals when in their bare (no sign, no range) form.
        if (ReWeaponStat.IsMatch(text)) return null;
        if (sign.Length == 0 && !rng.Success && ReSummaryStat.IsMatch(text)) return null;
        return new Affix
        {
            Text = text, Value = value, Min = vmin, Max = vmax,
            IsPercent = pct == "%", IsMultiplier = sign == "x",
        };
    }

    /// <summary>
    /// (4) Recover a rolled affix whose value sits MID-STRING so the leading-value <see cref="ReAffix"/>
    /// rejected it: "Lucky Hit: Up to a 15% Chance to Restore +6 Primary Resource [6-8]" and seal/charm
    /// power-name rolls "Way of the Blurring Blade:. +22% [13-25]% Critical Strike Damage". Only fires when
    /// a [min-max] bracket is present (a genuine rollable). The value is the LAST numeric token before the
    /// bracket; the name is the surrounding text (pre- + post-bracket) minus that token and a "Name:." prefix.
    /// Gated to short, single-clause, period-free lines so multi-sentence Imprinted/legendary powers still
    /// route to PowerText, not Affixes.
    /// </summary>
    static Affix? RecoverMidStringAffix(string core, string afterBracket, double? vmin, double? vmax, bool hadBracket)
    {
        if (!hadBracket) return null;                       // no [range] => not a rollable affix
        // Strip a leading "Power Name:." prefix (seal/charm power-name affix lines).
        var lead = RePowerNamePrefix.Match(core);
        string body = (lead.Success ? core[lead.Length..] : core).Trim();
        // SAFE GATE: the value MUST be the leading token. If it isn't, this is a multi-clause power
        // ("Lucky Hit: Up to a +3.5% Chance to Slow for 2 Seconds …") whose value can't be picked
        // reliably — leave it for PowerText (the safe pre-change behavior) rather than emit a wrong affix.
        var lv = ReLeadValue.Match(body);
        if (!lv.Success || lv.Index != 0 || lv.Length == 0) return null;
        var value = ToNum(lv.Groups[2].Value);
        if (value == null) return null;
        bool isPct = lv.Groups[3].Value == "%";
        bool isMul = lv.Groups[1].Value == "x" || lv.Groups[4].Success;   // a trailing "[x]" marks a multiplier
        // Name = the text after the value token + the post-bracket remainder, with the range's unit suffix
        // ("%", "%[x]"), stray [x]/[+] markers, and any trailing comparison delta removed.
        string tail = Regex.Replace(afterBracket, @"^\s*%?\s*(\[x\]|\[\+\])?\s*", "", RegexOptions.IgnoreCase);
        tail = ReCompareDelta.Replace(tail, "").Trim();
        string name = (body[lv.Length..] + " " + tail).Trim();
        name = ReMarkerJunk.Replace(name, "");
        name = ReTrailingClarifier.Replace(name, "").Trim();
        name = Regex.Replace(name, @"\s+", " ").Trim().Trim('%', '+', '.', ',', ';', ':', ' ');
        name = ReLeadingConnective.Replace(name, "").Trim();
        // Reject anything that still carries markup, a sentence boundary, or is too long — route to PowerText.
        if (name.Length == 0 || name.Length > 40) return null;
        if (name.IndexOfAny(new[] { '[', ']', '%', '(', ')' }) >= 0) return null;
        if (name.Contains(". ") || ReWeaponStat.IsMatch(name)) return null;
        return new Affix { Text = name, Value = value, Min = vmin, Max = vmax, IsPercent = isPct, IsMultiplier = isMul };
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
            // Numeric fields use ToNum (TryParse-based, never throws), uniformly with ItemPower/Dps/Socket
            // above. A throwing int.Parse here would fault the whole block on a malformed digit run — and TTS
            // format is season-volatile (CLAUDE.md), so a future format gluing digits must degrade gracefully,
            // not crash the parse, exactly like the rest of the parser.
            var mq = ReQuality.Match(ln);
            if (mq.Success && item.Quality == null) { item.Quality = (int?)ToNum(mq.Groups[1].Value); continue; }
            var mm = ReMasterwork.Match(ln);
            if (mm.Success) { item.MasterworkRank = (int?)ToNum(mm.Groups[1].Value); item.MasterworkMax = (int?)ToNum(mm.Groups[2].Value); continue; }
            var mt = ReTemper.Match(ln);
            if (mt.Success) { item.TemperUsed = (int?)ToNum(mt.Groups[1].Value); item.TemperMax = (int?)ToNum(mt.Groups[2].Value); continue; }
            var mr = ReReqLevel.Match(ln);
            if (mr.Success)
            {
                item.RequiresLevel = (int?)ToNum(mr.Groups[1].Value);
                var mc = ReClassLock.Match(ln);
                if (mc.Success) item.ClassLock = char.ToUpper(mc.Groups[1].Value[0]) + mc.Groups[1].Value[1..].ToLowerInvariant();
                continue;
            }
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
            var msk = ReSocket.Match(ln);
            if (msk.Success) { item.SocketCount = (int?)ToNum(msk.Groups[1].Value); continue; }
            if (ReEmptySocket.IsMatch(ln)) { item.EmptySockets++; continue; }
            var mset = ReSetName.Match(ln);
            if (mset.Success && item.SetName == null)
            {
                item.SetName = mset.Groups[1].Value.Trim();
                item.SetActive = (int?)ToNum(mset.Groups[2].Value);
                item.SetTotal = (int?)ToNum(mset.Groups[3].Value);
                item.PowerText.Add(ln);   // keep the bonus text discoverable like other PowerText
                continue;
            }
            var af = ParseAffix(ln);
            if (af != null) { item.Affixes.Add(af); continue; }
            // Stateful tooltip lines (wear/economy state, menu hints) are NOT item identity — keep them
            // out of PowerText so two captures of the same item differ only when the ITEM differs.
            // ("Durability: N/100. Tempers: a/b" never reaches here — ReTemper consumed it above.)
            if (ReStatefulInfo.IsMatch(ln)) continue;
            if (ln.Any(char.IsLower) && ln.Length > 8) item.PowerText.Add(ln);
        }
        item.IsComparison = afterPropertiesLost;   // bag/stash comparison — never worn
        FinalizeGreaterAffixes(item);
        return item;
    }

    /// <summary>
    /// Infer Greater Affixes once the whole block is parsed (the Quality line can appear before or after the
    /// affix lines, so this can't run per-line). Two cross-checked signals:
    ///   • per-item COUNT — the temper-charge denominator: an item gets 3 base charges + 1 per GA, so
    ///     <c>GreaterAffixCount = TemperMax − 3</c> (clamped 0..4). TemperMax 1 ⇒ Rare (0 GAs); no temper line ⇒ unknown.
    ///   • per-affix INFERENCE — a GA rolls at 1.5× the affix's max. Displayed values are inflated by masterwork
    ///     Quality (+1%/rank, parsed into <see cref="Item.Quality"/>, can exceed 25 via transfigure), so divide
    ///     that out first; a normalized value ≥ 1.2× max is a GA candidate.
    /// The count caps how many affixes get the star (a Quality-25 Capstone adds +50% to one affix and is
    /// indistinguishable per-affix — the highest candidate beyond the GA budget is recorded as the capstone).
    /// </summary>
    static void FinalizeGreaterAffixes(Item item)
    {
        // Mythics are always all-Greater-Affix (doc §2) — independent of the temper/masterwork signal, which
        // a comparison/bag tooltip or a poll-edge-truncated block may not carry. Floor it from the rarity.
        if (item.IsMythic && item.Affixes.Count > 0)
        {
            foreach (var a in item.Affixes) a.IsGreater = true;
            item.GreaterAffixCount = item.Affixes.Count;
            return;
        }

        int? count = item.TemperMax.HasValue ? Math.Clamp(item.TemperMax.Value - 3, 0, 4) : (int?)null;

        double infl = 1.0 + (item.Quality ?? 0) / 100.0;
        var ranked = item.Affixes
            .Select(a => (a, ratio: a.Value is double v && a.Max is double mx && mx > 0 ? (v / infl) / mx : 0.0))
            .Where(t => t.ratio >= 1.2)
            .OrderByDescending(t => t.ratio)
            .ToList();

        int stars = count ?? ranked.Count;   // cap by the authoritative count when known; else trust the inference
        for (int i = 0; i < ranked.Count && i < stars; i++) ranked[i].a.IsGreater = true;

        // the first candidate beyond the GA budget, at Quality 25+, is the Masterwork Capstone (+50% to one affix)
        if ((item.Quality ?? 0) >= 25 && ranked.Count > stars)
            item.CapstoneAffix = ranked[stars].a.Text;

        item.GreaterAffixCount = count ?? (ranked.Count > 0 ? ranked.Count : (int?)null);
    }

    public static bool LooksLikeItem(Item? it)
    {
        if (it == null) return false;
        if (it.ItemPower != null) return true;
        // Season 8 seals/charms/runes have no Item Power line. They also may carry NO rarity word —
        // a Set Charm voices only its type ("Set Charm") + affixes, so gating on Rarity discarded the
        // equipped charm (e.g. PHOBA OF MASTERY) and left every charm slot empty in the diff. The TypeSlot
        // pass already classified the item from its type line, so trust ItemType instead of Rarity here.
        if (it.Slot is "seal" or "charm" or "rune") return it.ItemType != null;
        return it.Rarity != null && it.Affixes.Count > 0;
    }

    /// <summary>Parse an already-extracted tooltip block (OCR path — no EQUIPPED/end-marker machinery).</summary>
    public static Item? ParseTooltipLines(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return null;
        string? name = null;
        var body = new List<string>();
        foreach (var raw in lines)
        {
            var ln = Clean(raw);
            if (ln.Length == 0) continue;
            var nc = NameCandidate(ln);
            if (name == null) { if (nc != null) name = nc; continue; }
            body.Add(ln);
        }
        if (name == null) return null;
        var item = ParseBlock(name, body);
        return LooksLikeItem(item) ? item : null;
    }

    // ---- stateful segmenter ----
    string? _name;
    List<string> _body = new();
    bool _equip, _blockEquip;
    bool _seenSlotHeader, _blockFromCharPanel;
    int _slotHeaderAge;           // lines fed since the header armed — expires stray labels (see Feed)
    string? _currentSlotHeader;   // most recent character-panel slot header
    int _blockSlotPosition;       // 1-based position within a multi-slot category (rings, weapons)
    DateTimeOffset? _lastLogTime; // '[ISO]' time of the most recently fed line — the block's hover time

    // D4 character-panel slot headers — voiced when the player opens the character sheet.
    // If one of these immediately precedes an EQUIPPED block it confirms the item is definitively worn.
    static readonly HashSet<string> SlotHeaders = new(StringComparer.OrdinalIgnoreCase)
        { "Head", "Torso", "Hands", "Legs", "Feet", "Ring", "Neck",
          "Main Hand", "Off-Hand", "Ranged", "Ranged Weapon" };

    // Position counters: D4 repeats the same slot header for each position (e.g. "Ring" twice).
    // Tracking counts let us distinguish Ring #1 from Ring #2, etc.
    readonly Dictionary<string, int> _slotPositionCounts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reset slot-position counters so re-opening the character panel starts fresh.
    /// Without this, the same weapon can accumulate position 1, 2, 3 … across panel opens.</summary>
    public void ResetSlotPositions() => _slotPositionCounts.Clear();

    /// <summary>True when a cleaned line starts a NEW tooltip block or context (EQUIPPED marker, slot
    /// header, ALL-CAPS item name, session marker). The watcher's post-end-marker lookahead stops here:
    /// the previous item's action tail has ended, so no further action verb can belong to that item.</summary>
    internal static bool IsBlockBoundary(string cleanedLine) =>
        cleanedLine.Equals("EQUIPPED", StringComparison.OrdinalIgnoreCase)
        || SlotHeaders.Contains(cleanedLine)
        || cleanedLine.StartsWith("=== d4scanner", StringComparison.OrdinalIgnoreCase)
        || NameCandidate(cleanedLine) != null;

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
        var ln = CleanWithTime(raw, out var lineTime);
        if (lineTime != null) _lastLogTime = lineTime;   // remember the block's true hover time for stamping
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
            _slotHeaderAge = 0;
            return null;
        }
        // Session restart marker — fully reset all parser state so stale blocks from a previous
        // session don't corrupt the first item parsed in the new session.
        if (ln.StartsWith("=== d4scanner", StringComparison.OrdinalIgnoreCase))
        {
            _name = null; _body = new(); _equip = false; _blockEquip = false;
            _seenSlotHeader = false; _blockFromCharPanel = false; _currentSlotHeader = null;
            _blockSlotPosition = 0; _slotPositionCounts.Clear();
            _lastLogTime = null;   // a new session starts fresh — don't inherit the prior session's stamp
            return null;
        }
        if (ln.Equals("EQUIPPED", StringComparison.OrdinalIgnoreCase)) { _equip = true; return null; }
        var low = ln.ToLowerInvariant();
        var nc = NameCandidate(ln);
        // A pending slot header is only good for the block that starts right after it — a few lines at
        // most ("Ring" → "Slot Transmog: ON" → "EQUIPPED" → NAME). If no name consumed it within 6
        // non-name lines it was a stray label (inventory paper-doll dump, vendor gamble category), and
        // letting it survive would stamp the NEXT unrelated hover FromCharPanel=true (a verified leak).
        if (_seenSlotHeader && nc == null && ++_slotHeaderAge > 6) _seenSlotHeader = false;
        if (_name == null) { if (nc != null) Start(nc); return null; }
        // New ALL-CAPS name while already in a block: previous block had no end-marker (e.g. interrupted
        // hover). Discard the stale block and start fresh — avoids mixing two items' body lines.
        if (nc != null && !string.Equals(nc, _name, StringComparison.OrdinalIgnoreCase))
            { Start(nc); return null; }
        if (EndMarkers.Any(low.Contains))
        {
            var item = ParseBlock(_name, _body);
            item.Equipped = _blockEquip;
            item.FromCharPanel = _blockFromCharPanel;
            item.LogTimeUtc = _lastLogTime;           // true hover time from the line's '[ISO]' prefix (null if un-stamped)
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
