using System.Globalization;

namespace D4Scanner.Core;

/// <summary>
/// One affix rolled up across every target slot that wants it — the data behind a single
/// "Gear &amp; Affixes" overview row. Conveys completeness (pieces that have the affix / target
/// pieces) and progress toward the combined goal (summed have vs summed wants when the target
/// magnitudes are known, otherwise blended roll-quality).
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
    public double WantsTotal { get; set; }  // summed target magnitude across target pieces
    public bool WantsKnown { get; set; }    // every target piece had a derivable target magnitude
    public bool WantsEstimated { get; set; } // part of WantsTotal was extrapolated (shown with a "~")

    public string Prefix { get; set; } = ""; // unit hint: "x" / "+" / ""
    public string Suffix { get; set; } = ""; // unit hint: "%" / ""
    public double ProgressPct { get; set; } // 0-100, progress toward the combined goal (drives the bar)

    // accumulators used during aggregation (not for display)
    internal int WantsPieces;               // pieces with a derivable target magnitude
    internal int HaveValuePieces;           // pieces that contributed a numeric magnitude to HaveTotal
    internal double QualitySum;             // summed per-piece quality (met=100, under=roll%, missing=0)

    public string Status => MetPieces >= TargetPieces && TargetPieces > 0 ? "met"
                          : HavePieces > 0 ? "under" : "missing";

    public string Fmt(double v) => Prefix + v.ToString("#,0.##", CultureInfo.InvariantCulture) + Suffix;
}

public static class AffixAggregate
{
    /// <summary>Roll up a gear category's per-slot affix <see cref="ReqItem"/>s into one
    /// <see cref="AffixProgress"/> per distinct affix, ordered by how many slots want it (desc) then name.</summary>
    public static List<AffixProgress> ForGear(Category gear)
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

        if (i.ValueNum is double v) { p.HaveTotal += v; p.HaveAny = true; p.HaveValuePieces++; }

        // unit hint from the first piece that carries one
        if (p.Prefix.Length == 0 && p.Suffix.Length == 0 && (i.IsMultiplier || i.IsPercent || i.ValueNum != null))
        {
            p.Prefix = i.IsMultiplier ? "x" : (i.ValueNum is double vv && vv > 0 ? "+" : "");
            p.Suffix = i.IsPercent ? "%" : "";
        }

        if (i.TargetNum is double t) { p.WantsTotal += t; p.WantsPieces++; }
    }

    static void Finish(AffixProgress p)
    {
        p.WantsKnown = p.TargetPieces > 0 && p.WantsPieces == p.TargetPieces;
        // Pieces with NO derivable target magnitude (build states no min and the piece has no captured
        // roll range) still count toward the goal: estimate each at the average per-piece target when one
        // is known, else at the average of the rolls you already have ("get this on every wanted piece at
        // the roll you've got"). Keeps the value column an honest current/target instead of a bare number.
        if (p.WantsPieces < p.TargetPieces)
        {
            double perPiece = p.WantsPieces > 0 ? p.WantsTotal / p.WantsPieces
                            : p.HaveValuePieces > 0 ? p.HaveTotal / p.HaveValuePieces : 0;
            if (perPiece > 0)
            {
                p.WantsTotal += perPiece * (p.TargetPieces - p.WantsPieces);
                p.WantsEstimated = true;
            }
        }
        // progress prefers the literal have/wants ratio whenever ANY target magnitude is known (a partially
        // known goal still beats no number at all — the piece-count caption carries completeness); falls back
        // to blended roll quality only when no piece has a derivable target.
        if (p.WantsTotal > 0)
            p.ProgressPct = Math.Max(0, Math.Min(100, 100.0 * p.HaveTotal / p.WantsTotal));
        else
            p.ProgressPct = p.TargetPieces > 0 ? Math.Max(0, Math.Min(100, p.QualitySum / p.TargetPieces)) : 0;
    }
}
