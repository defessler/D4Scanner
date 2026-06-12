using System.Globalization;

namespace D4Scanner.Core;

/// <summary>
/// One affix rolled up across every target slot that wants it — the data behind a single
/// "Gear &amp; Affixes" overview row. Conveys completeness (pieces that have the affix / target
/// pieces) and progress toward the PERFECT combined goal: every wanted piece at the affix's MAX roll.
/// The target is always the max-roll ceiling (read from the captured [min..max] range), so the value
/// column reads current vs a real "perfect" number — never an estimate.
/// </summary>
public sealed class AffixProgress
{
    public string Name { get; set; } = "";
    public string CountNoun { get; set; } = "pieces";   // "pieces" for affixes, "sockets" for the socket roll-up

    public int HavePieces { get; set; }     // pieces that have the affix present (met or under-rolled)
    public int MetPieces { get; set; }      // pieces present AND meeting the build's threshold
    public int UnderPieces { get; set; }    // present but under-rolled
    public int TargetPieces { get; set; }   // total slots the build wants this affix on

    public double HaveTotal { get; set; }   // summed actual magnitude across pieces that have it
    public bool HaveAny { get; set; }        // any actual magnitudes were summed
    public double WantsTotal { get; set; }  // PERFECT goal = max roll × target pieces (0 when no range captured)
    public bool WantsKnown { get; set; }    // a max roll was captured, so the perfect target is real

    public string Prefix { get; set; } = ""; // unit hint: "x" / "+" / ""
    public string Suffix { get; set; } = ""; // unit hint: "%" / ""
    public double ProgressPct { get; set; } // 0-100, progress toward the combined goal (drives the bar)

    // accumulators used during aggregation (not for display)
    internal double MaxRoll;                 // the affix's max-roll ceiling (representative; ~constant per affix)
    internal double QualitySum;              // summed per-piece quality (met=100, under=roll%, missing=0)

    public string Status => MetPieces >= TargetPieces && TargetPieces > 0 ? "met"
                          : HavePieces > 0 ? "under" : "missing";

    public string Fmt(double v) => Prefix + v.ToString("#,0.##", CultureInfo.InvariantCulture) + Suffix;
}

public static class AffixAggregate
{
    /// <summary>Roll up a gear category's per-slot affix <see cref="ReqItem"/>s into one
    /// <see cref="AffixProgress"/> per distinct affix, ordered by how many slots want it (desc) then name.
    /// When <paramref name="owned"/> is supplied, the max-roll ceiling is harvested from ANY owned copy of the
    /// affix (best captured range max, else the best value seen) — so an affix whose equipped piece voiced no
    /// range still gets a real "perfect" target from another copy in the bags or on another character.</summary>
    public static List<AffixProgress> ForGear(Category gear, IEnumerable<Item>? owned = null)
    {
        var by = new Dictionary<string, AffixProgress>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var g in gear.Groups)
            foreach (var i in g.Items)
            {
                var key = (i.Label ?? "").Trim();
                if (key.Length == 0) continue;
                if (!by.TryGetValue(key, out var p)) { p = new AffixProgress { Name = key }; by[key] = p; order.Add(key); }
                Accumulate(p, i);
            }
        // harvest a max-roll ceiling from any owned copy of each affix (range max preferred, else best value)
        if (owned != null)
        {
            var allAffixes = owned.SelectMany(it => it.Affixes ?? new()).ToList();
            foreach (var p in by.Values)
                foreach (var a in allAffixes)
                    if (DiffEngine.PhraseMatch(p.Name, a.Text))
                        p.MaxRoll = Math.Max(p.MaxRoll, a.Max ?? a.Value ?? 0);
        }
        foreach (var key in order) Finish(by[key]);
        return order
            .OrderByDescending(k => by[k].TargetPieces)
            .ThenBy(k => k, StringComparer.OrdinalIgnoreCase)
            .Select(k => by[k])
            .ToList();
    }

    static void Accumulate(AffixProgress p, ReqItem i)
    {
        p.TargetPieces++;

        double quality;
        if (i.Status == "met") { p.MetPieces++; p.HavePieces++; quality = 100; }
        else if (i.Status == "under") { p.UnderPieces++; p.HavePieces++; quality = i.RollPct ?? 50; }
        else quality = 0;   // missing
        p.QualitySum += Math.Max(0, Math.Min(100, quality));

        if (i.ValueNum is double v) { p.HaveTotal += v; p.HaveAny = true; }

        // unit hint from the first piece that carries one
        if (p.Prefix.Length == 0 && p.Suffix.Length == 0 && (i.IsMultiplier || i.IsPercent || i.ValueNum != null))
        {
            p.Prefix = i.IsMultiplier ? "x" : (i.ValueNum is double vv && vv > 0 ? "+" : "");
            p.Suffix = i.IsPercent ? "%" : "";
        }

        // the affix's max-roll ceiling — the same for a given affix, so any piece that voiced a range gives it
        if (i.MaxNum is double m && m > 0) p.MaxRoll = Math.Max(p.MaxRoll, m);
    }

    static void Finish(AffixProgress p)
    {
        // PERFECT combined goal: every wanted piece at the affix's max roll. The max is a real captured
        // number (the [min..max] ceiling), so the target is exact whenever any owned piece voiced a range.
        if (p.MaxRoll > 0)
        {
            p.WantsTotal = p.MaxRoll * p.TargetPieces;
            p.WantsKnown = true;
            p.ProgressPct = Math.Max(0, Math.Min(100, 100.0 * p.HaveTotal / p.WantsTotal));
        }
        else
        {
            // no range captured for any piece (no Advanced Tooltip, or the affix isn't owned anywhere):
            // we can't show a max target, so the bar falls back to blended roll quality.
            p.WantsTotal = 0;
            p.ProgressPct = p.TargetPieces > 0 ? Math.Max(0, Math.Min(100, p.QualitySum / p.TargetPieces)) : 0;
        }
    }
}
