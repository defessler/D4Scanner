using System.Text.Json;
using System.Text.RegularExpressions;

namespace D4Scanner.Core;

/// <summary>
/// Per-character persistence: one <c>profiles/&lt;slug&gt;.json</c> per tracked character plus an
/// <c>active.json</c> pointer to the current one. UI-free I/O over a supplied root directory, so it
/// is headlessly testable. Each profile owns its own <see cref="LiveBuild"/> — switching characters
/// swaps which loadout is active, so a Rogue and a Barbarian never bleed together.
/// </summary>
public sealed class ProfileStore
{
    readonly string _root;

    public ProfileStore(string root)
    {
        _root = root;
        try { Directory.CreateDirectory(_root); } catch { }
    }

    /// <summary>Filesystem-safe identity for a character name (lowercase, alnum + dashes).</summary>
    public static string Slugify(string? name)
    {
        var s = new string((name ?? "").Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        s = Regex.Replace(s, "-+", "-").Trim('-');
        return s.Length == 0 ? "unknown" : s;
    }

    string PathFor(string slug) => Path.Combine(_root, slug + ".json");
    string ActivePath => Path.Combine(_root, "active.json");

    CharacterProfile? LoadFile(string path)
    {
        try { return JsonSerializer.Deserialize<CharacterProfile>(File.ReadAllText(path), Json.Opts); }
        catch { return null; }
    }

    /// <summary>All saved profiles, most-recently-seen first.</summary>
    public List<CharacterProfile> All()
    {
        try
        {
            return Directory.EnumerateFiles(_root, "*.json")
                .Where(f => !string.Equals(Path.GetFileName(f), "active.json", StringComparison.OrdinalIgnoreCase))
                .Select(LoadFile).Where(p => p != null).Cast<CharacterProfile>()
                .OrderByDescending(p => p.LastSeenUtcTicks).ToList();
        }
        catch { return new(); }
    }

    public CharacterProfile? Get(string? slug)
    {
        if (string.IsNullOrEmpty(slug)) return null;
        var p = PathFor(slug);
        return File.Exists(p) ? LoadFile(p) : null;
    }

    public void Save(CharacterProfile profile)
    {
        if (string.IsNullOrEmpty(profile.Slug)) profile.Slug = Slugify(profile.Name);
        try { File.WriteAllText(PathFor(profile.Slug), JsonSerializer.Serialize(profile, Json.Opts)); }
        catch { }
    }

    public string? ActiveSlug
    {
        get
        {
            try { return File.Exists(ActivePath) ? JsonSerializer.Deserialize<ActiveRef>(File.ReadAllText(ActivePath), Json.Opts)?.Slug : null; }
            catch { return null; }
        }
        set
        {
            try { File.WriteAllText(ActivePath, JsonSerializer.Serialize(new ActiveRef { Slug = value }, Json.Opts)); }
            catch { }
        }
    }

    sealed class ActiveRef { public string? Slug { get; set; } }

    /// <summary>One-time migration: when no profiles exist yet but a legacy <paramref name="legacyLivePath"/>
    /// (live.json) does, import it into a default profile and make it active — so the user's current
    /// loadout survives the upgrade. Returns the migrated profile, or null if nothing to migrate.</summary>
    public CharacterProfile? MigrateLegacy(string legacyLivePath, string defaultName = "My Character")
    {
        if (All().Count > 0 || !File.Exists(legacyLivePath)) return null;
        try
        {
            var lb = JsonSerializer.Deserialize<LiveBuild>(File.ReadAllText(legacyLivePath), Json.Opts) ?? new();
            var prof = new CharacterProfile
            {
                Slug = Slugify(defaultName), Name = defaultName, Live = lb,
                Paragon = lb.Character?.ParagonLevel, LastSeenUtcTicks = DateTime.UtcNow.Ticks,
            };
            Save(prof);
            ActiveSlug = prof.Slug;
            return prof;
        }
        catch { return null; }
    }
}

/// <summary>Best-effort character class inference from equipped gear, for display only (the roster gives
/// the name but not the class). Uses class-locked weapon types; returns null when nothing is conclusive.</summary>
public static class ClassDetector
{
    static readonly (string kw, string cls)[] WeaponClass =
    {
        ("crossbow", "Rogue"), ("bow", "Rogue"),
        ("wand", "Sorcerer"), ("staff", "Sorcerer"),
        ("scythe", "Necromancer"),
        ("glaive", "Spiritborn"), ("quarterstaff", "Spiritborn"),
    };

    public static string? FromGear(LiveBuild? live)
    {
        if (live == null) return null;
        foreach (var it in live.Gear)
        {
            var hay = ((it.ItemType ?? "") + " " + (it.Slot ?? "")).ToLowerInvariant();
            foreach (var (kw, cls) in WeaponClass)
                if (hay.Contains(kw)) return cls;
        }
        return null;
    }
}
