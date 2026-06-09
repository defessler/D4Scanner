using System.Text.Json;
using System.Text.Json.Serialization;

namespace D4Scanner.Core;

// ---- captured live build (from the TTS log) ----

public enum ItemSource { Tts, Ocr }

public class Affix
{
    public string Text { get; set; } = "";
    public double? Value { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public bool IsPercent { get; set; }
    public bool IsMultiplier { get; set; }
}

public class Item
{
    public string Name { get; set; } = "";
    public string RawName { get; set; } = "";
    public string? Rarity { get; set; }
    public string? ItemType { get; set; }
    public string? Slot { get; set; }
    public bool IsUnique { get; set; }
    public bool IsMythic { get; set; }
    public bool IsAncestral { get; set; }
    public int? ItemPower { get; set; }
    public double? Dps { get; set; }
    public int? MasterworkRank { get; set; }
    public int? MasterworkMax { get; set; }
    public int? TemperUsed { get; set; }
    public int? TemperMax { get; set; }
    public int? Quality { get; set; }    // Season 8 item quality score
    public int? RequiresLevel { get; set; }
    public string? Aspect { get; set; }
    public bool Equipped { get; set; }
    public bool IsComparison { get; set; }   // "Properties lost when equipped:" seen — bag/stash item, NOT worn
    public bool FromCharPanel { get; set; }  // preceded by a character-panel slot header — definitively worn
    public UiContext Context { get; set; }   // classified surface (WornGear / BagItem / etc.)
    public string? UiPanel { get; set; }    // active D4 UI panel when this item was hovered (Character/Stash/Vendor/…)
    public List<Affix> Affixes { get; set; } = new();
    public List<string> PowerText { get; set; } = new();
    /// <summary>Rune codes found in sockets, e.g. ["Neo", "Vex"] from "NeoVex (200/100) - Graceful Heart".</summary>
    public List<string> SocketedRunes { get; set; } = new();
    /// <summary>Runeword name if a complete runeword is active, e.g. "Graceful Heart of the Oak".</summary>
    public string? RunewordName { get; set; }
    /// <summary>Total socket capacity from a bare "Socket (N)" line (comparison/bag view). Null = no socket line seen.</summary>
    public int? SocketCount { get; set; }
    /// <summary>Number of unfilled sockets — one per "Empty Socket" line in the tooltip.</summary>
    public int EmptySockets { get; set; }
    /// <summary>Set name from a Set Charm bonus header, e.g. "Mastery" / "Way of the Blurring Blade".</summary>
    public string? SetName { get; set; }
    /// <summary>Set pieces currently equipped (the "active" in "Mastery (0/2)").</summary>
    public int? SetActive { get; set; }
    /// <summary>Total pieces in the set (the "total" in "Mastery (0/2)").</summary>
    public int? SetTotal { get; set; }
    /// <summary>Positional slot within a multi-slot category (1-based), e.g. 1 or 2 for rings. 0 = unknown.</summary>
    public int SlotPosition { get; set; }
    /// <summary>UTC ticks when this item was last scanned from the TTS log.</summary>
    public long LastScannedTicks { get; set; }
    /// <summary>UTC time parsed from the log line's '[ISO]' prefix (set by the shim) — the TRUE time the
    /// item was hovered in-game, not the time the line was replayed at app launch. Null when the line had
    /// no timestamp prefix (older shim / un-prefixed fixtures); callers fall back to the system clock.</summary>
    public DateTimeOffset? LogTimeUtc { get; set; }
    public ItemSource Source { get; set; }   // Tts (default/zero) or Ocr
}

/// <summary>The D4 UI surface an item was captured from. Derived from TTS context signals.</summary>
public enum UiContext
{
    Unknown,
    WornGear,       // character panel: Unequip action, slot header, or FromCharPanel
    BagItem,        // inventory: Store/Mark as Junk/Salvage action
    StashItem,      // stash: Take action
    VendorItem,     // vendor: Buy + Cost : N Obols
    ParagonNode,    // Unlock/Refund + Normal/Magic/Rare/Legendary Node
    ParagonGlyph,   // Glyph Socket + SOCKETED GLYPH (drops via LooksLikeItem — only for future use)
    Skill,          // RANK n/m + MODIFIERS + Item Contribution
    Charm,          // Set Charm / Unique Charm + Talisman header
    HoradricSeal,   // Horadric Seal type + Unlocks N Charm Slots
    Rune,           // Rune of Invocation/Ritual + In bags: N
}

public class LiveSkill
{
    public string Name { get; set; } = "";
    public int? Rank { get; set; }
    public bool IsKeyPassive { get; set; }
    public bool Slotted { get; set; }
}

public class LiveParagon
{
    public string Board { get; set; } = "";
    public string? Glyph { get; set; }
    public int? GlyphLevel { get; set; }
}

/// <summary>The player's total character attributes + paragon level, captured from the character sheet.
/// These totals already include everything paragon grants (per-node board bonuses can't be summed reliably,
/// but the resulting attribute totals are exact).</summary>
public class LiveCharacter
{
    public int? Strength { get; set; }
    public int? Dexterity { get; set; }
    public int? Intelligence { get; set; }
    public int? Willpower { get; set; }
    public int? ParagonLevel { get; set; }
    [JsonIgnore] public bool Any => Strength.HasValue || Dexterity.HasValue || Intelligence.HasValue || Willpower.HasValue || ParagonLevel.HasValue;
    public LiveCharacter Clone() => (LiveCharacter)MemberwiseClone();
}

public class LiveBuild
{
    public List<Item> Gear { get; set; } = new();
    public List<Item> Inventory { get; set; } = new();   // non-equipped items seen in bags/stash
    public List<string> Aspects { get; set; } = new();
    public List<LiveSkill> Skills { get; set; } = new();
    public List<LiveParagon> Paragon { get; set; } = new();
    public LiveCharacter Character { get; set; } = new();   // total attributes + paragon level (character sheet)
    public string? Mercenary { get; set; }
    /// <summary>Charm items seen in talisman slots.</summary>
    public List<Item> Charms { get; set; } = new();
    /// <summary>Horadric Seal items seen in the seal slot.</summary>
    public List<Item> Seals { get; set; } = new();
    /// <summary>Rune items seen in stash/bags.</summary>
    public List<Item> Runes { get; set; } = new();
    /// <summary>Characters voiced on the character-select screen ("Name | Level (Paragon) (Tier)").
    /// The only place the player's character NAME is exposed — anchors per-character profiles.</summary>
    public List<RosterEntry> Roster { get; set; } = new();
}

/// <summary>One character on the character-select roster, parsed from "Name | Level (Paragon) (Tier)".</summary>
public sealed class RosterEntry
{
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public int Paragon { get; set; }
    public string Tier { get; set; } = "";   // world-tier roman numeral, e.g. "VII"
}

/// <summary>A tracked character: identity + its own saved live loadout. Persisted as profiles/&lt;slug&gt;.json.</summary>
public sealed class CharacterProfile
{
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Class { get; set; }            // best-effort, detected from gear (display only)
    public int? Paragon { get; set; }             // last-seen paragon level (for roster matching/display)
    public long LastSeenUtcTicks { get; set; }
    public LiveBuild Live { get; set; } = new();
    public string? TargetPath { get; set; }       // the target build file this character compares against
    public string? TargetSource { get; set; }     // the import slug/URL that produced it (for re-import / Open on Maxroll)
}

// ---- target build (mirrors schema/target.schema.json) ----

public class TargetBuild
{
    public string Name { get; set; } = "Target Build";
    public string? Class { get; set; }
    public string? Source { get; set; }
    public List<TargetGear> Gear { get; set; } = new();
    public List<TargetUnique> Uniques { get; set; } = new();
    public List<string> Aspects { get; set; } = new();
    public List<TargetSkill> Skills { get; set; } = new();
    public List<string> KeyPassives { get; set; } = new();
    public TargetParagon? Paragon { get; set; }
    public TargetMercenary? Mercenary { get; set; }      // mercenary + reinforcement the build wants (talismans aren't in the planner)
    public double? MinRollPercent { get; set; }  // global default roll-quality threshold (else the app's slider)
    public List<string> Profiles { get; set; } = new();  // all profiles available on the source build
    public string? Profile { get; set; }                 // the profile this target was built from
}

public class TargetGear
{
    public string Slot { get; set; } = "";
    public string? Label { get; set; }
    public List<TargetAffix> Affixes { get; set; } = new();
    public string? Aspect { get; set; }   // legendary aspect the build wants on this slot
    public List<string> Sockets { get; set; } = new();   // wanted gems/runes socketed in this slot
    public long? Image { get; set; }      // Maxroll icon hash (for icon sources keyed by image)
    public string? ItemId { get; set; }   // game item id string (for icon sources keyed by id)
}

/// <summary>A wanted affix, optionally with a value threshold. Deserializes from either a bare
/// string ("Maximum Life") or an object ({"name":"Maximum Life","min":1500} / {"minPercent":80}).</summary>
[JsonConverter(typeof(TargetAffixConverter))]
public class TargetAffix
{
    public string Name { get; set; } = "";
    public double? Min { get; set; }         // absolute minimum rolled value
    public double? MinPercent { get; set; }  // minimum roll as % of the affix's [min..max] range
    public bool Tempered { get; set; }       // a tempered (manually-forged) affix
}

public class TargetAffixConverter : JsonConverter<TargetAffix>
{
    public override TargetAffix Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
    {
        if (reader.TokenType == JsonTokenType.String)
            return new TargetAffix { Name = reader.GetString() ?? "" };
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var a = new TargetAffix();
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                var n = p.Name.ToLowerInvariant();
                if (n == "name" && p.Value.ValueKind == JsonValueKind.String) a.Name = p.Value.GetString() ?? "";
                else if (n == "min" && p.Value.ValueKind == JsonValueKind.Number) a.Min = p.Value.GetDouble();
                else if ((n == "minpercent" || n == "minpct") && p.Value.ValueKind == JsonValueKind.Number) a.MinPercent = p.Value.GetDouble();
                else if (n == "tempered" && (p.Value.ValueKind == JsonValueKind.True || p.Value.ValueKind == JsonValueKind.False)) a.Tempered = p.Value.GetBoolean();
            }
            return a;
        }
        throw new JsonException("affix must be a string or an object");
    }

    public override void Write(Utf8JsonWriter w, TargetAffix v, JsonSerializerOptions o)
    {
        if (v.Min == null && v.MinPercent == null && !v.Tempered) { w.WriteStringValue(v.Name); return; }
        w.WriteStartObject();
        w.WriteString("name", v.Name);
        if (v.Min != null) w.WriteNumber("min", v.Min.Value);
        if (v.MinPercent != null) w.WriteNumber("minPercent", v.MinPercent.Value);
        if (v.Tempered) w.WriteBoolean("tempered", true);
        w.WriteEndObject();
    }
}

public class TargetUnique
{
    public string Name { get; set; } = "";
    public string? Slot { get; set; }
    public bool Mythic { get; set; }
    public long? Image { get; set; }
    public string? ItemId { get; set; }
}

public class TargetSkill
{
    public string Name { get; set; } = "";
    public int? Rank { get; set; }
}

public class TargetMercenary
{
    public string? Main { get; set; }       // the hired mercenary
    public string? Support { get; set; }     // the reinforcement mercenary
    public List<string> SupportSkills { get; set; } = new();
}

public class TargetParagon
{
    public List<string> Boards { get; set; } = new();
    public List<TargetGlyph> Glyphs { get; set; } = new();
}

public class TargetGlyph
{
    public string Name { get; set; } = "";
    public int? Level { get; set; }
}

// ---- diff report (output of DiffEngine, mirrors diff.js) ----

public class ReqItem
{
    public string Label { get; set; } = "";
    public bool Done { get; set; }            // you have this requirement (presence)
    public string Status { get; set; } = "missing"; // "met" | "under" | "missing"
    public string? Source { get; set; }
    public string? Val { get; set; }          // your rolled value (gear)
    public double? RollPct { get; set; }      // roll within the affix's [min..max] range, 0-100
    public string? Need { get; set; }         // threshold description, e.g. "≥ 80%" or "≥ 1500"
    public string? Have { get; set; }         // what you have instead (uniques)
    public bool Tempered { get; set; }        // this affix is a tempered (manually-forged) affix
    public double? ValueNum { get; set; }     // your rolled magnitude (numeric), for summing across pieces
    public double? TargetNum { get; set; }    // the magnitude the build wants on this piece (Min or threshold value); null if not derivable
    public bool IsMultiplier { get; set; }    // affix is an "x" multiplier (unit hint for aggregation)
    public bool IsPercent { get; set; }       // affix value is a percentage (unit hint for aggregation)
}

public class GearLiveItem
{
    public string Name { get; set; } = "";
    public string? Rarity { get; set; }
    public int? ItemPower { get; set; }
    public bool IsUnique { get; set; }
    public bool IsAncestral { get; set; }
    public string? Aspect { get; set; }   // the item's legendary/unique power, if captured
}

public class Group
{
    public string Name { get; set; } = "";
    public List<ReqItem> Items { get; set; } = new();
    public int Matched { get; set; }
    public int Total { get; set; }
    public int Under { get; set; }            // matched but under-rolled
    public string? Kind { get; set; }                 // "gear" => value layout
    public List<GearLiveItem> LiveItems { get; set; } = new();
    public List<string> Extras { get; set; } = new();
    public string? WantAspect { get; set; }           // aspect the build wants in this slot
    public List<string> WantSockets { get; set; } = new();   // gems/runes the build wants socketed here
    public List<string> UpgradeItems { get; set; } = new();  // non-equipped items that beat the equipped one
    /// <summary>Live socket fill summary for this slot, e.g. "1/2 filled (1 empty)"; null when target wants none.</summary>
    public string? SocketStatus { get; set; }
    public bool SocketsDone { get; set; }                    // all wanted sockets filled (no empties / runeword present)
}

public class Category
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<Group> Groups { get; set; } = new();
    public int Matched { get; set; }
    public int Total { get; set; }
    public int Under { get; set; }
    public int Pct { get; set; }
}

public class DiffReport
{
    public string TargetName { get; set; } = "Target Build";
    public string? TargetClass { get; set; }
    public int Matched { get; set; }
    public int Total { get; set; }
    public int Under { get; set; }
    public int Pct { get; set; }
    public List<Category> Categories { get; set; } = new();
}

public static class Json
{
    public static readonly System.Text.Json.JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };
}
