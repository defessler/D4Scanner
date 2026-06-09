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

    public void Delete(string slug)
    {
        try { var p = PathFor(slug); if (File.Exists(p)) File.Delete(p); } catch { }
        if (ActiveSlug == slug) ActiveSlug = null;
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

/// <summary>Best-effort character class inference from a captured loadout, for display + as part of the
/// profile key. The roster gives the name but not the class, so we infer it from class-locked WEAPON types
/// and, crucially, class-locked SKILL names — Barbarian (and Druid/Necromancer) share weapon types with
/// other classes, so weapons alone can never identify them; their slotted skills can. Returns null when
/// nothing is conclusive (caller keeps retrying as more of the loadout streams in).</summary>
public static class ClassDetector
{
    static readonly (string kw, string cls)[] WeaponClass =
    {
        ("crossbow", "Rogue"), ("bow", "Rogue"),
        ("wand", "Sorcerer"), ("staff", "Sorcerer"),
        ("scythe", "Necromancer"),
        ("glaive", "Spiritborn"), ("quarterstaff", "Spiritborn"),
    };

    // Class-locked skill names (D4 skill names are class-exclusive). Weapons are checked first; skills
    // catch the classes with no class-locked weapon (Barbarian especially).
    static readonly Dictionary<string, string> SkillClass = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Whirlwind"] = "Barbarian", ["Hammer of the Ancients"] = "Barbarian", ["Death Blow"] = "Barbarian",
        ["Rupture"] = "Barbarian", ["Upheaval"] = "Barbarian", ["Double Swing"] = "Barbarian", ["Bash"] = "Barbarian",
        ["Flay"] = "Barbarian", ["Frenzy"] = "Barbarian", ["Rend"] = "Barbarian", ["Steel Grasp"] = "Barbarian",
        ["Iron Maelstrom"] = "Barbarian", ["Call of the Ancients"] = "Barbarian", ["Wrath of the Berserker"] = "Barbarian",
        ["War Cry"] = "Barbarian", ["Challenging Shout"] = "Barbarian", ["Rallying Cry"] = "Barbarian", ["Mighty Throw"] = "Barbarian",

        ["Heartseeker"] = "Rogue", ["Puncture"] = "Rogue", ["Forceful Arrow"] = "Rogue", ["Invigorating Strike"] = "Rogue",
        ["Barrage"] = "Rogue", ["Penetrating Shot"] = "Rogue", ["Rapid Fire"] = "Rogue", ["Twisting Blades"] = "Rogue",
        ["Flurry"] = "Rogue", ["Shadow Step"] = "Rogue", ["Dash"] = "Rogue", ["Caltrops"] = "Rogue", ["Smoke Grenade"] = "Rogue",
        ["Dark Shroud"] = "Rogue", ["Concealment"] = "Rogue", ["Rain of Arrows"] = "Rogue", ["Death Trap"] = "Rogue",
        ["Shadow Clone"] = "Rogue", ["Dance of Knives"] = "Rogue",

        ["Fireball"] = "Sorcerer", ["Frozen Orb"] = "Sorcerer", ["Ball Lightning"] = "Sorcerer", ["Chain Lightning"] = "Sorcerer",
        ["Arc Lash"] = "Sorcerer", ["Frost Bolt"] = "Sorcerer", ["Incinerate"] = "Sorcerer", ["Ice Shards"] = "Sorcerer",
        ["Charged Bolts"] = "Sorcerer", ["Teleport"] = "Sorcerer", ["Hydra"] = "Sorcerer", ["Blizzard"] = "Sorcerer",
        ["Meteor"] = "Sorcerer", ["Firewall"] = "Sorcerer", ["Deep Freeze"] = "Sorcerer", ["Unstable Currents"] = "Sorcerer",
        ["Lightning Spear"] = "Sorcerer", ["Frost Nova"] = "Sorcerer", ["Flame Shield"] = "Sorcerer",

        ["Bone Spear"] = "Necromancer", ["Blood Lance"] = "Necromancer", ["Sever"] = "Necromancer", ["Bone Spirit"] = "Necromancer",
        ["Corpse Explosion"] = "Necromancer", ["Corpse Tendrils"] = "Necromancer", ["Bone Prison"] = "Necromancer",
        ["Blood Mist"] = "Necromancer", ["Bone Storm"] = "Necromancer", ["Army of the Dead"] = "Necromancer", ["Blood Wave"] = "Necromancer",
        ["Raise Skeleton"] = "Necromancer", ["Decompose"] = "Necromancer", ["Hemorrhage"] = "Necromancer", ["Bone Splinters"] = "Necromancer",

        ["Pulverize"] = "Druid", ["Shred"] = "Druid", ["Landslide"] = "Druid", ["Tornado"] = "Druid", ["Lightning Storm"] = "Druid",
        ["Earth Spike"] = "Druid", ["Wind Shear"] = "Druid", ["Storm Strike"] = "Druid", ["Maul"] = "Druid",
        ["Boulder"] = "Druid", ["Trample"] = "Druid", ["Cyclone Armor"] = "Druid", ["Grizzly Rage"] = "Druid",
        ["Hurricane"] = "Druid", ["Rabies"] = "Druid", ["Poison Creeper"] = "Druid", ["Cataclysm"] = "Druid",

        ["Quill Volley"] = "Spiritborn", ["The Hunter"] = "Spiritborn", ["Soar"] = "Spiritborn", ["Crushing Hand"] = "Spiritborn",
        ["Rock Splitter"] = "Spiritborn", ["Withering Fist"] = "Spiritborn", ["Thrash"] = "Spiritborn", ["Stinger"] = "Spiritborn",
        ["Touch of Death"] = "Spiritborn", ["Armored Hide"] = "Spiritborn", ["Counterattack"] = "Spiritborn",
        ["Concussive Stomp"] = "Spiritborn", ["Scourge"] = "Spiritborn", ["Ravager"] = "Spiritborn",
    };

    public static string? Detect(LiveBuild? live)
    {
        if (live == null) return null;
        // 1) class-locked weapon type
        foreach (var it in live.Gear)
        {
            var hay = ((it.ItemType ?? "") + " " + (it.Slot ?? "")).ToLowerInvariant();
            foreach (var (kw, cls) in WeaponClass)
                if (hay.Contains(kw)) return cls;
        }
        // 2) class-locked slotted skill (the only signal for Barbarian, which has no class-locked weapon)
        foreach (var sk in live.Skills)
            if (sk.Name is { Length: > 0 } n && SkillClass.TryGetValue(n.Trim(), out var c)) return c;
        return null;
    }
}
