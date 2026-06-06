using D4Scanner.Core;

// Dependency-free assertions over the Core diff/guide logic. Locks in the behavior the WPF app and CLI
// rely on (the JS side already has tracker/diff.test.js; this is the C# Core's regression net).

int passed = 0, failed = 0;
var failures = new List<string>();

void Check(string name, bool cond)
{
    if (cond) passed++;
    else { failed++; failures.Add(name); }
}
void Eq<T>(string name, T expected, T actual) =>
    Check($"{name}  (expected {expected}, got {actual})", EqualityComparer<T>.Default.Equals(expected, actual));

// ---- Normalize ----
Eq("Normalize lowercases + strips punctuation", "maximum life", DiffEngine.Normalize("Maximum Life!!"));
Eq("Normalize collapses whitespace", "foo bar", DiffEngine.Normalize("  Foo   Bar  "));
Eq("Normalize null -> empty", "", DiffEngine.Normalize(null));

// ---- PhraseMatch ----
Check("PhraseMatch exact (case-insensitive)", DiffEngine.PhraseMatch("Maximum Life", "maximum life"));
Check("PhraseMatch substring", DiffEngine.PhraseMatch("life", "maximum life"));
Check("PhraseMatch empty is false", !DiffEngine.PhraseMatch("", "x"));
Check("PhraseMatch unrelated is false", !DiffEngine.PhraseMatch("Dexterity", "Strength"));
Check("PhraseMatch short token doesn't over-match", !DiffEngine.PhraseMatch("of", "maximum life"));

// ---- SlotBaseName ----
Eq("SlotBaseName strips '#1'", "ring", DiffEngine.SlotBaseName("Ring #1"));
Eq("SlotBaseName strips trailing index", "ring", DiffEngine.SlotBaseName("Ring 2"));
Eq("SlotBaseName plain", "helm", DiffEngine.SlotBaseName("Helm"));

// ---- ScoreSlot / AffixMet ----
var tg = new TargetGear { Slot = "Helm", Affixes = {
    new TargetAffix { Name = "Maximum Life", Min = 1000 },
    new TargetAffix { Name = "Dexterity" } } };
var item = new Item { Name = "X", Slot = "Helm", Affixes = {
    new Affix { Text = "Maximum Life", Value = 1200 },
    new Affix { Text = "Dexterity", Value = 50, Min = 10, Max = 100 } } };   // dex roll = 44%
Eq("ScoreSlot gate 50 -> 1 (dex under gate)", 1, DiffEngine.ScoreSlot(tg, item, 50));
Eq("ScoreSlot gate 40 -> 2 (dex clears gate)", 2, DiffEngine.ScoreSlot(tg, item, 40));
Check("AffixMet absolute min satisfied",
    DiffEngine.AffixMet(new TargetAffix { Name = "Maximum Life", Min = 1000 }, item, 50));
Check("AffixMet absolute min not satisfied",
    !DiffEngine.AffixMet(new TargetAffix { Name = "Maximum Life", Min = 2000 }, item, 50));
Check("AffixMet absent affix is false",
    !DiffEngine.AffixMet(new TargetAffix { Name = "Armor" }, item, 50));

// ---- Diff: a fully-met build ----
var target = new TargetBuild
{
    Name = "T",
    Gear = { new TargetGear { Slot = "Helm", Affixes = {
        new TargetAffix { Name = "Maximum Life", Min = 1000 },
        new TargetAffix { Name = "Dexterity" } } } },
    Uniques = { new TargetUnique { Name = "Cowl of the Homeless", Slot = "Helm" } },
};
var liveMet = new LiveBuild { Gear = {
    new Item { Name = "Cowl of the Homeless", Slot = "Helm", Affixes = {
        new Affix { Text = "Maximum Life", Value = 1200 },
        new Affix { Text = "Dexterity", Value = 80, Min = 10, Max = 100 } } } } };   // dex roll ~78%
var rMet = DiffEngine.Diff(target, liveMet, 50);
Eq("Diff met: total 3", 3, rMet.Total);
Eq("Diff met: matched 3", 3, rMet.Matched);
Eq("Diff met: pct 100", 100, rMet.Pct);
Eq("Diff met: under 0", 0, rMet.Under);
Check("Diff met: gear 2/2", rMet.Categories.Any(c => c.Id == "gear" && c.Matched == 2 && c.Total == 2));
Check("Diff met: uniques 1/1", rMet.Categories.Any(c => c.Id == "uniques" && c.Matched == 1 && c.Total == 1));

// ---- Diff: an under-rolled + missing build ----
var livePartial = new LiveBuild { Gear = {
    new Item { Name = "Some Helm", Slot = "Helm", Affixes = {
        new Affix { Text = "Maximum Life", Value = 800 } } } } };   // ML under min; Dex absent; unique absent
var rPart = DiffEngine.Diff(target, livePartial, 50);
Eq("Diff partial: total 3", 3, rPart.Total);
Eq("Diff partial: matched 1 (ML present)", 1, rPart.Matched);
Eq("Diff partial: under 1 (ML below min)", 1, rPart.Under);
Eq("Diff partial: pct 33", 33, rPart.Pct);

// ---- BuildGuide ----
Eq("Steps: none when complete", 0, BuildGuide.Steps(rMet).Count);
var steps = BuildGuide.Steps(rPart);
Check("Steps: GET for the missing affix", steps.Any(s => s.Verb == "GET"));
Check("Steps: IMPROVE for the under-rolled affix", steps.Any(s => s.Verb == "IMPROVE"));
Check("Steps: FIND for the missing unique", steps.Any(s => s.Verb == "FIND"));
Check("Steps: impact-ordered by tier", steps.Select(s => s.Tier).SequenceEqual(steps.Select(s => s.Tier).OrderBy(t => t)));

// ---- GearParser: the seasonally-fragile TTS parser, against the canonical sample log fixture ----
var sampleLog = Path.Combine(AppContext.BaseDirectory, "sample_tts.log");
Check("sample_tts.log fixture present", File.Exists(sampleLog));
if (File.Exists(sampleLog))
{
    var lb = LogWatcher.BuildFromFile(sampleLog, equippedOnly: false);
    Eq("Parser: 5 items parsed", 5, lb.Gear.Count);

    Item? Slot(string s) => lb.Gear.FirstOrDefault(g => g.Slot == s);
    var helm = Slot("helm");
    Check("Parser: helm parsed", helm != null);
    if (helm != null)
    {
        Eq("Parser: helm name title-cased", "Archon Spellblade", helm.Name);
        Eq("Parser: helm rarity Legendary", "Legendary", helm.Rarity ?? "");
        Eq("Parser: helm item power 780", 780, helm.ItemPower ?? 0);
        Eq("Parser: helm masterwork 4", 4, helm.MasterworkRank ?? 0);
        Eq("Parser: helm temper used 2", 2, helm.TemperUsed ?? 0);
        Eq("Parser: helm requires level 60", 60, helm.RequiresLevel ?? 0);
        Eq("Parser: helm 4 affixes", 4, helm.Affixes.Count);
        var life = helm.Affixes.FirstOrDefault(a => a.Text.Contains("Maximum Life"));
        Check("Parser: helm has Maximum Life affix", life != null);
        if (life != null)
        {
            Eq("Parser: Maximum Life value 1540", 1540d, life.Value ?? 0);
            Eq("Parser: Maximum Life range min 1300", 1300d, life.Min ?? 0);
            Eq("Parser: Maximum Life range max 1600", 1600d, life.Max ?? 0);
            Check("Parser: Maximum Life is not a percent", !life.IsPercent);
        }
        var cdr = helm.Affixes.FirstOrDefault(a => a.Text.Contains("Cooldown"));
        Check("Parser: Cooldown Reduction flagged percent", cdr != null && cdr.IsPercent);
    }
    Check("Parser: apostrophe name lower-cased after '", lb.Gear.Any(g => g.Name.Contains("Sorcerer's")));
    var chest = Slot("chest");
    Check("Parser: chest unique (Raiment)", chest != null && chest.IsUnique && chest.Name.Contains("Raiment"));
    var ring = Slot("ring");
    Check("Parser: ring mythic + ancestral (Tal Rasha's)", ring != null && ring.IsMythic && ring.IsUnique && ring.IsAncestral && ring.Name.Contains("Tal Rasha"));
}

// ---- Substitutes: core-vs-flexible classification + best-owned + ladder ----
var subTarget = new TargetBuild { Gear = {
    new TargetGear { Slot = "Helm", Affixes = {
        new TargetAffix { Name = "Maximum Life", Min = 1000 },   // value-gated -> core
        new TargetAffix { Name = "Dexterity" },                   // on 2 slots -> core
        new TargetAffix { Name = "Lucky Hit Chance" } } },        // once, ungated -> flexible
    new TargetGear { Slot = "Gloves", Affixes = {
        new TargetAffix { Name = "Dexterity" },
        new TargetAffix { Name = "Attack Speed" } } } } };
var coreNames = Substitutes.CoreAffixNames(subTarget);
Check("CoreAffix: value-gated is core (maximum life)", coreNames.Contains("maximum life"));
Check("CoreAffix: repeated-across-slots is core (dexterity)", coreNames.Contains("dexterity"));
Check("CoreAffix: single ungated is flexible (lucky hit chance)", !coreNames.Contains("lucky hit chance"));
var subLive = new LiveBuild { Gear = {
    new Item { Name = "A Helm", Slot = "Helm", Affixes = {
        new Affix { Text = "Maximum Life", Value = 1200 },
        new Affix { Text = "Dexterity", Value = 50 } } } } };
var plan = Substitutes.Plan(subTarget, subLive, 50);
Eq("Substitutes: one entry per gear slot", 2, plan.Count);
var helmSub = plan.First(s => s.Slot == "Helm");
Eq("Substitutes: helm coreTotal 2", 2, helmSub.CoreTotal);
Eq("Substitutes: helm wanted label", "Any Helm", helmSub.Wanted);
Eq("Substitutes: ladder is Now/Better/Best", 3, helmSub.Ladder.Count);
Check("Substitutes: ladder leads with Now", helmSub.Ladder[0].StartsWith("Now:"));
Check("Substitutes: best-owned is the equipped helm", helmSub.BestOwned == "A Helm");

// ---- Activities: build-tailored recommendations from the gaps ----
Eq("Activities: none when complete", 0, Activities.Recommend(rMet).Count);
var acts2 = Activities.Recommend(rPart);
Check("Activities: chase the missing affixes", acts2.Any(a => a.Title.Contains("missing affixes")));
Check("Activities: hunt the missing uniques", acts2.Any(a => a.Title.Contains("missing uniques")));
Check("Activities: masterwork for under-rolled", acts2.Any(a => a.Title.Contains("Masterwork")));

// ---- LootFilter: markdown checklist + D4Companion-shaped preset ----
var md = LootFilter.Markdown(target);
Check("LootFilter md: title", md.Contains("# T — Loot Filter"));
Check("LootFilter md: Helm section", md.Contains("## Helm"));
Check("LootFilter md: lists affix + threshold", md.Contains("Maximum Life") && md.Contains("≥"));
Check("LootFilter md: lists the unique", md.Contains("**Unique:** Cowl of the Homeless"));
var preset = System.Text.Json.JsonSerializer.Serialize(LootFilter.CompanionPreset(target));
Check("LootFilter preset: ItemAffixes key", preset.Contains("ItemAffixes"));
Check("LootFilter preset: ItemUniques key", preset.Contains("ItemUniques"));
Check("LootFilter preset: affix id present", preset.Contains("Maximum Life"));
Check("LootFilter preset: unique id present", preset.Contains("Cowl of the Homeless"));
Check("LootFilter preset: affix typed by slot", preset.Contains("\"Type\":\"Helm\""));

// ---- BuildGuide dedup + RE-TEMPER verb ----
var guideTarget = new TargetBuild { Gear = {
    new TargetGear { Slot = "weapon", Affixes = { new TargetAffix { Name = "Damage Over Time" } } },
    new TargetGear { Slot = "weapon", Affixes = { new TargetAffix { Name = "Damage Over Time" } } } } };
var liveMissAll = new LiveBuild();
var rMissAll = DiffEngine.Diff(guideTarget, liveMissAll, 50);
var stepsAll = BuildGuide.Steps(rMissAll);
// Same affix on two weapon slots should be merged into one step
var dotSteps = stepsAll.Where(s => s.Text.Contains("Damage Over Time")).ToList();
Check("BuildGuide dedup: duplicate affix steps merged", dotSteps.Count == 1);
// Both slots share the same label "weapon", so the merged text is "weapon — Damage Over Time"
// (distinct dedup means only one slot label shown — that's correct, not a bug)
Check("BuildGuide dedup: merged text still contains the affix label", dotSteps.Count > 0 && dotSteps[0].Text.Contains("Damage Over Time"));

var retemperTarget = new TargetBuild { Gear = {
    new TargetGear { Slot = "helm", Affixes = {
        new TargetAffix { Name = "Max Stacks", Min = 3, Tempered = true } } } } };
var liveLowRoll = new LiveBuild { Gear = { new Item { Name = "A Helm", Slot = "helm", Affixes = {
    new Affix { Text = "Max Stacks", Value = 2, Min = 1, Max = 5 } } } } };
var rTemper = DiffEngine.Diff(retemperTarget, liveLowRoll, 50);
var temSteps = BuildGuide.Steps(rTemper);
Check("BuildGuide RE-TEMPER verb for under-rolled tempered affix",
    temSteps.Any(s => s.Verb == "RE-TEMPER"));

// ---- GearParser.ParseTooltipLines: OCR path (no EQUIPPED / end-marker machinery) ----
var ocrBlock = new[]
{
    "ARCHON SPELLBLADE",
    "Legendary Helm",
    "780 Item Power",
    "+1,540 Maximum Life [1,300 - 1,600]",
    "+10.5% Cooldown Reduction [10% - 15%]",
    "+25 Intelligence [20 - 30]",
    "Requires Level 60",
};
var ocrItem = GearParser.ParseTooltipLines(ocrBlock);
Check("ParseTooltipLines: item parsed from OCR block", ocrItem != null);
if (ocrItem != null)
{
    Eq("ParseTooltipLines: name", "Archon Spellblade", ocrItem.Name);
    Eq("ParseTooltipLines: slot", "helm", ocrItem.Slot ?? "");
    Eq("ParseTooltipLines: rarity", "Legendary", ocrItem.Rarity ?? "");
    Eq("ParseTooltipLines: item power", 780, ocrItem.ItemPower ?? 0);
    Eq("ParseTooltipLines: 3 affixes", 3, ocrItem.Affixes.Count);
    var ocrLife = ocrItem.Affixes.FirstOrDefault(a => a.Text.Contains("Maximum Life"));
    Check("ParseTooltipLines: Maximum Life present", ocrLife != null);
    if (ocrLife != null)
    {
        Eq("ParseTooltipLines: Maximum Life value", 1540d, ocrLife.Value ?? 0);
        Eq("ParseTooltipLines: Maximum Life min", 1300d, ocrLife.Min ?? 0);
    }
    var ocrCdr = ocrItem.Affixes.FirstOrDefault(a => a.Text.Contains("Cooldown"));
    Check("ParseTooltipLines: Cooldown Reduction is percent", ocrCdr != null && ocrCdr.IsPercent);
}
Check("ParseTooltipLines: empty list returns null", GearParser.ParseTooltipLines(Array.Empty<string>()) == null);
Check("ParseTooltipLines: no name returns null",
    GearParser.ParseTooltipLines(new[] { "Legendary Helm", "780 Item Power" }) == null);

// ReQuality: both TTS "50 +50/25 Quality" (no parens) and OCR "50 (+30/25) Quality" (parens) formats
var qualityBlock = new[] { "MYTHIC RING", "Mythic Unique Ring", "800 Item Power", "50 +50/25 Quality", "+500 Maximum Life" };
var qualityItem = GearParser.ParseTooltipLines(qualityBlock);
Check("ReQuality fix: unparenthesized '50 +50/25 Quality' parsed", qualityItem != null);
Check("ReQuality fix: item.Quality == 50 (no parens)", qualityItem?.Quality == 50);

var qLines = new[] { "SOME UNIQUE ITEM", "850 Item Power", "Legendary", "50 (+30/25) Quality", "Left mouse button" };
var qSeg = new GearParser();
Item? qParsed = null;
foreach (var ln in qLines) { var r = qSeg.Feed(ln); if (r != null) qParsed = r; }
Check("ReQuality fix: parenthesized '50 (+30/25) Quality' parsed", qParsed?.Quality == 50);

// ItemSource: LogWatcher stamps Source=Tts on all parsed items
if (File.Exists(sampleLog))
{
    var lbTts = LogWatcher.BuildFromFile(sampleLog, equippedOnly: false);
    Check("ItemSource: all TTS-parsed items stamped Tts",
        lbTts.Gear.Count > 0 && lbTts.Gear.All(g => g.Source == ItemSource.Tts));
}

// LogToJsonlConverter: compact serialization produces one line per item (no newlines in the JSON).
var testItem = new Item { Name = "Doom", Slot = "helm", Rarity = "Legendary",
    Affixes = { new Affix { Text = "Max Life", Value = 1000 } } };
var compact = System.Text.Json.JsonSerializer.Serialize(testItem, new System.Text.Json.JsonSerializerOptions
{
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
});
Check("LogToJsonlConverter compact JSON fits on one line", !compact.Contains('\n'));

// ---- TTS weapon path: regression net for the S8 bugs (melee-in-ranged-slot, weapon duplication) ----
// The sample_tts.log fixture has NO weapons, so the weapon-assignment + dedup paths were entirely
// uncovered — exactly where the repeated "weapons are wrong" reports came from. Pin them directly.

// 1) Weapon-type affinity must beat raw affix-count: a crossbow target must claim the live crossbow
//    even when a sword matches MORE of its wanted affixes (the "melee in the crossbow slot" report).
{
    var wTarget = new TargetBuild { Gear = {
        new TargetGear { Slot = "weapon", Label = "Crossbow", ItemId = "2HCrossbow_Unique_Skyhunter",
            Affixes = { new TargetAffix { Name = "Dexterity" }, new TargetAffix { Name = "Critical Strike Chance" } } },
        new TargetGear { Slot = "weapon", Label = "Sword", ItemId = "1HSword_Unique_Etna",
            Affixes = { new TargetAffix { Name = "Dexterity" } } } } };
    var wLive = new LiveBuild { Gear = {
        // the crossbow matches only 1 of the crossbow-target's affixes...
        new Item { Name = "Skyhunter", Slot = "weapon", ItemType = "Crossbow",
            Affixes = { new Affix { Text = "Dexterity", Value = 50 } } },
        // ...the sword matches BOTH — without the type bonus it would steal the crossbow slot.
        new Item { Name = "Etna's Lost Dagger", Slot = "weapon", ItemType = "Sword",
            Affixes = { new Affix { Text = "Dexterity", Value = 60 }, new Affix { Text = "Critical Strike Chance", Value = 9 } } } } };
    var wGroups = DiffEngine.Diff(wTarget, wLive).Categories.First(c => c.Id == "gear").Groups;
    var xbow = wGroups.First(g => g.Name == "Crossbow");
    var swd  = wGroups.First(g => g.Name == "Sword");
    Check("Weapon assign: crossbow slot gets the crossbow, not the higher-affix sword",
        xbow.LiveItems.Count == 1 && xbow.LiveItems[0].Name == "Skyhunter");
    Check("Weapon assign: sword slot gets the sword",
        swd.LiveItems.Count == 1 && swd.LiveItems[0].Name == "Etna's Lost Dagger");
    Check("Weapon assign: the same weapon is never placed in two slots",
        xbow.LiveItems.Count == 1 && swd.LiveItems.Count == 1 && xbow.LiveItems[0].Name != swd.LiveItems[0].Name);
}

// 2) LatestPerSlot must collapse the SAME weapon re-hovered at two panel positions to one entry
//    (the "same weapon shows up in multiple slots" duplication report).
{
    var dup = new List<Item> {
        new Item { Name = "Skyhunter", RawName = "SKYHUNTER", Slot = "weapon", SlotPosition = 1 },
        new Item { Name = "Skyhunter", RawName = "SKYHUNTER", Slot = "weapon", SlotPosition = 2 },
    };
    Eq("LatestPerSlot: a re-hovered weapon collapses to one entry", 1, LogWatcher.LatestPerSlot(dup).Count);
}

// 3) ...but two DISTINCT dual-wield weapons must BOTH survive (guards the opposite failure mode:
//    over-pruning to "only 2 weapons shown" / dropping a real second weapon).
{
    var two = new List<Item> {
        new Item { Name = "Skyhunter", RawName = "SKYHUNTER", Slot = "weapon", SlotPosition = 1 },
        new Item { Name = "Etna's Lost Dagger", RawName = "ETNA'S LOST DAGGER", Slot = "weapon", SlotPosition = 2 },
    };
    Eq("LatestPerSlot: two distinct dual-wield weapons are both kept", 2, LogWatcher.LatestPerSlot(two).Count);
}

// ---- TTS diagnostics: faithful raw -> parsed -> classified -> final introspection for the in-app view ----
{
    // The sample fixture parses 5 items but has no nav/EQUIPPED lines (so they classify non-equipped) —
    // assert the parse-level facts that hold regardless of classification.
    if (File.Exists(sampleLog))
    {
        var ds = LogWatcher.Diagnose(sampleLog);
        Check("Diagnose: sample log marked existing", ds.LogExists);
        Eq("Diagnose: 5 items parsed from sample", 5, ds.Items.Count);
        Check("Diagnose: helm row carries slot + item power", ds.Items.Any(d => d.Slot == "helm" && d.ItemPower == 780));
        Check("Diagnose: raw tail captured", ds.RawTail.Count > 0);
        Check("Diagnose: final set is a subset of parsed", ds.FinalEquipped.Count <= ds.Items.Count);
    }
    // With real navigation + EQUIPPED markers (as a live log has), an item must classify equipped and
    // survive to the final displayed set — the path the stripped sample fixture can't exercise.
    var navLog = new[] {
        "=== d4scanner tts shim attached ===",
        "Equipment", "Head", "EQUIPPED",
        "ARCHON SPELLBLADE",
        "Legendary Helm",
        "780 Item Power",
        "+1,540 Maximum Life [1,300 - 1,600]",
        "Durability: 100/100. Tempers: 2/2",
        "Requires Level 60",
        "Right mouse button",
    };
    var dn = LogWatcher.DiagnoseLines(navLog);
    Eq("Diagnose(nav): one item parsed", 1, dn.Items.Count);
    Check("Diagnose(nav): item classified equipped", dn.Items.Count == 1 && dn.Items[0].Equipped);
    Check("Diagnose(nav): item survives to final set", dn.Items.Count == 1 && dn.Items[0].InFinal);
    Eq("Diagnose(nav): final equipped count 1", 1, dn.FinalEquipped.Count);
    Check("Diagnose(nav): session marker counted", dn.SessionMarkers >= 1);
    Check("Diagnose(nav): panel tracked as Character", dn.Items.Count == 1 && dn.Items[0].Panel == "Character");
}

// ---- Real S8 Rogue capture: full-loadout regression from an actual hover session ----
// Covers the slots the original fixture lacked: bow + dual-wield melee, amulet, pants, runewords, and the
// double-hovered off-hand sword that must collapse to one. This is the gold fixture from a live capture.
var rogueLog = Path.Combine(AppContext.BaseDirectory, "sample_tts_rogue_s8.log");
Check("Rogue fixture present", File.Exists(rogueLog));
if (File.Exists(rogueLog))
{
    var g = LogWatcher.BuildFromFile(rogueLog, equippedOnly: true).Gear;
    Eq("Rogue: 11 equipped items captured", 11, g.Count);
    Eq("Rogue: exactly 3 weapons after dedup (bow + sword + dagger)", 3, g.Count(x => x.Slot == "weapon"));
    Eq("Rogue: double-hovered off-hand sword collapses to one", 1, g.Count(x => x.Name.Contains("Obsidian Blade")));
    Eq("Rogue: exactly 2 rings", 2, g.Count(x => x.Slot == "ring"));
    Check("Rogue: bow captured as weapon @ 900 IP", g.Any(x => x.Name.Contains("Mammalbane") && x.Slot == "weapon" && x.ItemPower == 900));
    Check("Rogue: main-hand dagger captured as weapon", g.Any(x => x.Name.Contains("Etna") && x.Slot == "weapon"));
    Check("Rogue: helm unique captured", g.Any(x => x.Name.Contains("Nameless") && x.Slot == "helm" && x.IsUnique));
    Check("Rogue: pants captured", g.Any(x => x.Name.Contains("Shrouded Gift") && x.Slot == "pants"));
    Check("Rogue: amulet captured", g.Any(x => x.Name.Contains("Wildbolt") && x.Slot == "amulet"));
    Check("Rogue: gloves captured", g.Any(x => x.Name.Contains("Vambraces") && x.Slot == "gloves"));
    Check("Rogue: chest captured", g.Any(x => x.Name.Contains("Boneweave") && x.Slot == "chest"));
    Check("Rogue: boots captured", g.Any(x => x.Name.Contains("Adventurer") && x.Slot == "boots"));

    // stat-line noise filter: weapon implicits + bare summary totals must NOT become affixes,
    // while real affixes of similar names survive (regression guard against over-filtering).
    var bow = g.First(x => x.Name.Contains("Mammalbane"));
    Check("Rogue: bow drops the Weapon Damage implicit",
        !bow.Affixes.Any(a => a.Text.Equals("Weapon Damage", StringComparison.OrdinalIgnoreCase)));
    Check("Rogue: bow drops the Attacks per Second stat",
        !bow.Affixes.Any(a => a.Text.Contains("Attacks per Second", StringComparison.OrdinalIgnoreCase)));
    var chest = g.First(x => x.Name.Contains("Boneweave"));
    Eq("Rogue: chest keeps exactly one Armor affix (summary total dropped)",
        1, chest.Affixes.Count(a => a.Text.Equals("Armor", StringComparison.OrdinalIgnoreCase)));
    Check("Rogue: chest's surviving Armor affix is the rolled one (has a range)",
        chest.Affixes.First(a => a.Text.Equals("Armor", StringComparison.OrdinalIgnoreCase)).Min != null);
    var amulet = g.First(x => x.Name.Contains("Wildbolt"));
    Check("Rogue: amulet drops the bare 'All Resist' summary",
        !amulet.Affixes.Any(a => a.Text.Equals("All Resist", StringComparison.OrdinalIgnoreCase)));
    var boots = g.First(x => x.Name.Contains("Adventurer"));
    Check("Rogue: boots keep the real 'Resistance to All Elements' affix",
        boots.Affixes.Any(a => a.Text.Contains("Resistance to All Elements", StringComparison.OrdinalIgnoreCase)));

    // (C) Etna main-hand dagger voices "25 ( +25) Quality" — null before the ReQuality fix, must now be 25.
    var etna = g.First(x => x.Name.Contains("Etna") && x.Slot == "weapon");
    Check("Rogue: Etna dagger Quality parsed from '25 ( +25)' form", etna.Quality == 25);
    // (B) "+176 Dexterity +[125 - 149]" -> 'Dexterity' (dangling '+' stripped), not 'Dexterity +'.
    Check("Rogue: Etna dagger has a clean 'Dexterity' affix (trailing '+' stripped)",
        etna.Affixes.Any(a => a.Text.Equals("Dexterity", StringComparison.Ordinal)));
    Check("Rogue: no affix text retains a dangling '+'",
        g.All(x => x.Affixes.All(a => !a.Text.EndsWith("+") && a.Text.Trim() == a.Text)));
    // (B) "+231 Life on Kill +[219 - 263]" -> 'Life on Kill', never 'Life on Kill +'.
    Check("Rogue: 'Life on Kill' affix parsed without trailing '+'",
        g.Any(x => x.Affixes.Any(a => a.Text.Equals("Life on Kill", StringComparison.Ordinal))) &&
        g.All(x => !x.Affixes.Any(a => a.Text.Equals("Life on Kill +", StringComparison.Ordinal))));
    // weapon de-dup bound to REAL fixture names (this Rogue's actual bow/sword/dagger), not just synthetic strings.
    var liveWeaponNames = LiveGearResolver.BuildShownWeaponNameSet(g.Where(x => x.Slot == "weapon").Select(x => x.Name));
    Check("Rogue: weapon-dedup hides the actual equipped Etna dagger by name",
        LiveGearResolver.ShouldHideDuplicateWeapon(liveWeaponNames, "Etna's Lost Dagger"));
    Check("Rogue: weapon-dedup keeps a weapon not in the loadout",
        !LiveGearResolver.ShouldHideDuplicateWeapon(liveWeaponNames, "Doombringer"));
}

// ---- ReQuality: paren-single-number Quality form + bare-Quality guard (no fixture needed) ----
var qParen1 = GearParser.ParseTooltipLines(new[] {
    "SOME UNIQUE BLADE", "Unique Dagger", "850 Item Power", "25 ( +25) Quality", "+100 Dexterity [80 - 120]" });
Check("ReQuality fix: paren single-number '25 ( +25) Quality' parsed", qParen1 != null);
Check("ReQuality fix: item.Quality == 25 (paren, no slash)", qParen1?.Quality == 25);
var qBare = GearParser.ParseTooltipLines(new[] {
    "PLAIN ITEM", "Legendary Ring", "800 Item Power", "50 Quality", "+200 Maximum Life [150 - 250]" });
Check("ReQuality: bare '50 Quality' still parses (non-regression guard, pre-existing branch)", qBare?.Quality == 50);
// ReAffixTrailJunk must NOT over-strip: a normal multi-word affix is left byte-identical.
var cleanAff = GearParser.ParseTooltipLines(new[] {
    "GUARD ITEM", "Legendary Ring", "800 Item Power", "+100 Critical Strike Chance [80 - 120]" });
Check("Trailing strip: a normal affix keeps its exact text (no over-strip)",
    cleanAff != null && cleanAff.Affixes.Any(a => a.Text == "Critical Strike Chance"));

// ---- LiveGearResolver.Merge (extracted MergeGear) + weapon de-dup decision ----
Item MkGear(string name, string slot, ItemSource src) => new Item { Name = name, Slot = slot, Source = src };
// empty fresh returns the persisted list unchanged (reference-identical) — callers rely on no defensive copy.
{
    var persisted = new List<Item> { MkGear("Helm-T", "Helm", ItemSource.Tts) };
    var merged = LiveGearResolver.Merge(persisted, new List<Item>());
    Check("Merge: empty fresh returns persisted (reference)", ReferenceEquals(persisted, merged));
}
// fresh Tts replaces a persisted item for the same slot base.
{
    var persisted = new List<Item> { MkGear("OldHelm", "Helm", ItemSource.Tts) };
    var fresh     = new List<Item> { MkGear("NewHelm", "Helm", ItemSource.Tts) };
    var merged = LiveGearResolver.Merge(persisted, fresh);
    Eq("Merge: fresh-Tts replaces persisted (count)", 1, merged.Count);
    Eq("Merge: fresh-Tts replaces persisted (name)", "NewHelm", merged[0].Name);
}
// fresh Ocr does NOT replace a persisted Tts for the same slot base — Tts is kept.
{
    var persisted = new List<Item> { MkGear("TtsHelm", "Helm", ItemSource.Tts) };
    var fresh     = new List<Item> { MkGear("OcrHelm", "Helm", ItemSource.Ocr) };
    var merged = LiveGearResolver.Merge(persisted, fresh);
    Eq("Merge: fresh-Ocr does NOT replace persisted-Tts (count)", 1, merged.Count);
    Eq("Merge: fresh-Ocr does NOT replace persisted-Tts (kept Tts name)", "TtsHelm", merged[0].Name);
    Check("Merge: kept item is the Tts one", merged[0].Source == ItemSource.Tts);
}
// a fresh batch with BOTH Tts and Ocr for a slot: whole fresh group wins over persisted-Tts.
{
    var persisted = new List<Item> { MkGear("TtsHelm", "Helm", ItemSource.Tts) };
    var fresh     = new List<Item> { MkGear("FreshTtsHelm", "Helm", ItemSource.Tts), MkGear("FreshOcrHelm", "Helm", ItemSource.Ocr) };
    var merged = LiveGearResolver.Merge(persisted, fresh);
    Eq("Merge: fresh batch with a Tts wins over persisted-Tts (count)", 2, merged.Count);
    Check("Merge: fresh batch with a Tts replaced persisted", merged.All(i => i.Name.StartsWith("Fresh")));
}
// fresh Ocr replaces a persisted Ocr (no Tts conflict -> fresh wins).
{
    var persisted = new List<Item> { MkGear("OldOcrHelm", "Helm", ItemSource.Ocr) };
    var fresh     = new List<Item> { MkGear("NewOcrHelm", "Helm", ItemSource.Ocr) };
    var merged = LiveGearResolver.Merge(persisted, fresh);
    Eq("Merge: fresh-Ocr replaces persisted-Ocr (count)", 1, merged.Count);
    Eq("Merge: fresh-Ocr replaces persisted-Ocr (name)", "NewOcrHelm", merged[0].Name);
}
// fresh Tts UPGRADES a slot that previously held only Ocr — the central 'Tts wins' direction.
{
    var persisted = new List<Item> { MkGear("StaleOcrHelm", "Helm", ItemSource.Ocr) };
    var fresh     = new List<Item> { MkGear("FreshTtsHelm", "Helm", ItemSource.Tts) };
    var merged = LiveGearResolver.Merge(persisted, fresh);
    Eq("Merge: fresh-Tts upgrades a persisted-Ocr slot", "FreshTtsHelm", merged[0].Name);
    Check("Merge: upgraded item is Tts", merged[0].Source == ItemSource.Tts);
}
// untouched slots preserved: fresh only touches Helm, persisted Chest survives.
{
    var persisted = new List<Item> { MkGear("ChestT", "Chest", ItemSource.Tts), MkGear("HelmT", "Helm", ItemSource.Tts) };
    var fresh     = new List<Item> { MkGear("NewHelm", "Helm", ItemSource.Tts) };
    var merged = LiveGearResolver.Merge(persisted, fresh);
    Eq("Merge: untouched slot preserved (count)", 2, merged.Count);
    Check("Merge: untouched Chest preserved", merged.Any(i => i.Name == "ChestT" && i.Source == ItemSource.Tts));
    Check("Merge: touched Helm updated", merged.Any(i => i.Name == "NewHelm"));
    Check("Merge: stale Helm removed", !merged.Any(i => i.Name == "HelmT"));
}
// slot base-name normalization: 'Ring #1' and 'Ring 2' share base 'ring'; fresh-Ocr ring must not replace persisted-Tts ring.
{
    var persisted = new List<Item> { MkGear("TtsRing", "Ring #1", ItemSource.Tts) };
    var fresh     = new List<Item> { MkGear("OcrRing", "Ring 2", ItemSource.Ocr) };
    var merged = LiveGearResolver.Merge(persisted, fresh);
    Eq("Merge: ring base-name Ocr does NOT replace persisted-Tts (count)", 1, merged.Count);
    Eq("Merge: ring base-name kept the Tts name", "TtsRing", merged[0].Name);
}
// weapon de-dup decision (paper-doll: same unique weapon must not render twice)
var shownWeapons = LiveGearResolver.BuildShownWeaponNameSet(new[] { "Etna's Lost Dagger", "Word of Hakan", "", null });
Eq("BuildShownWeaponNameSet skips null/empty", 2, shownWeapons.Count);
Check("BuildShownWeaponNameSet is case-insensitive", shownWeapons.Contains("etna's lost dagger"));
Check("ShouldHideDuplicateWeapon hides an already-shown weapon", LiveGearResolver.ShouldHideDuplicateWeapon(shownWeapons, "Etna's Lost Dagger"));
Check("ShouldHideDuplicateWeapon keeps a distinct weapon", !LiveGearResolver.ShouldHideDuplicateWeapon(shownWeapons, "Doombringer"));
Check("ShouldHideDuplicateWeapon is case-insensitive", LiveGearResolver.ShouldHideDuplicateWeapon(shownWeapons, "ETNA'S LOST DAGGER"));
Check("ShouldHideDuplicateWeapon null candidate is false", !LiveGearResolver.ShouldHideDuplicateWeapon(shownWeapons, null));
Check("ShouldHideDuplicateWeapon empty candidate is false", !LiveGearResolver.ShouldHideDuplicateWeapon(shownWeapons, ""));
Check("ShouldHideDuplicateWeapon works on a plain list (case-insensitive)", LiveGearResolver.ShouldHideDuplicateWeapon(new List<string> { "Word of Hakan" }, "word of hakan"));
Check("ShouldHideDuplicateWeapon empty set is false", !LiveGearResolver.ShouldHideDuplicateWeapon(new HashSet<string>(), "Doombringer"));

// ---- report ----
Console.WriteLine($"D4Scanner.Core tests: {passed} passed, {failed} failed");
foreach (var f in failures) Console.WriteLine("  FAIL: " + f);
return failed == 0 ? 0 : 1;
