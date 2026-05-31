/* D4Scanner diff engine — compares a captured LIVE build against a TARGET build.
 * Works in the browser (window.D4Diff) and in Node (module.exports).
 *
 * Matching philosophy: conservative. Two phrases match only on normalized
 * equality or full substring containment (either direction, min length 3).
 * No loose token overlap — "Critical Strike Chance" must NOT match
 * "Critical Strike Damage". Within a gear slot, each live affix can satisfy at
 * most one target requirement (greedy assignment) so counts stay honest.
 */
(function (root, factory) {
  if (typeof module === "object" && module.exports) module.exports = factory();
  else root.D4Diff = factory();
})(typeof self !== "undefined" ? self : this, function () {
  "use strict";

  function normalize(s) {
    return String(s == null ? "" : s)
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, " ")
      .trim()
      .replace(/\s+/g, " ");
  }

  function phraseMatch(a, b) {
    a = normalize(a);
    b = normalize(b);
    if (!a || !b) return false;
    if (a === b) return true;
    if (a.length >= 3 && b.indexOf(a) !== -1) return true;
    if (b.length >= 3 && a.indexOf(b) !== -1) return true;
    return false;
  }

  function slotBase(slot) {
    return normalize(slot).replace(/\s*\d+$/, "").trim();
  }

  // Live items whose base slot matches `base` (rings pool together).
  function pooledItems(live, base) {
    return (live.gear || []).filter(function (item) {
      return slotBase(item.slot) === base || (!item.slot && base === "");
    });
  }

  // Format a rolled affix value, e.g. "+1,540", "18.5%", "x24%".
  function fmtVal(af) {
    if (!af || af.value == null) return "";
    var v = af.value;
    var num = (typeof v === "number") ? v.toLocaleString("en-US", { maximumFractionDigits: 2 }) : String(v);
    var pre = af.isMultiplier ? "x" : ((typeof v === "number" && v > 0) ? "+" : "");
    return pre + num + (af.isPercent ? "%" : "");
  }

  // a target affix is a string ("Maximum Life") or an object {name, min, minPercent}
  function affName(a) { return typeof a === "string" ? a : (a && a.name) || ""; }

  function rollPct(af) {
    if (!af || af.value == null || af.min == null || af.max == null) return null;
    var lo = af.min, hi = af.max, v = af.value;
    if (hi <= lo) return null;
    return Math.max(0, Math.min(100, (v - lo) / (hi - lo) * 100));
  }

  function group(name, items) {
    items.forEach(function (i) { if (i.done && (!i.status || i.status === "missing")) i.status = "met"; });
    var matched = items.filter(function (i) { return i.done; }).length;
    var under = items.filter(function (i) { return i.status === "under"; }).length;
    return { name: name, items: items, matched: matched, total: items.length, under: under };
  }

  function category(id, name, groups) {
    var matched = 0, total = 0, under = 0;
    groups.forEach(function (g) { matched += g.matched; total += g.total; under += (g.under || 0); });
    return {
      id: id, name: name, groups: groups,
      matched: matched, total: total, under: under,
      pct: total ? Math.round((matched / total) * 100) : 0,
    };
  }

  function diff(target, live) {
    target = target || {};
    live = live || {};
    var cats = [];

    // ---- Gear & affixes (value-aware: HAVE vs NEED per slot) ----
    if (target.gear && target.gear.length) {
      // Assign one distinct live item to each target slot. Slots that share a base
      // (ring1/ring2, the 3 weapons) each get their best-matching unused live item.
      var byBase = {};
      target.gear.forEach(function (g, idx) {
        var b = slotBase(g.slot); (byBase[b] = byBase[b] || []).push(idx);
      });
      var assigned = {};
      Object.keys(byBase).forEach(function (b) {
        var liveIts = pooledItems(live, b);
        var taken = liveIts.map(function () { return false; });
        byBase[b].forEach(function (idx) {
          var aff = target.gear[idx].affixes || [];
          var best = -1, bestScore = -1;
          for (var i = 0; i < liveIts.length; i++) {
            if (taken[i]) continue;
            var score = 0;
            aff.forEach(function (a) {
              if ((liveIts[i].affixes || []).some(function (x) { return phraseMatch(affName(a), x.text); })) score++;
            });
            if (score > bestScore) { bestScore = score; best = i; }
          }
          if (best >= 0) { taken[best] = true; assigned[idx] = liveIts[best]; } else assigned[idx] = null;
        });
      });

      var gate = (target.minRollPercent != null ? target.minRollPercent : 50);
      var gearGroups = target.gear.map(function (g, idx) {
        var it = assigned[idx];
        var pool = it ? (it.affixes || []) : [];
        var used = {};
        var items = (g.affixes || []).map(function (aff) {
          var name = affName(aff);
          var match = null;
          for (var i = 0; i < pool.length; i++) {
            if (used[i]) continue;
            if (phraseMatch(name, pool[i].text)) { match = pool[i]; used[i] = true; break; }
          }
          var req = { label: name, done: !!match, source: match ? "tts" : null,
                      val: match ? fmtVal(match) : null, status: match ? "met" : "missing", rollPct: null, need: null };
          if (match) {
            var pct = rollPct(match); req.rollPct = pct;
            if (typeof aff === "object" && aff.min != null) {
              req.need = "≥ " + aff.min; req.status = (match.value || 0) >= aff.min ? "met" : "under";
            } else {
              var thr = (typeof aff === "object" && aff.minPercent != null) ? aff.minPercent : gate;
              req.need = "roll ≥ " + Math.round(thr) + "%";
              req.status = (pct == null || pct >= thr) ? "met" : "under";
            }
          }
          return req;
        });
        var extras = [];
        pool.forEach(function (af, i) {
          if (used[i] || /quality/i.test(af.text)) return;   // skip matched + masterwork "Quality" noise
          var d = fmtVal(af), s = af.text + (d ? " " + d : "");
          if (extras.indexOf(s) < 0) extras.push(s);
        });
        var grp = group(g.label || g.slot, items);
        grp.kind = "gear";
        grp.liveItems = it ? [{ name: it.name, rarity: it.rarity, itemPower: it.itemPower, isUnique: !!it.isUnique }] : [];
        grp.extras = extras;
        return grp;
      });
      cats.push(category("gear", "Gear & Affixes", gearGroups));
    }

    // ---- Uniques & mythics (need X, you have Y) ----
    if (target.uniques && target.uniques.length) {
      var uitems = target.uniques.map(function (u) {
        var done = (live.gear || []).some(function (it) { return phraseMatch(u.name, it.name); });
        var slotItems = u.slot ? pooledItems(live, slotBase(u.slot)) : [];
        var have = slotItems.map(function (it) { return it.name; }).filter(Boolean).join(", ");
        return {
          label: u.name + (u.mythic ? " (Mythic)" : ""),
          done: done, source: done ? "tts" : null,
          have: (have && !done) ? have : null, slot: u.slot || null,
        };
      });
      cats.push(category("uniques", "Uniques & Mythics", [group("Equipped", uitems)]));
    }

    // ---- Aspects ----
    if (target.aspects && target.aspects.length) {
      var liveAspects = live.aspects || [];
      var aitems = target.aspects.map(function (asp) {
        var done = liveAspects.some(function (a) { return phraseMatch(asp, a); });
        if (!done) {
          var key = normalize(asp).replace(/\b(aspect|of|the)\b/g, "").trim();
          done = (live.gear || []).some(function (it) {
            if (it.aspect && phraseMatch(asp, it.aspect)) return true;
            return (it.powerText || []).some(function (p) { return key.length >= 3 && normalize(p).indexOf(key) !== -1; });
          });
        }
        return { label: asp, done: done, source: done ? "vision" : null };
      });
      cats.push(category("aspects", "Aspects", [group("Aspects", aitems)]));
    }

    // ---- Skills & key passives ----
    var skillGroups = [];
    if (target.skills && target.skills.length) {
      var sk = (live.skills || []);
      var sitems = target.skills.map(function (t) {
        var hit = sk.find(function (s) { return phraseMatch(t.name, s.name); });
        var done = !!hit && (t.rank == null || (hit.rank || 0) >= t.rank);
        var label = t.name + (t.rank ? " " + (hit ? (hit.rank || 0) : 0) + "/" + t.rank : "");
        return { label: label, done: done, source: done ? "vision" : null };
      });
      skillGroups.push(group("Active Skills", sitems));
    }
    if (target.keyPassives && target.keyPassives.length) {
      var kp = (live.skills || []);
      var kitems = target.keyPassives.map(function (name) {
        var done = kp.some(function (s) { return s.isKeyPassive && phraseMatch(name, s.name); }) ||
                   kp.some(function (s) { return phraseMatch(name, s.name); });
        return { label: name, done: done, source: done ? "vision" : null };
      });
      skillGroups.push(group("Key Passives", kitems));
    }
    if (skillGroups.length) cats.push(category("skills", "Skills & Passives", skillGroups));

    // ---- Paragon (boards + glyphs) ----
    var paraGroups = [];
    var lp = live.paragon || [];
    if (target.paragon && target.paragon.boards && target.paragon.boards.length) {
      var bitems = target.paragon.boards.map(function (b) {
        var done = lp.some(function (p) { return phraseMatch(b, p.board); });
        return { label: b, done: done, source: done ? "vision" : null };
      });
      paraGroups.push(group("Boards", bitems));
    }
    if (target.paragon && target.paragon.glyphs && target.paragon.glyphs.length) {
      var gitems = target.paragon.glyphs.map(function (gl) {
        var hit = lp.find(function (p) { return p.glyph && phraseMatch(gl.name, p.glyph); });
        var done = !!hit && (gl.level == null || (hit.glyphLevel || 0) >= gl.level);
        var lvl = hit && hit.glyphLevel != null ? hit.glyphLevel : 0;
        var label = gl.name + (gl.level ? "  " + lvl + " / " + gl.level : "");
        return { label: label, done: done, source: done ? "vision" : null };
      });
      paraGroups.push(group("Glyphs", gitems));
    }
    if (paraGroups.length) cats.push(category("paragon", "Paragon & Glyphs", paraGroups));

    var matched = 0, total = 0, under = 0;
    cats.forEach(function (c) { matched += c.matched; total += c.total; under += (c.under || 0); });

    return {
      target: { name: target.name || "Target Build", class: target.class || null, source: target.source || null },
      overall: { matched: matched, total: total, under: under, pct: total ? Math.round((matched / total) * 100) : 0 },
      categories: cats,
    };
  }

  return { normalize: normalize, phraseMatch: phraseMatch, diff: diff };
});
