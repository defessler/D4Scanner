using System.Text.Json;
using System.Text.Json.Serialization;

namespace D4Scanner.Core;

// ---- captured live build (from the TTS log) ----

public class Affix
{
    public string Text { get; set; } = "";
    public double? Value { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public bool IsPercent { get; set; }
    public bool IsMultiplier { get; set; }
    public bool IsGreater { get; set; }
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
    public int? RequiresLevel { get; set; }
    public string? Aspect { get; set; }
    public bool Equipped { get; set; }
    public List<Affix> Affixes { get; set; } = new();
    public List<string> PowerText { get; set; } = new();
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

public class LiveBuild
{
    public List<Item> Gear { get; set; } = new();
    public List<string> Aspects { get; set; } = new();
    public List<LiveSkill> Skills { get; set; } = new();
    public List<LiveParagon> Paragon { get; set; } = new();
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
    public double? MinRollPercent { get; set; }  // global default roll-quality threshold (else the app's slider)
    public List<string> Profiles { get; set; } = new();  // all profiles available on the source build
    public string? Profile { get; set; }                 // the profile this target was built from
}

public class TargetGear
{
    public string Slot { get; set; } = "";
    public string? Label { get; set; }
    public List<TargetAffix> Affixes { get; set; } = new();
}

/// <summary>A wanted affix, optionally with a value threshold. Deserializes from either a bare
/// string ("Maximum Life") or an object ({"name":"Maximum Life","min":1500} / {"minPercent":80}).</summary>
[JsonConverter(typeof(TargetAffixConverter))]
public class TargetAffix
{
    public string Name { get; set; } = "";
    public double? Min { get; set; }         // absolute minimum rolled value
    public double? MinPercent { get; set; }  // minimum roll as % of the affix's [min..max] range
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
            }
            return a;
        }
        throw new JsonException("affix must be a string or an object");
    }

    public override void Write(Utf8JsonWriter w, TargetAffix v, JsonSerializerOptions o)
    {
        if (v.Min == null && v.MinPercent == null) { w.WriteStringValue(v.Name); return; }
        w.WriteStartObject();
        w.WriteString("name", v.Name);
        if (v.Min != null) w.WriteNumber("min", v.Min.Value);
        if (v.MinPercent != null) w.WriteNumber("minPercent", v.MinPercent.Value);
        w.WriteEndObject();
    }
}

public class TargetUnique
{
    public string Name { get; set; } = "";
    public string? Slot { get; set; }
    public bool Mythic { get; set; }
}

public class TargetSkill
{
    public string Name { get; set; } = "";
    public int? Rank { get; set; }
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
}

public class GearLiveItem
{
    public string Name { get; set; } = "";
    public string? Rarity { get; set; }
    public int? ItemPower { get; set; }
    public bool IsUnique { get; set; }
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
