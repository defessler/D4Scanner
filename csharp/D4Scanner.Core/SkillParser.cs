using System.Text.RegularExpressions;

namespace D4Scanner.Core;

/// <summary>
/// Captures the player's selected skills/passives and their point ranks from the screen reader. Hovering a
/// skill in the skill tree voices its name on one line followed by "RANK X/Y" (current total rank / base max),
/// e.g. "Concealment" → "RANK 17/15". Unlike paragon nodes, each skill name is paired with its rank, so the
/// ranks dedup cleanly by name (latest wins) and compare directly to the target build's wanted ranks.
/// </summary>
public sealed class SkillParser
{
    readonly Dictionary<string, LiveSkill> _byName = new(StringComparer.OrdinalIgnoreCase);
    string? _prev;   // previous non-empty cleaned line — the candidate skill name
    static readonly Regex RankRe = new(@"^RANK\s+([0-9]{1,3})\s*/\s*([0-9]{1,3})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Captured skills, highest rank first.</summary>
    public List<LiveSkill> Skills => _byName.Values.OrderByDescending(s => s.Rank ?? 0).ToList();
    public bool HasAny => _byName.Count > 0;

    public void Reset() { _byName.Clear(); _prev = null; }

    /// <summary>Feed one raw TTS line. Returns true when a skill rank was captured or changed.</summary>
    public bool Feed(string rawLine)
    {
        var line = GearParser.Clean(rawLine).Trim();
        if (line.Length == 0) return false;

        var m = RankRe.Match(line);
        if (m.Success && _prev is string name && IsSkillName(name) && int.TryParse(m.Groups[1].Value, out var rank))
        {
            _prev = null;
            bool changed = !_byName.TryGetValue(name, out var ex) || ex.Rank != rank;
            _byName[name] = new LiveSkill { Name = name, Rank = rank };
            return changed;
        }
        _prev = line;
        return false;
    }

    // The line directly before "RANK X/Y" is the skill/passive name. Guard against stray prose: a name is a
    // short, digit-free, letter-led phrase (e.g. "Dance of Knives", "Concealment").
    static bool IsSkillName(string s) =>
        s.Length is >= 3 and <= 40 && char.IsLetter(s[0]) && !s.Contains(':') && !s.Any(char.IsDigit) && s.Count(c => c == ' ') <= 4;
}
