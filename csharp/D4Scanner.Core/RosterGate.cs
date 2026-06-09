namespace D4Scanner.Core;

/// <summary>
/// Decides when a run of roster-shaped lines is REALLY the character-select screen, versus a single
/// other-player nameplate the screen reader voices in the same "Name | Level (Paragon) (Tier)" shape
/// while you are in town. Character-select lists your characters, so it yields a CONTIGUOUS block of
/// &gt;= <see cref="Threshold"/> distinct roster lines; a passer-by yields a lone line surrounded by
/// gameplay. Requiring two contiguous roster lines before honoring them means an in-game player can
/// never be picked as a character or trigger the gear reset.
///
/// Limitation: a single-character account voices only one roster line, so this gate won't open for it
/// — that case needs the shim to emit an explicit char-select screen marker (tracked separately).
/// </summary>
public sealed class RosterGate
{
    public const int Threshold = 2;

    readonly List<string> _pending = new();   // roster lines seen but not yet confirmed as char-select
    int _contig;

    /// <param name="Matched">The line was roster-shaped — the caller must NOT parse it as gear.</param>
    /// <param name="Commit">Raw roster lines to record into the roster now (empty while still buffering).</param>
    /// <param name="EnteredCharSelect">This line crossed the threshold — the caller should wipe the prior
    /// character's accumulated gear and signal a character switch (fires once per contiguous block).</param>
    public readonly record struct Result(bool Matched, IReadOnlyList<string> Commit, bool EnteredCharSelect);

    static readonly Result Ignored = new(false, System.Array.Empty<string>(), false);

    /// <summary>Feed one raw log line and learn how to treat it.</summary>
    public Result Feed(string rawLine)
    {
        if (RosterParser.ParseLine(rawLine) != null)
        {
            _contig++;
            if (_contig < Threshold) { _pending.Add(rawLine); return new Result(true, System.Array.Empty<string>(), false); }
            if (_contig == Threshold)
            {
                var commit = new List<string>(_pending) { rawLine };
                _pending.Clear();
                return new Result(true, commit, true);
            }
            return new Result(true, new[] { rawLine }, false);   // already in the confirmed block — keep recording
        }

        // A non-blank, non-roster line ends the contiguous block (blank lines between entries are tolerated).
        if (GearParser.Clean(rawLine).Trim().Length > 0) { _contig = 0; _pending.Clear(); }
        return Ignored;
    }
}
