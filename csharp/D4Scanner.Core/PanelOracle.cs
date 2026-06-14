namespace D4Scanner.Core;

/// <summary>
/// A thread-safe, time-keyed record of which D4 UI panel the OCR channel has recently seen open
/// (Character / Inventory / Stash / Vendor / Paragon). It is the "panel-state oracle" half of the
/// OCR↔TTS sensor fusion: the OCR engine (a screen reader, App layer) calls <see cref="Observe"/>
/// once per scan with the panel it visually detected; the TTS classifier
/// (<see cref="LogWatcher.ClassifyContext"/>) calls <see cref="PanelAt"/> to ask "what panel was open
/// when this item was hovered?" and uses a positive "Character" answer to rescue a worn item whose
/// in-log Character marker aged out of the rolling window.
///
/// Design invariants (each pins a reviewed failure mode):
///  • LOCKED — the writer (OCR timer thread) and reader (LogWatcher poll thread) are genuinely
///    concurrent and <see cref="PanelAt"/> does a multi-step nearest-in-time scan, so all access is
///    guarded by a single lock (the GameDataIcons gate pattern), never volatile/Interlocked.
///  • FAIL-CLOSED — <see cref="PanelAt"/> returns null for a non-positive query tick (unknown hover
///    time) or when nothing sits within <see cref="ToleranceTicks"/>; it NEVER falls back to the
///    most-recent observation (that would let an un-timestamped vendor hover pull the latest panel and
///    re-open the v0.37 vendor-gear leak).
///  • TIGHT — the tolerance is ~one OCR interval so a stray "Character" frame captured well AFTER a
///    vendor hover (panel since changed) can't retroactively rescue it.
///  • BOUNDED — observations self-trim by time (older than 2×tolerance) and a hard count cap, so a
///    long session can't grow the buffer unbounded.
///  • CLEARABLE — <see cref="Clear"/> is called on every gear-wipe boundary (shim re-attach,
///    char-select, log rotation) so a prior session's "Character" window can't keep stale gear worn.
/// Live-only: in one-shot replay (BuildFromLines / DiagnoseLines) the oracle is null, so classification
/// stays fully deterministic.
/// </summary>
public sealed class PanelOracle
{
    readonly object _gate = new();
    readonly List<(string panel, long ticks)> _obs = new();
    const int MaxObs = 32;

    /// <summary>Match window in ticks. ~one OCR scan interval (+slack): an observation farther than this
    /// from a hover is treated as a different panel session and ignored.</summary>
    public long ToleranceTicks { get; }

    public PanelOracle(int toleranceSeconds = 25) => ToleranceTicks = TimeSpan.FromSeconds(toleranceSeconds).Ticks;

    /// <summary>Record that <paramref name="panel"/> was observed open at <paramref name="utcTicks"/>
    /// (the OCR scan time, DateTime.UtcNow.Ticks — the same UTC tick scale as an item's LogTimeUtc).
    /// No-ops on a null/empty panel or a non-positive tick.</summary>
    public void Observe(string? panel, long utcTicks)
    {
        if (string.IsNullOrEmpty(panel) || utcTicks <= 0) return;
        lock (_gate)
        {
            _obs.Add((panel!, utcTicks));
            long cutoff = utcTicks - 2 * ToleranceTicks;            // self-trim to the query window…
            _obs.RemoveAll(o => o.ticks < cutoff);
            if (_obs.Count > MaxObs) _obs.RemoveRange(0, _obs.Count - MaxObs);   // …with a hard ceiling
        }
    }

    /// <summary>The panel observed closest in time to <paramref name="atTicks"/> within
    /// <see cref="ToleranceTicks"/>, or null. Fail-closed: a non-positive tick (unknown hover time) or no
    /// observation within tolerance returns null — never a most-recent fallback. On an exact-distance tie
    /// the most-recently-observed panel wins (recency over insertion order).</summary>
    public string? PanelAt(long atTicks)
    {
        if (atTicks <= 0) return null;
        lock (_gate)
        {
            string? best = null;
            long bestDist = long.MaxValue;
            foreach (var (panel, ticks) in _obs)   // oldest→newest, so '<=' lets a newer equal-distance win
            {
                long dist = ticks >= atTicks ? ticks - atTicks : atTicks - ticks;
                if (dist <= ToleranceTicks && dist <= bestDist) { bestDist = dist; best = panel; }
            }
            return best;
        }
    }

    /// <summary>Drop every observation. Called on gear-wipe boundaries (shim re-attach, char-select,
    /// log rotation) so a prior session's panel state can't keep stale gear classified as worn.</summary>
    public void Clear() { lock (_gate) _obs.Clear(); }

    /// <summary>Current observation count (after self-trimming) — diagnostics / tests.</summary>
    public int Count { get { lock (_gate) return _obs.Count; } }

    static readonly HashSet<string> Anatomy = new(StringComparer.OrdinalIgnoreCase)
        { "Head", "Torso", "Hands", "Legs", "Feet", "Neck", "Main Hand", "Off-Hand" };

    /// <summary>The four core attributes the Character "Stats &amp; Materials" sheet shows as standalone labels.
    /// ≥3 present = the stats sheet (a tooltip affix like "+50 Strength" is one line, never three bare labels
    /// at once). A second, Purveyor-collision-free corroboration when the "Equipment" tab word OCR-garbles.</summary>
    static readonly HashSet<string> StatLabels = new(StringComparer.OrdinalIgnoreCase)
        { "Strength", "Intelligence", "Willpower", "Dexterity" };

    /// <summary>Classify which D4 panel a frame's OCR text lines show — the visual half of the fusion, kept
    /// in Core (pure string logic, no WPF/OCR dependency) so it is headlessly testable. High precision for
    /// the worn-gear case: the Armory loadout screen renders the SAME Equipment/Head/Torso chrome as the
    /// character sheet, so it is excluded FIRST (a browsed loadout must never read as worn). "Character" is
    /// reached by any of: the literal "Equipment" tab (tolerant of an OCR-glued tab glyph), ≥2 distinct
    /// anatomical slot labels, ≥3 core-attribute stats-sheet labels, or a "CHARACTER"-header comparison
    /// tooltip with worn evidence. A lone "Head"/"Torso" stays too weak: the Purveyor of Curiosities gamble
    /// categories include "Head" (the rest are item-type words deliberately NOT in the anatomical set), so
    /// 1 &lt; 2 → not Character. Vendor/Stash/Inventory/Paragon are checked FIRST — including the salvage/cube
    /// crafting stations and the "STAS"/"Edit Tab" stash chrome — so a focused menu's unambiguous markers veto
    /// the passive stats sidebar that co-renders on those screens (the v0.37 vendor-gear-leak class; the 15
    /// real-capture misclassifications these branches were derived from). NOTE (season-volatile): the ≥2
    /// anatomy boundary and the marker word-sets are validated by the Detect regression tests — those are the
    /// tripwires if a future season relabels gamble categories or the OCR garble shifts.</summary>
    public static string? Detect(IEnumerable<string> lines)
    {
        var clean = lines.Select(GearParser.Clean).Where(c => c.Length > 0).ToList();
        bool Has(string s) => clean.Any(c => c.Equals(s, StringComparison.OrdinalIgnoreCase));
        bool HasSub(string s) => clean.Any(c => c.Contains(s, StringComparison.OrdinalIgnoreCase));

        if (HasSub("Armory") || HasSub("Loadout")) return null;   // exclude the Armory's char-sheet-like chrome
        // Stash — tolerant of the observed "STAS" title garble (real frames 23-14-12/15/19) and backed by the
        // stash-exclusive "Edit Tab" control, so a co-rendered stats sidebar can't steal a focused stash to
        // Character. "Stats" does NOT start with "STAS" (4th char differs), so the real stats sheet is safe;
        // the ≤5 cap keeps it off longer words (STASIS-shaped affixes).
        bool stashTitle = Has("Stash")
            || clean.Any(c => c.StartsWith("STAS", StringComparison.OrdinalIgnoreCase) && c.Length <= 5);
        if (stashTitle || Has("Edit Tab")) return "Stash";
        // Vendor — incl. the Blacksmith salvage modal and the Horadric Cube. Anchored on multi-word station
        // phrases ("Salvage All"/"All Junk"/"Horadric Cube"/"Transmute") that never appear in a worn-item
        // action tail (the bare "Blacksmith" title OCR-garbles to "BLACKSITIITH"), so the always-on stats
        // sidebar can't read a focused crafting station as Character (real frames 23-14-05/09, 23-01-19).
        if (HasSub("Obols") || Has("Buyback") || HasSub("Purveyor")
            || HasSub("Salvage All") || HasSub("All Junk") || HasSub("Horadric Cube") || Has("Transmute"))
            return "Vendor";
        if (Has("Paragon")) return "Paragon";
        if (Has("Inventory")) return "Inventory";
        // Character — the "Equipment" tab, tolerant of an OCR-glued tab glyph (e.g. "O) Equipment" → the line
        // ends with "Equipment"; the ≤13 length cap excludes flavor text and "…equipped:").
        if (Has("Equipment")
            || clean.Any(c => c.EndsWith("Equipment", StringComparison.OrdinalIgnoreCase) && c.Length <= 13))
            return "Character";
        if (Anatomy.Count(Has) >= 2) return "Character";
        // …or the stats sheet by ≥3 core-attribute labels (rescues frames whose Equipment tab garbled), …
        if (StatLabels.Count(Has) >= 3) return "Character";
        // …or a comparison/worn tooltip that shows the "CHARACTER" column header (OCR'd "CHARACTER a", hence
        // StartsWith) PLUS worn/comparison evidence but no anatomy grid — the column-split frames the position
        // fusion needs (real frames 23-13-15 helm, 23-13-45 weapons). Requires corroboration so a stray header
        // word can't fire it.
        bool characterHeader = clean.Any(c => c.StartsWith("CHARACTER", StringComparison.OrdinalIgnoreCase));
        bool wornEvidence = Has("EQUIPPED") || Has("Unequip")
                         || HasSub("Hide Comparison") || HasSub("Compare other slot");
        if (characterHeader && wornEvidence) return "Character";
        return null;
    }
}
