using System.Text.RegularExpressions;

namespace D4Scanner.Core;

/// <summary>
/// Captures the player's total character attributes (Strength / Dexterity / Intelligence / Willpower) and
/// Paragon level from the screen-reader stream. The character sheet voices each attribute as a bare label
/// line ("Dexterity") immediately followed by its value line ("1,501"); the paragon level voices as
/// "Paragon 186". These totals already include everything Paragon grants — the per-node board bonuses can't
/// be summed reliably (the reader gives no node identity and re-announces the same node on every pass), but
/// the resulting attribute totals are exact.
/// </summary>
public sealed class CharacterParser
{
    public LiveCharacter Character { get; private set; } = new();
    string? _pending;   // attribute key awaiting its value on the next non-empty line

    static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Strength"] = "str", ["Dexterity"] = "dex", ["Intelligence"] = "int", ["Willpower"] = "wil",
    };
    static readonly Regex ParagonRe = new(@"^Paragon\s+([0-9]{1,4})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex ValueRe = new(@"^[0-9]{1,3}(?:,[0-9]{3})*$|^[0-9]+$", RegexOptions.Compiled);

    public void Reset() { Character = new(); _pending = null; }

    /// <summary>Feed one raw TTS line. Returns true when the captured character data changed.</summary>
    public bool Feed(string rawLine)
    {
        var line = GearParser.Clean(rawLine).Trim();
        if (line.Length == 0) return false;   // tolerate blank lines between a label and its value

        // Paragon level: "Paragon 186" / "PARAGON 186"
        var pm = ParagonRe.Match(line);
        if (pm.Success && int.TryParse(pm.Groups[1].Value, out var lvl) && lvl is > 0 and < 2000)
        {
            _pending = null;
            if (Character.ParagonLevel == lvl) return false;
            Character.ParagonLevel = lvl; return true;
        }

        // A pure number directly after an attribute label is that attribute's total.
        if (_pending is string attr)
        {
            _pending = null;
            if (ValueRe.IsMatch(line) && int.TryParse(line.Replace(",", ""), out var val) && val is > 0 and < 100000)
            {
                if (Get(attr) == val) return false;
                Set(attr, val); return true;
            }
            // value didn't follow — fall through (this line might itself be a new label, handled below)
        }

        if (Labels.TryGetValue(line, out var key)) _pending = key;
        return false;
    }

    int? Get(string k) => k switch
    {
        "str" => Character.Strength, "dex" => Character.Dexterity,
        "int" => Character.Intelligence, "wil" => Character.Willpower, _ => null,
    };
    void Set(string k, int v)
    {
        switch (k)
        {
            case "str": Character.Strength = v; break;
            case "dex": Character.Dexterity = v; break;
            case "int": Character.Intelligence = v; break;
            case "wil": Character.Willpower = v; break;
        }
    }
}
