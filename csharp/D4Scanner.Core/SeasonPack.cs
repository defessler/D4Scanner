using System.Text.Json;
using System.Text.Json.Serialization;

namespace D4Scanner.Core;

/// <summary>
/// The season-volatile guidance data — activity copy, Infernal Hordes spoils, the boss ladder, Torment
/// tier gates, masterwork/socket/glyph constants — that the guidance engine reads instead
/// of hard-coding. Diablo IV's itemization changes every season and expansion; keeping these tables in one
/// versioned JSON (embedded, with a user-local override) means a season update is a data edit, not a code
/// change, and the loaded <see cref="SeasonLabel"/> can be surfaced as an in-app staleness stamp.
///
/// Load order: <c>%LOCALAPPDATA%\d4scanner\season_pack.json</c> (offline hot-fix) wins over the embedded
/// <c>Assets/season_pack.json</c>. Any failure falls back to the embedded copy, then to a minimal built-in
/// default — <see cref="Current"/> never throws and never returns null.
/// </summary>
public sealed class SeasonPack
{
    public int Season { get; set; }
    public string SeasonName { get; set; } = "";
    public string Expansion { get; set; } = "";
    public string Patch { get; set; } = "";
    public string VerifiedUtc { get; set; } = "";

    public Dictionary<string, ActivityCopy> Activities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<HordesSpoil> HordesSpoils { get; set; } = new();
    public BossLadderData BossLadder { get; set; } = new();
    public int AncestralFloorIP { get; set; } = 900;   // endgame IP floor — a sub-floor non-Ancestral item is temporary (centralized here; doc flags hard-coded IP caps as the #1 staleness tripwire)
    public List<TormentGate> TormentGates { get; set; } = new();
    public List<PitTorment> PitToTorment { get; set; } = new();
    public MasterworkConstants Masterwork { get; set; } = new();
    public Dictionary<string, int> SocketCapacity { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public GlyphConstants Glyph { get; set; } = new();

    public sealed class ActivityCopy { public string Title { get; set; } = ""; public string Detail { get; set; } = ""; }
    public sealed class HordesSpoil { public string Name { get; set; } = ""; public int Aether { get; set; } public string Yields { get; set; } = ""; public string For { get; set; } = ""; }
    public sealed class BossTier { public string? Key { get; set; } public List<string> Bosses { get; set; } = new(); }
    public sealed class UniversalBoss { public string Name { get; set; } = ""; public string Cost { get; set; } = ""; public string Note { get; set; } = ""; }
    public sealed class BossLadderData { public BossTier Initiate { get; set; } = new(); public BossTier Greater { get; set; } = new(); public BossTier Exalted { get; set; } = new(); public UniversalBoss Universal { get; set; } = new(); }
    public sealed class TormentGate { public int Tier { get; set; } public string Unlocks { get; set; } = ""; }
    public sealed class PitTorment { public int Torment { get; set; } public int Pit { get; set; } }
    public sealed class MasterworkConstants { public int QualityCap { get; set; } = 25; public double ObduciteBase { get; set; } = 10; public double ObduciteSlope { get; set; } = 3.75; public int TwoHandMultiplier { get; set; } = 2; }
    public sealed class GlyphConstants { public int LevelCap { get; set; } = 50; public int LegendaryUpgrade { get; set; } = 45; public int RadiusBump { get; set; } = 15; public string Note { get; set; } = ""; }

    // ---- loading ----

    static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    static SeasonPack? _current;
    /// <summary>The active season pack — lazy-loaded once, never null. Override file beats embedded copy.</summary>
    public static SeasonPack Current => _current ??= Load();

    /// <summary>Path to the optional user-local override (lets a season's data be corrected without a release).</summary>
    public static string OverridePath => Path.Combine(AppPaths.Root, "season_pack.json");

    static SeasonPack Load()
    {
        // user override first (offline hot-fix), then the embedded resource, then a minimal built-in default
        try { if (File.Exists(OverridePath)) return Parse(File.ReadAllText(OverridePath)); } catch { }
        try
        {
            var asm = typeof(SeasonPack).Assembly;
            var res = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("season_pack.json", StringComparison.OrdinalIgnoreCase));
            if (res != null) { using var s = asm.GetManifestResourceStream(res)!; using var rdr = new StreamReader(s); return Parse(rdr.ReadToEnd()); }
        }
        catch { }
        return Fallback();
    }

    static SeasonPack Parse(string json) => JsonSerializer.Deserialize<SeasonPack>(json, Opts) ?? Fallback();

    /// <summary>For tests: parse a pack from a string without touching disk or the embedded copy.</summary>
    public static SeasonPack FromJson(string json) => Parse(json);

    static SeasonPack Fallback() => new()
    {
        Season = 0, SeasonName = "unknown", Patch = "?", VerifiedUtc = "?",
    };

    // ---- typed accessors ----

    /// <summary>"Season 13 · Lord of Hatred · verified 2026-06-10" — the in-app staleness stamp.</summary>
    public string SeasonLabel =>
        (Season > 0 ? $"Season {Season}" : SeasonName) +
        (string.IsNullOrEmpty(Expansion) ? "" : "  ·  " + Expansion) +
        (string.IsNullOrEmpty(VerifiedUtc) ? "" : "  ·  verified " + VerifiedUtc);

    /// <summary>Activity copy for a need-key (uniques/aspects/affixes/temper/sockets/masterwork/glyphs/
    /// greaterAffixes/currency/warPlans); returns a safe placeholder if the key is unknown.</summary>
    public ActivityCopy Activity(string key) =>
        Activities.TryGetValue(key, out var a) ? a : new ActivityCopy { Title = key, Detail = "" };

    /// <summary>The Infernal Hordes spoil tagged for a purpose (gear/materials/gold/bartuc), else the first.</summary>
    public HordesSpoil Spoil(string forKey) =>
        HordesSpoils.FirstOrDefault(s => string.Equals(s.For, forKey, StringComparison.OrdinalIgnoreCase))
        ?? HordesSpoils.FirstOrDefault() ?? new HordesSpoil { Name = "Spoils of Materials" };

    /// <summary>Estimated Obducite to take an item from its current Quality to the cap, ×2 for two-handers.
    /// Each upgrade costs floor(slope·Q + base) and grants ~3.5 Quality on average.</summary>
    public int ObduciteToCap(int currentQuality, bool twoHanded = false)
    {
        double total = 0; double q = Math.Max(0, currentQuality);
        const double avgStep = 3.5;
        while (q < Masterwork.QualityCap)
        {
            total += Math.Floor(Masterwork.ObduciteSlope * q + Masterwork.ObduciteBase);
            q += avgStep;
        }
        return (int)Math.Round(total) * (twoHanded ? Masterwork.TwoHandMultiplier : 1);
    }

    /// <summary>Max sockets for a slot base name (helm/chest/pants/weapon/offhand/amulet/ring/gloves/boots),
    /// 0 when unknown or socketless.</summary>
    public int SocketsFor(string slotBase) => SocketCapacity.TryGetValue(slotBase ?? "", out var n) ? n : 0;

    /// <summary>The Pit tier that guarantees a Torment tier's content (T1≈Pit10 … T12≈Pit100).</summary>
    public int? PitForTorment(int torment) => PitToTorment.FirstOrDefault(p => p.Torment == torment)?.Pit;
}
