using System.Text.RegularExpressions;

namespace D4Scanner.Core;

/// <summary>
/// Parses the character-select roster the screen reader voices as "Name | Level (Paragon) (Tier)"
/// — e.g. "MementoMori | 70 (220) (VII)". This is the ONLY place the player's character name is
/// exposed in the TTS stream, so it anchors per-character profile separation.
/// </summary>
public sealed class RosterParser
{
    // Name | Level (Paragon) (Tier)   — tier content carries no parens
    static readonly Regex Re = new(
        @"^(?<name>.+?)\s*\|\s*(?<lvl>\d{1,3})\s*\(\s*(?<para>\d{1,4})\s*\)\s*\(\s*(?<tier>[^()]*?)\s*\)\s*$",
        RegexOptions.Compiled);

    readonly Dictionary<string, RosterEntry> _byName = new(StringComparer.OrdinalIgnoreCase);
    readonly List<string> _order = new();

    /// <summary>Roster captured so far, in first-seen order.</summary>
    public List<RosterEntry> Entries => _order.Select(n => _byName[n]).ToList();

    /// <summary>Parse a single line into a roster entry, or null if it isn't one.</summary>
    public static RosterEntry? ParseLine(string rawLine)
    {
        var line = GearParser.Clean(rawLine).Trim();
        var m = Re.Match(line);
        if (!m.Success) return null;
        var name = m.Groups["name"].Value.Trim();
        if (name.Length == 0) return null;
        return new RosterEntry
        {
            Name = name,
            Level = int.Parse(m.Groups["lvl"].Value),
            Paragon = int.Parse(m.Groups["para"].Value),
            Tier = m.Groups["tier"].Value.Trim(),
        };
    }

    /// <summary>Feed a raw line. Returns the entry (and records/updates it) when the line is a roster
    /// line, else null. A re-voiced character updates its level/paragon in place.</summary>
    public RosterEntry? Feed(string rawLine)
    {
        var e = ParseLine(rawLine);
        if (e == null) return null;
        if (!_byName.ContainsKey(e.Name)) _order.Add(e.Name);
        _byName[e.Name] = e;
        return e;
    }
}

/// <summary>Resolves which roster character is currently active from in-game signals.</summary>
public static class CharacterResolver
{
    /// <summary>Resolve the active character from a captured paragon level: an exact paragon match wins.
    /// Returns null when no entry matches or two share the paragon (ambiguous → caller falls back to manual).</summary>
    public static RosterEntry? ByParagon(IReadOnlyList<RosterEntry> roster, int? paragon)
    {
        if (paragon is not int p || roster == null) return null;
        RosterEntry? hit = null;
        foreach (var e in roster)
            if (e.Paragon == p)
            {
                if (hit != null) return null;   // ambiguous
                hit = e;
            }
        return hit;
    }
}
