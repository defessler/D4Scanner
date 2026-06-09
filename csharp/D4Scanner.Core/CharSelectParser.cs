using System.Text.RegularExpressions;

namespace D4Scanner.Core;

/// <summary>The character the player entered the world with, read directly off the character-select screen.</summary>
public sealed class CharSelectIdentity
{
    public string Name { get; set; } = "";
    public string? Class { get; set; }     // voiced in the detail block; null when the player confirmed without highlighting
    public string? Realm { get; set; }     // "Seasonal" / "Eternal" (+ hardcore variants)
    public int? Paragon { get; set; }      // from "Paragon N"; null for low-level characters voiced as "Level N"
    public int? Level { get; set; }
}

/// <summary>
/// Reads the CHARACTER-SELECT screen from the TTS stream — the only place the player's own characters
/// are voiced with their CLASS. Ground truth (mined from a real 18MB log, Season 8):
///
///   Highlighting a character voices a detail block, in order:
///     HEOKI · Seasonal · Rogue · Paragon 186 · Torment XI · "R Undo Character Delete  D Delete Character …"
///   (low-level: "Level 1" instead of "Paragon N", then an account-paragon "(83)" line). The hotkey footer
///   ("… Undo Character Delete …") is exclusive to this screen and uses U+00A0 separators in the raw log —
///   <see cref="GearParser.Clean"/> normalizes those, so all matching here runs on CLEANED lines.
///
///   List rows voice short runs (Name · Realm · numbers) without a class. "CREATE NEW CHARACTER" voices the
///   bare class names as a list — which is why a class line only counts when PRECEDED by name+realm and
///   FOLLOWED by a "Paragon N"/"Level N" line. Entering the world voices "QUEUED FOR GAME - START GAME
///   PENDING" — the detail block standing at that moment is the character the player went in with.
///
///   The in-game lines "Name | 70 (208) (VII)" are OTHER PLAYERS' nameplates (≈5,300 of them across ~1,100
///   distinct names in the reference log) and must never feed identity — they are not parsed here at all.
/// </summary>
public sealed class CharSelectParser
{
    public static readonly string[] Classes =
        { "Barbarian", "Druid", "Necromancer", "Rogue", "Sorcerer", "Spiritborn", "Paladin", "Warlock" };

    static readonly Regex ReParagon = new(@"^Paragon\s+(\d{1,4})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex ReLevel = new(@"^Level\s+(\d{1,3})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // D4 character names are a single word of letters only (2-16); rejects nameplate/clan-decorated text.
    static readonly Regex ReName = new(@"^[A-Za-z]{2,16}$", RegexOptions.Compiled);

    /// <summary>True when the cleaned line could only be voiced on the character-select screen.</summary>
    public static bool IsCharSelectMarker(string cleaned) =>
        cleaned.Contains("Undo Character Delete", StringComparison.OrdinalIgnoreCase) ||
        cleaned.Equals("CREATE NEW CHARACTER", StringComparison.OrdinalIgnoreCase);

    static bool IsRealm(string s) =>
        s.StartsWith("Seasonal", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("Eternal", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("Hardcore", StringComparison.OrdinalIgnoreCase);

    static string? AsClass(string s)
    {
        foreach (var c in Classes)
            if (s.Equals(c, StringComparison.OrdinalIgnoreCase)) return c;
        return null;
    }

    public static bool IsValidCharName(string? s) => s != null && ReName.IsMatch(s);

    /// <summary>True while the parser believes the character-select screen is showing.</summary>
    public bool InCharSelect { get; private set; }

    /// <summary>Detail block currently highlighted at character-select (full identity incl. class).</summary>
    public CharSelectIdentity? Highlighted { get; private set; }

    /// <summary>Distinct own characters seen this visit (detail blocks; list rows when no block was voiced).</summary>
    public List<CharSelectIdentity> Seen { get; } = new();

    /// <summary>Fires when the character-select screen appears (visit start) — the host should wipe the
    /// prior character's accumulated gear and re-arm identification.</summary>
    public event Action? VisitStarted;

    /// <summary>Fires when the player enters the world; carries the best identity for the character they
    /// picked. Class is set when a detail block was voiced this visit (i.e. the player highlighted it).</summary>
    public event Action<CharSelectIdentity>? Confirmed;

    string _prev1 = "", _prev2 = "";          // last two non-empty cleaned lines
    CharSelectIdentity? _pendingBlock;        // name+realm+class seen, awaiting the Paragon/Level line
    string? _listName; string? _listRealm;    // weaker list-row capture (no class)

    public void Feed(string rawLine)
    {
        var line = GearParser.Clean(rawLine).Trim();
        if (line.Length == 0) return;

        // ---- screen lifecycle ----
        if (IsCharSelectMarker(line))
        {
            if (!InCharSelect)
            {
                InCharSelect = true;
                Highlighted = null; Seen.Clear();
                VisitStarted?.Invoke();
            }
        }
        else if (line.StartsWith("QUEUED FOR GAME", StringComparison.OrdinalIgnoreCase))
        {
            if (InCharSelect)
            {
                InCharSelect = false;
                var id = Highlighted
                      ?? (_listName != null ? new CharSelectIdentity { Name = _listName, Realm = _listRealm } : null);
                if (id != null) Confirmed?.Invoke(id);
            }
            _pendingBlock = null; _listName = null; _listRealm = null;
        }

        // ---- detail-block state machine (only meaningful while at character-select) ----
        if (InCharSelect)
        {
            if (_pendingBlock != null)
            {
                // the line right after the class decides: "Paragon N" / "Level N" completes the block
                var pm = ReParagon.Match(line); var lm = ReLevel.Match(line);
                if (pm.Success || lm.Success)
                {
                    if (pm.Success) _pendingBlock.Paragon = int.Parse(pm.Groups[1].Value);
                    if (lm.Success) _pendingBlock.Level = int.Parse(lm.Groups[1].Value);
                    Highlighted = _pendingBlock;
                    if (!Seen.Any(s => s.Name.Equals(Highlighted.Name, StringComparison.OrdinalIgnoreCase)
                                    && s.Class == Highlighted.Class && s.Realm == Highlighted.Realm))
                        Seen.Add(Highlighted);
                }
                _pendingBlock = null;   // matched or not, the window has passed
            }
            else if (AsClass(line) is string cls && IsRealm(_prev1) && IsValidCharName(_prev2))
            {
                // name → realm → class … (await Paragon/Level to confirm it isn't the create-character list)
                _pendingBlock = new CharSelectIdentity { Name = _prev2, Realm = _prev1, Class = cls };
            }
            else if (IsRealm(line) && IsValidCharName(_prev1))
            {
                // weaker list-row capture: Name / Realm (numbers may follow); used only if no block is voiced
                _listName = _prev1; _listRealm = line;
            }
        }

        _prev2 = _prev1; _prev1 = line;
    }
}
