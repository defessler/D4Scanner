using System.Text.RegularExpressions;

namespace D4Scanner.Core;

/// <summary>
/// Recognizes PLAYER-NAMEPLATE lines — "Name | Level (Paragon) (Tier)", optionally clan-tagged
/// ("&lt;Muld&gt; Sverren | 70 (211) (VII)"). Ground truth from a real 18MB log: these are voiced for
/// OTHER PLAYERS throughout normal play (~5,300 lines, ~1,100 distinct names), including the player's
/// own world nameplate. They must NEVER feed identity or gear — this parser exists so the pipeline can
/// recognize and skip them. The player's own characters are identified by <see cref="CharSelectParser"/>
/// (the character-select screen, where the class is voiced).
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

/// <summary>Resolves which character is currently active from in-game signals — never from an in-game
/// player's nameplate (the roster passed here is already gated to the character-select screen).</summary>
public static class CharacterResolver
{
    public enum IdKind { None, Resolved, Ambiguous }

    /// <param name="Kind">None = can't tell yet; Resolved = unique character; Ambiguous = multiple share the signal.</param>
    /// <param name="Name">The resolved character name (only when <see cref="IdKind.Resolved"/>).</param>
    /// <param name="Candidates">The colliding names to disambiguate (only when <see cref="IdKind.Ambiguous"/>).</param>
    public readonly record struct CharId(IdKind Kind, string? Name, IReadOnlyList<string> Candidates);

    static readonly CharId NoId = new(IdKind.None, null, System.Array.Empty<string>());

    /// <summary>Resolve the active character by matching the captured paragon against the (gated) roster.
    /// Unique paragon → Resolved; two-or-more sharing it → Ambiguous (caller prompts); none → None.</summary>
    public static CharId Resolve(IReadOnlyList<RosterEntry> roster, int? paragon)
    {
        if (paragon is not int p || roster == null) return NoId;
        var names = roster.Where(e => e.Paragon == p).Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return names.Count switch
        {
            1 => new CharId(IdKind.Resolved, names[0], names),
            > 1 => new CharId(IdKind.Ambiguous, null, names),
            _ => NoId,
        };
    }

    /// <summary>In-game reconciliation using the player's OWN character sheet (never a roster line): the
    /// unique saved profile matching the captured paragon — and class when both are known. Returns null when
    /// zero or more than one profile matches (so a same-paragon/same-class pair never silently mis-binds).</summary>
    public static CharacterProfile? ReconcileOwn(IReadOnlyList<CharacterProfile> profiles, int? paragon, string? cls)
    {
        if (paragon is not int p || profiles == null) return null;
        var m = profiles.Where(pr => pr.Paragon == p &&
                    (cls == null || pr.Class == null || string.Equals(pr.Class, cls, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        return m.Count == 1 ? m[0] : null;
    }

    /// <summary>Back-compat: unique paragon match or null (ambiguous/none).</summary>
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
