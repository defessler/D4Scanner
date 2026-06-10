using System.Globalization;
using System.Text.RegularExpressions;

namespace D4Scanner.Core;

/// <summary>Port of tracker/diff.js — conservative, value-aware build-vs-gear diff.</summary>
public static class DiffEngine
{
    public static string Normalize(string? s) =>
        Regex.Replace(Regex.Replace((s ?? "").ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim(), @"\s+", " ");

    public static bool PhraseMatch(string a, string b)
    {
        a = Normalize(a); b = Normalize(b);
        if (a.Length == 0 || b.Length == 0) return false;
        if (a == b) return true;
        if (a.Length >= 3 && b.Contains(a)) return true;
        if (b.Length >= 3 && a.Contains(b)) return true;
        return false;
    }

    static string SlotBase(string? slot) => Regex.Replace(Normalize(slot), @"\s*\d+$", "").Trim();

    static List<Item> PooledItems(LiveBuild live, string baseSlot) =>
        live.Gear.Where(it => SlotBase(it.Slot) == baseSlot || (string.IsNullOrEmpty(it.Slot) && baseSlot == "")).ToList();

    static string FmtVal(Affix? af)
    {
        if (af?.Value == null) return "";
        double v = af.Value.Value;
        string num = v.ToString("#,0.##", CultureInfo.InvariantCulture);
        string pre = af.IsMultiplier ? "x" : (v > 0 ? "+" : "");
        return pre + num + (af.IsPercent ? "%" : "");
    }

    static Group MakeGroup(string name, List<ReqItem> items)
    {
        foreach (var i in items) if (i.Done && i.Status == "missing") i.Status = "met";  // non-gear items
        return new Group
        {
            Name = name, Items = items, Total = items.Count,
            Matched = items.Count(i => i.Done),
            Under = items.Count(i => i.Status == "under"),
        };
    }

    static Category MakeCategory(string id, string name, List<Group> groups)
    {
        int m = groups.Sum(g => g.Matched), t = groups.Sum(g => g.Total), u = groups.Sum(g => g.Under);
        return new Category { Id = id, Name = name, Groups = groups, Matched = m, Total = t, Under = u, Pct = t > 0 ? (int)Math.Round(100.0 * m / t) : 0 };
    }

    /// <summary>Roll quality of a matched affix within its own [min..max] range, 0-100 (null if no range).</summary>
    static double? RollPct(Affix af)
    {
        if (af.Value == null || af.Min == null || af.Max == null) return null;
        double lo = af.Min.Value, hi = af.Max.Value, v = af.Value.Value;
        if (hi <= lo) return null;
        return Math.Max(0, Math.Min(100, (v - lo) / (hi - lo) * 100.0));
    }

    /// <summary>How many of a target slot's affixes a given item meets (presence + threshold). For upgrade-finding.</summary>
    public static int ScoreSlot(TargetGear g, Item item, double gate)
    {
        var pool = item.Affixes;
        var used = new bool[pool.Count];
        int met = 0;
        foreach (var aff in g.Affixes)
        {
            Affix? match = null;
            for (int i = 0; i < pool.Count; i++)
            {
                if (used[i]) continue;
                if (PhraseMatch(aff.Name, pool[i].Text)) { match = pool[i]; used[i] = true; break; }
            }
            if (match == null) continue;
            bool ok;
            if (aff.Min != null) ok = (match.Value ?? 0) >= aff.Min.Value;
            else
            {
                var pct = RollPct(match); double thr = aff.MinPercent ?? gate;
                // when there is no range data (item scanned without Advanced Tooltip, or binary affix):
                //   - if only the global gate applies (no explicit MinPercent), treat as presence-only match
                //   - if there IS an explicit MinPercent threshold, we can't verify → don't count
                ok = pct != null ? pct.Value >= thr : aff.MinPercent == null;
            }
            if (ok) met++;
        }
        return met;
    }

    /// <summary>
    /// Evaluate ANY item against a target slot's affix set, producing one display-ready <see cref="ReqItem"/>
    /// per wanted affix (met / under / missing, value, roll %, threshold) plus the item's surplus affixes.
    /// This is the slot evaluation <see cref="Diff"/> uses for equipped gear, extracted so candidate items
    /// (the All-Items hover) render with EXACTLY the same rows. Missing affixes still carry the target
    /// threshold in <see cref="ReqItem.Need"/> so a comparison can always show what the build wants.
    /// </summary>
    public static List<ReqItem> EvalSlot(TargetGear g, Item? it, double gate, out List<string> extras)
    {
        var pool = it?.Affixes ?? new List<Affix>();
        var used = new bool[pool.Count];
        var items = new List<ReqItem>();
        foreach (var aff in g.Affixes)
        {
            Affix? match = null;
            for (int i = 0; i < pool.Count; i++)
            {
                if (used[i]) continue;
                if (PhraseMatch(aff.Name, pool[i].Text)) { match = pool[i]; used[i] = true; break; }
            }
            var req = new ReqItem { Label = aff.Name, Tempered = aff.Tempered };
            // the target is known even when the piece is missing the affix — always show what the build wants
            if (aff.Min != null) { req.TargetNum = aff.Min.Value; req.Need = "≥ " + aff.Min.Value.ToString("#,0.##"); }
            else if (aff.MinPercent != null) req.Need = $"roll ≥ {aff.MinPercent:0}%";
            if (match == null) { req.Status = "missing"; req.Done = false; }
            else
            {
                req.Done = true;            // you have the affix (presence)
                req.Source = "tts";
                req.Val = FmtVal(match);
                req.ValueNum = match.Value;
                req.IsMultiplier = match.IsMultiplier;
                req.IsPercent = match.IsPercent;
                var pct = RollPct(match);
                req.RollPct = pct;
                // threshold: explicit absolute min > explicit minPercent > global gate
                if (aff.Min != null)
                {
                    req.Status = (match.Value ?? 0) >= aff.Min.Value ? "met" : "under";
                }
                else
                {
                    double thr = aff.MinPercent ?? gate;
                    // show the concrete value needed (thr% into the affix's captured [min..max] range)
                    // rather than a bare "roll ≥ N%" when a range is available to derive it from.
                    if (match.Min != null && match.Max != null && match.Max.Value > match.Min.Value)
                    {
                        double need = match.Min.Value + thr / 100.0 * (match.Max.Value - match.Min.Value);
                        req.TargetNum = need;
                        req.Need = "≥ " + need.ToString(match.IsPercent ? "#,0.#" : "#,0") + (match.IsPercent ? "%" : "");
                    }
                    req.Status = (pct == null || pct.Value >= thr) ? "met" : "under";
                }
            }
            items.Add(req);
        }
        extras = new List<string>();
        for (int i = 0; i < pool.Count; i++)
        {
            if (used[i] || Regex.IsMatch(pool[i].Text, "quality", RegexOptions.IgnoreCase)) continue;
            var d = FmtVal(pool[i]); var s = pool[i].Text + (d.Length > 0 ? " " + d : "");
            if (!extras.Contains(s)) extras.Add(s);
        }
        return items;
    }

    /// <summary>How many of a target slot's affixes are PRESENT on the item at ANY value (no roll/threshold
    /// gate; each item affix matched once). The primary upgrade signal: having the right affix at a low roll
    /// beats not having it at all — rolls can be masterworked up, missing affixes can only be enchanted in
    /// one at a time.</summary>
    public static int PresenceCount(TargetGear g, Item item)
    {
        var pool = item.Affixes;
        var used = new bool[pool.Count];
        int present = 0;
        foreach (var aff in g.Affixes)
            for (int i = 0; i < pool.Count; i++)
            {
                if (used[i]) continue;
                if (PhraseMatch(aff.Name, pool[i].Text)) { used[i] = true; present++; break; }
            }
        return present;
    }

    /// <summary>Average roll quality (0-100) of the target slot's affixes that <paramref name="item"/> actually
    /// has (matched once each). 0 when it has none of them; affixes with no range count as a perfect 100. An
    /// upgrade sub-tiebreak between items that meet the same number of slot affixes.</summary>
    public static double SlotQuality(TargetGear g, Item item)
    {
        var pool = item.Affixes;
        var used = new bool[pool.Count];
        double sum = 0; int n = 0;
        foreach (var aff in g.Affixes)
            for (int i = 0; i < pool.Count; i++)
            {
                if (used[i]) continue;
                if (PhraseMatch(aff.Name, pool[i].Text)) { used[i] = true; sum += RollPct(pool[i]) ?? 100; n++; break; }
            }
        return n == 0 ? 0 : sum / n;
    }

    /// <summary>Does a single target affix appear on the item and meet its threshold? (first match — used for
    /// substitute/flexibility scoring where the strict per-item dedup of <see cref="ScoreSlot"/> isn't needed.)</summary>
    public static bool AffixMet(TargetAffix aff, Item item, double gate)
    {
        foreach (var x in item.Affixes)
            if (PhraseMatch(aff.Name, x.Text))
            {
                if (aff.Min != null) return (x.Value ?? 0) >= aff.Min.Value;
                var pct = RollPct(x); double thr = aff.MinPercent ?? gate;
                return pct == null || pct.Value >= thr;
            }
        return false;
    }

    /// <summary>True when a target ItemId and a live ItemType refer to the same weapon type.
    /// Used to give a big assignment-score bonus so a crossbow target always picks the crossbow.</summary>
    static bool WeaponTypeMatch(string? targetItemId, string? liveItemType)
    {
        if (string.IsNullOrEmpty(targetItemId) || string.IsNullOrEmpty(liveItemType)) return false;
        var id = targetItemId.ToLowerInvariant();
        var lt = liveItemType.ToLowerInvariant();
        if (id.Contains("crossbow") && lt.Contains("crossbow")) return true;
        if (id.Contains("bow") && !id.Contains("crossbow") && lt.Contains("bow") && !lt.Contains("crossbow")) return true;
        if (id.Contains("sword") && lt.Contains("sword")) return true;
        if (id.Contains("dagger") && lt.Contains("dagger")) return true;
        if (id.Contains("mace") && lt.Contains("mace")) return true;
        if (id.Contains("axe") && lt.Contains("axe")) return true;
        if (id.Contains("staff") && lt.Contains("staff")) return true;
        if (id.Contains("scythe") && lt.Contains("scythe")) return true;
        if (id.Contains("polearm") && lt.Contains("polearm")) return true;
        if (id.Contains("spear") && lt.Contains("spear")) return true;
        if (id.Contains("wand") && lt.Contains("wand")) return true;
        if (id.Contains("glaive")      && lt.Contains("glaive"))      return true;
        if (id.Contains("quarterstaff") && lt.Contains("quarterstaff")) return true;
        if (id.Contains("totem")        && lt.Contains("totem"))        return true;
        if (id.Contains("focus")        && lt.Contains("focus"))        return true;
        if (id.Contains("shield")       && lt.Contains("shield"))       return true;
        return false;
    }

    static readonly string[] WeaponKeywords =
        { "crossbow", "bow", "sword", "dagger", "mace", "axe", "staff", "scythe", "polearm", "spear", "wand", "glaive", "quarterstaff", "blade" };
    /// <summary>True when the type/itemId names any recognised weapon (vs. armor/jewellery).</summary>
    static bool IsWeaponType(string? s) => !string.IsNullOrEmpty(s) && WeaponKeywords.Any(k => s.ToLowerInvariant().Contains(k));
    /// <summary>True for ranged weapons (bow / crossbow). Used to keep melee weapons out of ranged slots.</summary>
    static bool IsRangedWeapon(string? s) => !string.IsNullOrEmpty(s) && s.ToLowerInvariant() is var t && (t.Contains("crossbow") || t.Contains("bow"));

    /// <summary>Human-readable weapon type from ItemId.</summary>
    public static string? WeaponTypeFromItemId(string? itemId) {
        if (string.IsNullOrEmpty(itemId)) return null;
        var p = itemId.Split("_")[0]; var h = (p.StartsWith("1H")||p.StartsWith("2H")) ? p.Substring(2) : p;
        return h.Length > 1 ? char.ToUpperInvariant(h[0]) + h.Substring(1).ToLowerInvariant() : null;
    }

    /// <summary>Base slot name without a trailing index (e.g. "Ring #1" → "ring"). Public for reuse.</summary>
    public static string SlotBaseName(string? slot) => SlotBase(slot);

    public static DiffReport Diff(TargetBuild target, LiveBuild live, double defaultMinRollPercent = 50)
    {
        var cats = new List<Category>();

        // ---- Gear & affixes (value-aware: HAVE vs NEED per slot) ----
        if (target.Gear.Count > 0)
        {
            // assign one distinct live item to each target slot (rings/weapons sharing a base each get their best match)
            var byBase = new Dictionary<string, List<int>>();
            for (int i = 0; i < target.Gear.Count; i++)
            {
                var b = SlotBase(target.Gear[i].Slot);
                if (!byBase.TryGetValue(b, out var lst)) byBase[b] = lst = new();
                lst.Add(i);
            }
            var assigned = new Dictionary<int, Item?>();
            foreach (var kv in byBase)
            {
                var liveIts = PooledItems(live, kv.Key);
                var taken = new bool[liveIts.Count];
                foreach (var idx in kv.Value)
                {
                    var aff = target.Gear[idx].Affixes;
                    int best = -1, bestScore = -1;
                    for (int i = 0; i < liveIts.Count; i++)
                    {
                        if (taken[i]) continue;
                        // Weapon-class gate: a melee weapon (sword/dagger/…) must never fill a ranged slot
                        // (crossbow/bow) or vice-versa. Without this, a crossbow slot with no live crossbow
                        // borrows the player's melee dagger by affix-count — the exact bug being fixed.
                        if (kv.Key == "weapon" && IsWeaponType(target.Gear[idx].ItemId) && IsWeaponType(liveIts[i].ItemType)
                            && IsRangedWeapon(liveIts[i].ItemType) != IsRangedWeapon(target.Gear[idx].ItemId))
                            continue;
                        int score = aff.Count(a => liveIts[i].Affixes.Any(x => PhraseMatch(a.Name, x.Text)));
                        // Type-affinity bonus: when the target slot has an ItemId encoding a weapon type (e.g.
                        // "2HCrossbow_Legendary_..."), strongly prefer a live item whose ItemType matches.
                        // A 100-point bonus dominates the affix-count score so a crossbow target always picks
                        // the crossbow over a sword with more matching affixes.
                        if (kv.Key == "weapon" && WeaponTypeMatch(target.Gear[idx].ItemId, liveIts[i].ItemType))
                            score += 100;
                        if (score > bestScore) { bestScore = score; best = i; }
                    }
                    if (best >= 0) { taken[best] = true; assigned[idx] = liveIts[best]; } else assigned[idx] = null;
                }
            }

            var gearGroups = new List<Group>();
            for (int gi = 0; gi < target.Gear.Count; gi++)
            {
                var g = target.Gear[gi];
                var it = assigned.TryGetValue(gi, out var ai) ? ai : null;
                double gate = target.MinRollPercent ?? defaultMinRollPercent;
                var items = EvalSlot(g, it, gate, out var extras);
                var grp = MakeGroup(g.Label ?? g.Slot, items);
                grp.Kind = "gear";
                grp.LiveItems = it != null
                    ? new() { new GearLiveItem { Name = it.Name, Rarity = it.Rarity, ItemPower = it.ItemPower, IsUnique = it.IsUnique, IsAncestral = it.IsAncestral, Aspect = it.Aspect } }
                    : new();
                grp.Extras = extras;
                grp.WantAspect = g.Aspect;
                grp.WantSockets = g.Sockets;
                // Socket HAVE/NEED — only when the target actually wants sockets here (zero-risk for non-socket slots).
                if (g.Sockets.Count > 0)
                {
                    int want = g.Sockets.Count;
                    // filled count: a captured runeword/socketed rune counts as filled; otherwise derive from
                    // total capacity minus the empties the tooltip voiced. Clamp to >= 0.
                    int? cap = it?.SocketCount;
                    int empty = it?.EmptySockets ?? 0;
                    bool hasRune = !string.IsNullOrEmpty(it?.RunewordName) || (it?.SocketedRunes.Count > 0);
                    int filled = cap != null ? Math.Max(0, cap.Value - empty)
                               : hasRune ? want                         // runeword present, no bare socket line
                               : (it != null ? Math.Max(0, want - empty) : 0);
                    bool done = it != null && empty == 0 && (cap == null ? hasRune : filled >= cap.Value) && filled >= want;
                    grp.SocketsDone = done;
                    grp.SocketStatus = it == null
                        ? $"0/{want} filled (item not detected)"
                        : (cap != null
                            ? $"{filled}/{cap.Value} filled" + (empty > 0 ? $" ({empty} empty)" : "")
                            : hasRune ? $"{want}/{want} filled (runeword)" : $"{Math.Max(0, want - empty)}/{want} filled" + (empty > 0 ? $" ({empty} empty)" : ""));
                }

                // upgrade-finding: non-equipped items of this slot that meet MORE target affixes
                int eqMet = items.Count(x => x.Status == "met");
                var baseSlot = SlotBase(g.Slot);
                foreach (var inv in live.Inventory)
                {
                    if (SlotBase(inv.Slot) != baseSlot) continue;
                    int met = ScoreSlot(g, inv, gate);
                    if (met > eqMet) grp.UpgradeItems.Add($"{inv.Name}  ({met}/{g.Affixes.Count})");
                }
                gearGroups.Add(grp);
            }
            cats.Add(MakeCategory("gear", "Gear & Affixes", gearGroups));
        }

        // ---- Uniques & mythics (need X, you have Y) ----
        if (target.Uniques.Count > 0)
        {
            var uitems = target.Uniques.Select(u =>
            {
                bool done = live.Gear.Any(it => PhraseMatch(u.Name, it.Name));
                var slotItems = !string.IsNullOrEmpty(u.Slot) ? PooledItems(live, SlotBase(u.Slot)) : new List<Item>();
                var have = string.Join(", ", slotItems.Select(it => it.Name).Where(n => !string.IsNullOrEmpty(n)));
                return new ReqItem
                {
                    Label = u.Name + (u.Mythic ? " (Mythic)" : ""),
                    Done = done, Source = done ? "tts" : null,
                    Have = (have.Length > 0 && !done) ? have : null,
                };
            }).ToList();
            cats.Add(MakeCategory("uniques", "Uniques & Mythics", new() { MakeGroup("Equipped", uitems) }));
        }

        // ---- Aspects ----
        if (target.Aspects.Count > 0)
        {
            var aitems = target.Aspects.Select(asp =>
            {
                bool done = live.Aspects.Any(a => PhraseMatch(asp, a));
                if (!done)
                {
                    var key = Regex.Replace(Normalize(asp), @"\b(aspect|of|the)\b", "").Trim();
                    done = live.Gear.Any(it =>
                        (it.Aspect != null && PhraseMatch(asp, it.Aspect)) ||
                        it.PowerText.Any(p => key.Length >= 3 && Normalize(p).Contains(key)));
                }
                return new ReqItem { Label = asp, Done = done, Source = done ? "vision" : null };
            }).ToList();
            cats.Add(MakeCategory("aspects", "Aspects", new() { MakeGroup("Aspects", aitems) }));
        }

        // ---- Skills & key passives ----
        var skillGroups = new List<Group>();
        if (target.Skills.Count > 0)
        {
            var sitems = target.Skills.Select(t =>
            {
                var hit = live.Skills.FirstOrDefault(s => PhraseMatch(t.Name, s.Name));
                int have = hit?.Rank ?? 0;
                int want = t.Rank ?? 1;
                bool done = hit != null && have >= want;
                return new ReqItem
                {
                    Label = t.Name, Done = done,
                    Status = done ? "met" : hit != null ? "under" : "missing",
                    Have = hit != null ? $"{have} pts" : null,
                    Need = $"wants {want}",   // want = t.Rank ?? 1 — D4 actives are 1 base point (gear adds the rest)
                    Val = hit != null ? have.ToString() : null,
                    Source = done ? "tts" : null,
                };
            }).ToList();
            skillGroups.Add(MakeGroup("Active Skills", sitems));
        }
        if (target.KeyPassives.Count > 0)
        {
            var kitems = target.KeyPassives.Select(name =>
            {
                var hit = live.Skills.FirstOrDefault(s => PhraseMatch(name, s.Name));
                bool done = hit != null;
                return new ReqItem
                {
                    Label = name, Done = done, Status = done ? "met" : "missing",
                    Have = hit?.Rank != null ? $"{hit.Rank} pts" : (done ? "selected" : null),
                    Source = done ? "tts" : null,
                };
            }).ToList();
            skillGroups.Add(MakeGroup("Key Passives", kitems));
        }
        if (skillGroups.Count > 0) cats.Add(MakeCategory("skills", "Skills & Passives", skillGroups));

        // ---- Paragon (boards + glyphs) ----
        var paraGroups = new List<Group>();
        var lp = live.Paragon;
        if (target.Paragon?.Boards.Count > 0)
        {
            var bitems = target.Paragon.Boards.Select(b =>
                new ReqItem { Label = b, Done = lp.Any(p => PhraseMatch(b, p.Board)), Source = lp.Any(p => PhraseMatch(b, p.Board)) ? "vision" : null }).ToList();
            paraGroups.Add(MakeGroup("Boards", bitems));
        }
        if (target.Paragon?.Glyphs.Count > 0)
        {
            var gitems = target.Paragon.Glyphs.Select(gl =>
            {
                var hit = lp.FirstOrDefault(p => p.Glyph != null && PhraseMatch(gl.Name, p.Glyph));
                bool done = hit != null && (gl.Level == null || (hit.GlyphLevel ?? 0) >= gl.Level);
                int lvl = hit?.GlyphLevel ?? 0;
                string label = gl.Name + (gl.Level != null ? "  " + lvl + " / " + gl.Level : "");
                return new ReqItem { Label = label, Done = done, Source = done ? "vision" : null };
            }).ToList();
            paraGroups.Add(MakeGroup("Glyphs", gitems));
        }
        if (paraGroups.Count > 0) cats.Add(MakeCategory("paragon", "Paragon & Glyphs", paraGroups));

        // ---- Mercenary (vision-gated; talismans are not present in Maxroll planner data) ----
        if (target.Mercenary != null)
        {
            var mItems = new List<ReqItem>();
            void AddMerc(string? label, string role)
            {
                if (string.IsNullOrEmpty(label)) return;
                bool done = !string.IsNullOrEmpty(live.Mercenary) && PhraseMatch(label!, live.Mercenary!);
                mItems.Add(new ReqItem { Label = role + ": " + label, Done = done, Source = done ? "vision" : null });
            }
            AddMerc(target.Mercenary.Main, "Mercenary");
            AddMerc(target.Mercenary.Support, "Reinforcement");
            if (mItems.Count > 0) cats.Add(MakeCategory("mercenary", "Mercenary", new() { MakeGroup("Mercenary", mItems) }));
        }

        int m = cats.Sum(c => c.Matched), t2 = cats.Sum(c => c.Total), u = cats.Sum(c => c.Under);
        return new DiffReport
        {
            TargetName = target.Name, TargetClass = target.Class,
            Matched = m, Total = t2, Under = u, Pct = t2 > 0 ? (int)Math.Round(100.0 * m / t2) : 0,
            Categories = cats,
        };
    }
}
