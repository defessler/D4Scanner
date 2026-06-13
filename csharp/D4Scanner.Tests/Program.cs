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

// ---- ScoreSlot / AffixMet (100% baseline: presence + EXPLICIT build minimums only — no global gate) ----
var tg = new TargetGear { Slot = "Helm", Affixes = {
    new TargetAffix { Name = "Maximum Life", Min = 1000 },
    new TargetAffix { Name = "Dexterity" } } };
var item = new Item { Name = "X", Slot = "Helm", Affixes = {
    new Affix { Text = "Maximum Life", Value = 1200 },
    new Affix { Text = "Dexterity", Value = 50, Min = 10, Max = 100 } } };   // dex roll = 44%
Eq("ScoreSlot: presence + explicit Min -> 2 (a 44% roll with no build minimum is MET)", 2, DiffEngine.ScoreSlot(tg, item));
var tgMinPct = new TargetGear { Slot = "Helm", Affixes = {
    new TargetAffix { Name = "Maximum Life", Min = 2000 },                    // explicit Min not reached
    new TargetAffix { Name = "Dexterity", MinPercent = 60 } } };              // explicit roll min not reached (44%)
Eq("ScoreSlot: explicit Min + MinPercent below -> 0", 0, DiffEngine.ScoreSlot(tgMinPct, item));
var tgMinPctLow = new TargetGear { Slot = "Helm", Affixes = { new TargetAffix { Name = "Dexterity", MinPercent = 40 } } };
Eq("ScoreSlot: explicit MinPercent cleared -> 1", 1, DiffEngine.ScoreSlot(tgMinPctLow, item));
Check("AffixMet absolute min satisfied",
    DiffEngine.AffixMet(new TargetAffix { Name = "Maximum Life", Min = 1000 }, item));
Check("AffixMet absolute min not satisfied",
    !DiffEngine.AffixMet(new TargetAffix { Name = "Maximum Life", Min = 2000 }, item));
Check("AffixMet absent affix is false",
    !DiffEngine.AffixMet(new TargetAffix { Name = "Armor" }, item));
Check("AffixMet presence with no minimum is met",
    DiffEngine.AffixMet(new TargetAffix { Name = "Dexterity" }, item));

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
var rMet = DiffEngine.Diff(target, liveMet);
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
var rPart = DiffEngine.Diff(target, livePartial);
Eq("Diff partial: total 3", 3, rPart.Total);
Eq("Diff partial: matched 1 (ML present)", 1, rPart.Matched);
Eq("Diff partial: under 1 (ML below min)", 1, rPart.Under);
Eq("Diff partial: pct 33", 33, rPart.Pct);

// ---- A6: owned-unique secondary-roll advisory (RollNote) — presence still counts; a badly-rolled copy is flagged ----
{
    var a6Target = new TargetBuild
    {
        Name = "A6",
        Uniques = { new TargetUnique { Name = "Sea Lord's Fine Gloves", Slot = "Gloves",
            Affixes = { new TargetAffix { Name = "Dexterity" }, new TargetAffix { Name = "Armor" } } } },
    };
    // owns the unique but only ONE of the two build secondaries (missing Armor)
    var a6Partial = new LiveBuild { Gear = {
        new Item { Name = "Sea Lord's Fine Gloves", Slot = "gloves", IsUnique = true, Affixes = {
            new Affix { Text = "Dexterity", Value = 80 } } } } };
    var a6u = DiffEngine.Diff(a6Target, a6Partial).Categories.First(c => c.Id == "uniques").Groups[0].Items[0];
    Check("A6: owned unique still counts as met (presence)", a6u.Done);
    Check("A6: under-rolled owned unique gets a RollNote (1/2)", a6u.RollNote != null && a6u.RollNote.Contains("1/2"));
    Check("A6: RollNote names the missing build secondary", a6u.RollNote!.Contains("Armor"));
    // owns the unique with BOTH build secondaries → no advisory note
    var a6Full = new LiveBuild { Gear = {
        new Item { Name = "Sea Lord's Fine Gloves", Slot = "gloves", IsUnique = true, Affixes = {
            new Affix { Text = "Dexterity", Value = 80 }, new Affix { Text = "Armor", Value = 500 } } } } };
    var a6uf = DiffEngine.Diff(a6Target, a6Full).Categories.First(c => c.Id == "uniques").Groups[0].Items[0];
    Check("A6: fully-rolled owned unique has no RollNote", a6uf.Done && a6uf.RollNote == null);
}

// ---- BuildGuide ----
Eq("Steps: none when complete", 0, BuildGuide.Steps(rMet).Count);
var steps = BuildGuide.Steps(rPart);
Check("Steps: GET for the missing affix", steps.Any(s => s.Verb == "GET"));
Check("Steps: IMPROVE for the under-rolled affix", steps.Any(s => s.Verb == "IMPROVE"));
Check("Steps: FIND for the missing unique", steps.Any(s => s.Verb == "FIND"));
Check("Steps: impact-ordered by tier", steps.Select(s => s.Tier).SequenceEqual(steps.Select(s => s.Tier).OrderBy(t => t)));

// BuildGuide: several owned upgrades for ONE slot collapse to a single EQUIP step (best by met) + a count,
// instead of flooding the DO NEXT rail with one step per candidate (fresh-review find).
{
    var eqTarget = new TargetBuild { Gear = { new TargetGear { Slot = "Boots", Affixes = {
        new TargetAffix { Name = "Dexterity" }, new TargetAffix { Name = "Maximum Life" }, new TargetAffix { Name = "Movement Speed" } } } } };
    var eqLive = new LiveBuild
    {
        Gear = { new Item { Name = "Worn Boots", Slot = "boots", Equipped = true, Affixes = { new Affix { Text = "Dexterity", Value = 10 } } } },
        Inventory =
        {
            new Item { Name = "Better Boots", Slot = "boots", Affixes = { new Affix { Text = "Dexterity", Value = 10 }, new Affix { Text = "Maximum Life", Value = 500 } } },
            new Item { Name = "Best Boots",   Slot = "boots", Affixes = { new Affix { Text = "Dexterity", Value = 10 }, new Affix { Text = "Maximum Life", Value = 500 }, new Affix { Text = "Movement Speed", Value = 16 } } },
        },
    };
    var eqSteps = BuildGuide.Steps(DiffEngine.Diff(eqTarget, eqLive), eqLive).Where(s => s.Verb == "EQUIP").ToList();
    Check("Guide EQUIP-collapse: exactly one EQUIP step for the slot", eqSteps.Count == 1);
    if (eqSteps.Count == 1)
    {
        Check("Guide EQUIP-collapse: picks the best owned upgrade (Best Boots)", (eqSteps[0].Text ?? "").Contains("Best Boots"));
        Check("Guide EQUIP-collapse: detail notes the extra owned upgrade (+1 more)", (eqSteps[0].Detail ?? "").Contains("+1 more"));
    }
}

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
var plan = Substitutes.Plan(subTarget, subLive);
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

// ---- SeasonPack: embedded data loads + typed accessors ----
{
    var sp = SeasonPack.Current;
    Eq("SeasonPack: season 13", 13, sp.Season);
    Check("SeasonPack: label names the season", sp.SeasonLabel.Contains("Season 13"));
    Check("SeasonPack: activity copy present", sp.Activity("uniques").Title.Length > 0 && sp.Activity("masterwork").Detail.Length > 0);
    Check("SeasonPack: gear spoil is Greater Equipment @400 Aether", sp.Spoil("gear").Name.Contains("Greater Equipment") && sp.Spoil("gear").Aether == 400);
    Eq("SeasonPack: Bartuc costs 666 Aether", 666, sp.Spoil("bartuc").Aether);
    Eq("SeasonPack: helm holds 2 sockets", 2, sp.SocketsFor("helm"));
    Eq("SeasonPack: gloves hold 0 sockets", 0, sp.SocketsFor("gloves"));
    Eq("SeasonPack: T1 ≈ Pit 10", 10, sp.PitForTorment(1) ?? -1);
    Eq("SeasonPack: T12 ≈ Pit 100", 100, sp.PitForTorment(12) ?? -1);
    Eq("SeasonPack: masterwork capped item needs 0 Obducite", 0, sp.ObduciteToCap(25));
    Check("SeasonPack: Obducite cost falls as Quality rises", sp.ObduciteToCap(0) > sp.ObduciteToCap(20));
    Check("SeasonPack: two-handers cost double Obducite", sp.ObduciteToCap(0, true) == sp.ObduciteToCap(0) * 2);
    // a malformed/partial override parses without throwing and overrides what it specifies
    var ov = SeasonPack.FromJson("{ \"season\": 99, \"seasonName\": \"Test\" }");
    Eq("SeasonPack: override JSON parses", 99, ov.Season);
}

// ---- Stale-term tripwire: guidance output must never reintroduce pre-2026 mechanics ----
{
    string Gather(DiffReport rep, TargetBuild tgt, LiveBuild lv)
    {
        var parts = new List<string>();
        foreach (var a in Activities.Recommend(rep)) { parts.Add(a.Title); parts.Add(a.Detail); }
        var (off, reason) = InfernalHordesAdvisor.RecommendOffering(rep, rep.TargetClass);
        parts.Add(off); parts.Add(reason);
        foreach (var s in BuildGuide.Steps(rep)) { parts.Add(s.Text); parts.Add(s.Detail ?? ""); parts.Add(s.Headline); }
        foreach (var sub in Substitutes.Plan(tgt, lv)) { parts.Add(sub.Wanted); parts.AddRange(sub.Ladder); }
        return string.Join(" ␟ ", parts);
    }
    var blob = Gather(rPart, target, livePartial);
    string[] stale = {
        "Ingolith", "summoning material", "Tormented Boss", "Sigil Powder",
        "Spoils of the Realm", "Spoils of the Vault", "Spoils of Battle",
        "Spoils of Darkness", "Spoils of Creation", "Spoils of Salvation",
        "Legendary at 46", "Nightmare Dungeons to earn Glyph",
    };
    foreach (var term in stale)
        Check($"Tripwire: guidance never says '{term}'", !blob.Contains(term, StringComparison.OrdinalIgnoreCase));
    // positive: the corrected vocabulary IS present somewhere across the guidance
    Check("Tripwire: guidance uses the real Lair Boss / Belial vocabulary", blob.Contains("Lair Boss") || blob.Contains("Belial"));
}

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

// LootFilter: a loose aspect (in Aspects but pinned to no gear piece) must still export — both markdown
// ("Other Aspects") and the companion preset. Real-data gap: a build had 8 aspects but only 7 were
// slot-bound, so the 8th was silently dropped from the exported filter.
var aspTarget = new TargetBuild
{
    Name = "AspT",
    Gear = { new TargetGear { Slot = "Helm", Aspect = "Bound Aspect", Affixes = { new TargetAffix { Name = "Dexterity" } } } },
    Aspects = { "Bound Aspect", "Loose Flex Aspect" },
};
var aspMd = LootFilter.Markdown(aspTarget);
Check("LootFilter md: bound aspect under its slot", aspMd.Contains("**Aspect:** Bound Aspect"));
Check("LootFilter md: loose aspect listed in Other Aspects", aspMd.Contains("## Other Aspects") && aspMd.Contains("Loose Flex Aspect"));
Check("LootFilter md: bound aspect NOT duplicated into Other Aspects", aspMd.IndexOf("Bound Aspect", StringComparison.Ordinal) == aspMd.LastIndexOf("Bound Aspect", StringComparison.Ordinal));
var aspPreset = System.Text.Json.JsonSerializer.Serialize(LootFilter.CompanionPreset(aspTarget));
Check("LootFilter preset: loose aspect present", aspPreset.Contains("Loose Flex Aspect"));
Check("LootFilter preset: bound aspect present", aspPreset.Contains("Bound Aspect"));

// ---- BuildGuide dedup + RE-TEMPER verb ----
var guideTarget = new TargetBuild { Gear = {
    new TargetGear { Slot = "weapon", Affixes = { new TargetAffix { Name = "Damage Over Time" } } },
    new TargetGear { Slot = "weapon", Affixes = { new TargetAffix { Name = "Damage Over Time" } } } } };
var liveMissAll = new LiveBuild();
var rMissAll = DiffEngine.Diff(guideTarget, liveMissAll);
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
var rTemper = DiffEngine.Diff(retemperTarget, liveLowRoll);
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

// A3: "+N Ranks to <Skill>" must strip the "Ranks to" connective so the clean skill name matches build
// targets (the parser used to leave affix text "Ranks to Core Skill"). +Ranks is a top-tier affix.
{
    var rankItem = GearParser.ParseTooltipLines(new[]
    {
        "STORMCALLER GRASP", "Legendary Gloves", "800 Item Power",
        "+3 Ranks to Core Skill [2 - 3]",
        "+2 Ranks to Ball Lightning [1 - 2]",
        "Requires Level 70",
    });
    Check("A3: skill-rank item parsed", rankItem != null);
    if (rankItem != null)
    {
        Check("A3: '+3 Ranks to Core Skill' -> clean 'Core Skill' (value 3)",
            rankItem.Affixes.Any(a => a.Text.Equals("Core Skill", StringComparison.Ordinal) && a.Value == 3));
        Check("A3: '+2 Ranks to Ball Lightning' -> clean 'Ball Lightning'",
            rankItem.Affixes.Any(a => a.Text.Equals("Ball Lightning", StringComparison.Ordinal)));
        Check("A3: no affix retains the 'Ranks to' connective",
            rankItem.Affixes.All(a => !a.Text.StartsWith("Ranks", StringComparison.OrdinalIgnoreCase)));
    }
}

// A1: a Mythic Unique is always Ancestral (doc §2) even when the tooltip doesn't voice "Ancestral" — so it
// can never trip the sub-900 "below the Ancestral floor" junk verdict.
{
    var mythic = GearParser.ParseTooltipLines(new[]
    {
        "TYRAEL'S MIGHT", "Mythic Unique Helm", "800 Item Power",
        "+200 All Stats [150 - 250]", "Requires Level 70",
    });
    Check("A1: mythic parsed", mythic != null);
    if (mythic != null)
    {
        Check("A1: Mythic flagged IsMythic", mythic.IsMythic);
        Check("A1: Mythic flagged Ancestral even without the word", mythic.IsAncestral);
        Check("A7: Mythic is all-GA (count = affix count) with no temper line",
            mythic.GreaterAffixCount == mythic.Affixes.Count && mythic.Affixes.Count > 0);
        Check("A7: every Mythic affix flagged IsGreater", mythic.Affixes.All(a => a.IsGreater));
    }
}

// E7: long affixed item names (>64 chars) must not be silently dropped — the name-length ceiling was 64,
// which clipped legitimately long names; raised to 96. This name is ~65 chars (fails under the old ceiling).
{
    var longName = "ADVENTURER'S CEREMONIAL WAR HELM OF THE ETERNAL MENDING OBSCURITY";
    var li = GearParser.ParseTooltipLines(new[] { longName, "Legendary Helm", "800 Item Power",
        "+100 Dexterity [80 - 120]", "Requires Level 70" });
    Check("E7: long item name parses (not dropped by the length ceiling)", li != null && li.Name.Length > 40);
}

// D2: Maxroll class detection validates the skill-token prefix against the known roster — a malformed or
// empty token stores null (fail-open), NOT a garbage class. Real tokens are "ClassName_Skill" (verified
// against live Rogue/Barbarian builds, which resolve to "Rogue"/"Barbarian").
Eq("D2: valid class prefix kept", "Rogue", MaxrollImporter.NormalizeClass("Rogue") ?? "");
Eq("D2: valid class normalized to canonical casing", "Barbarian", MaxrollImporter.NormalizeClass("barbarian") ?? "");
Check("D2: unknown/garbage class -> null", MaxrollImporter.NormalizeClass("Generic") == null);
Check("D2: empty token -> null", MaxrollImporter.NormalizeClass("") == null);

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

// Parser hardening: numeric fields (Quality/Masterwork/Temper/Requires-Level) parse via ToNum like the
// rest of the parser, so an oversized digit run degrades gracefully instead of throwing OverflowException
// and faulting the whole block. TTS format is season-volatile (CLAUDE.md), so this guards a future
// digit-gluing format change — not a reachable case today, but the asymmetry was a latent landmine.
Item? oversized = null;
try
{
    oversized = GearParser.ParseTooltipLines(new[]
    {
        "GLITCHED HELM", "Legendary Helm", "800 Item Power",
        "99999999999999 Quality", "Masterwork: 8/99999999999999",
        "Tempers: 2/99999999999999", "Requires Level 99999999999999",
        "+500 Maximum Life",
    });
}
catch { oversized = null; }   // a regression to int.Parse re-throws here -> clean test failure, not a crashed run
Check("Parser hardening: oversized numeric fields don't throw", oversized != null);
Check("Parser hardening: affix still parsed past oversized fields",
    oversized?.Affixes.Any(a => a.Text == "Maximum Life") == true);

// Regression: ordinary values still parse correctly through ToNum
var normNum = GearParser.ParseTooltipLines(new[]
{
    "NORMAL HELM", "Legendary Helm", "800 Item Power",
    "Masterwork: 8/12", "Tempers: 2/5", "Requires Level 60", "+500 Maximum Life",
});
Eq("Parser hardening: masterwork rank still 8", 8, normNum?.MasterworkRank ?? 0);
Eq("Parser hardening: masterwork max still 12", 12, normNum?.MasterworkMax ?? 0);
Eq("Parser hardening: temper max still 5", 5, normNum?.TemperMax ?? 0);
Eq("Parser hardening: requires level still 60", 60, normNum?.RequiresLevel ?? 0);

// ItemSource: LogWatcher stamps Source=Tts on all parsed items
if (File.Exists(sampleLog))
{
    var lbTts = LogWatcher.BuildFromFile(sampleLog, equippedOnly: false);
    Check("ItemSource: all TTS-parsed items stamped Tts",
        lbTts.Gear.Count > 0 && lbTts.Gear.All(g => g.Source == ItemSource.Tts));
}

// Item compact serialization stays on one line (no newlines in the JSON).
var testItem = new Item { Name = "Doom", Slot = "helm", Rarity = "Legendary",
    Affixes = { new Affix { Text = "Max Life", Value = 1000 } } };
var compact = System.Text.Json.JsonSerializer.Serialize(testItem, new System.Text.Json.JsonSerializerOptions
{
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
});
Check("Item compact JSON fits on one line", !compact.Contains('\n'));

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

    // ---- #1 item freshness: every item carries the TRUE hover time from its line's '[ISO]' prefix ----
    // The whole rogue fixture is stamped on 2026-06-05 ~23:20Z; before the fix LogTimeUtc was always null
    // (Clean dropped the prefix) and LastScannedTicks got 'now', so an old loadout looked current at launch.
    Check("Rogue: every captured item has a parsed LogTimeUtc (from its '[ISO]' prefix)",
        g.All(x => x.LogTimeUtc != null));
    Check("Rogue: LogTimeUtc lands on the fixture's recording day (2026-06-05Z)",
        g.All(x => x.LogTimeUtc!.Value.UtcDateTime.Date == new DateTime(2026, 6, 5)));
    // The bow's block is wholly stamped 23:20:06Z, so its hover time is exactly that instant.
    Eq("Rogue: bow LogTimeUtc == 2026-06-05T23:20:06Z (block's '[ISO]' time)",
        new DateTimeOffset(2026, 6, 5, 23, 20, 6, TimeSpan.Zero), bow.LogTimeUtc);
    // LastScannedTicks the app shows as 'age' must DERIVE from the log time, not the wall clock — this
    // mirrors Poll's expression so a replayed old loadout can't masquerade as 'now'.
    long derivedTicks = bow.LogTimeUtc?.UtcTicks ?? DateTime.UtcNow.Ticks;
    Eq("Rogue: bow age-ticks derive from its '[ISO]' time (Poll's formula), not the system clock",
        new DateTimeOffset(2026, 6, 5, 23, 20, 6, TimeSpan.Zero).UtcTicks, derivedTicks);
}

// ---- ParseAffix accuracy: Set-Charm capture, comparison-leak drop, skill-rank clean, mid-string recover ----
// Each block FAILS before the v0.12.x ParseAffix/LooksLikeItem accuracy pass and PASSES after. Built from the
// REAL S8 Rogue live-log strings (PHOBA OF MASTERY charm; Toughness comparison deltas; "+2 to Heartseeker";
// the "Lucky Hit ... Restore +6 Primary Resource" and "Way of the Blurring Blade:." mid-string rolls).

// (1) SET CHARM DROP — a Set Charm voices its type ("Set Charm") + affixes but NO rarity word. LooksLikeItem
//     gating on Rarity discarded the equipped charm; gating on ItemType now keeps it. (Real lines 7683-7686.)
var phoba = GearParser.ParseTooltipLines(new[] {
    "PHOBA OF MASTERY",
    "Set Charm",
    "+2 to Subterfuge Skills [1 - 2] (+2)",
    "+167 Physical Resistance [165 - 210] (+167)",
});
Check("ParseAffix #1: Set Charm with no rarity word is captured (PHOBA OF MASTERY)", phoba != null);
if (phoba != null)
{
    Eq("ParseAffix #1: charm slot", "charm", phoba.Slot ?? "");
    Eq("ParseAffix #1: charm type", "Set Charm", phoba.ItemType ?? "");
    Check("ParseAffix #1: charm has no rarity word (gated on ItemType, not Rarity)", phoba.Rarity == null);
    Check("ParseAffix #1: charm keeps the '+167 Physical Resistance' affix",
        phoba.Affixes.Any(a => a.Text.Equals("Physical Resistance", StringComparison.Ordinal) && a.Value == 167));
    // (3) "+2 to Subterfuge Skills" -> "Subterfuge Skills" (leading 'to ' stripped, no dangling connective).
    Check("ParseAffix #3: charm skill affix '+2 to Subterfuge Skills' -> clean 'Subterfuge Skills'",
        phoba.Affixes.Any(a => a.Text.Equals("Subterfuge Skills", StringComparison.Ordinal)));
    Check("ParseAffix #3: charm affix never retains the leading 'to '",
        phoba.Affixes.All(a => !a.Text.StartsWith("to ", StringComparison.OrdinalIgnoreCase)));
}
// Charm survives the FULL stateful Feed path even though its real block trails a "Properties lost when
// equipped:" comparison section (the parser still captures it; downstream routes charms separately).
{
    var raw = new[] {
        "[2026-06-05T16:48:48Z]PHOBA OF MASTERY ",
        "[2026-06-05T16:48:48Z]Set Charm",
        "[2026-06-05T16:48:48Z]+2 to Subterfuge Skills [1 - 2] (+2)",
        "[2026-06-05T16:48:48Z]+167 Physical Resistance [165 - 210] (+167)",
        "[2026-06-05T16:48:48Z]Properties lost when equipped:",
        "[2026-06-05T16:48:48Z]+8% Bonus Kill Experience (0.8% at level 70)",
        "[2026-06-05T16:48:48Z]Requires Level 54. Unique Equipped. Lord of Hatred Item ",
        "[2026-06-05T16:48:48Z]Right mouse button",
    };
    var fseg = new GearParser();
    Item? fed = null;
    foreach (var l in raw) { var r = fseg.Feed(l); if (r != null) fed = r; }
    Check("ParseAffix #1: real PHOBA block survives Feed (was dropped by the Rarity gate)",
        fed != null && fed.Slot == "charm" && fed.ItemType == "Set Charm");
}

// (2) COMPARISON-TOOLTIP LEAK — "638 Armor (-4.2% Toughness)" / "157 All Resist (-3.8% Toughness)" are the
//     D4 comparison overlay's delta, NOT real affixes. They must never become affixes (and so can't
//     false-match a target's Armor/All-Res). The real rolled "+892 Armor [780-980]" still survives.
var cmp = GearParser.ParseTooltipLines(new[] {
    "BONEWEAVE HAUBERK",
    "Legendary Chest Armor",
    "800 Item Power",
    "638 Armor (-4.2% Toughness)",
    "157 All Resist (-3.8% Toughness)",
    "+892 Armor [780 - 980] (+892)",
});
Check("ParseAffix #2: comparison-delta chest parsed", cmp != null);
if (cmp != null)
{
    Check("ParseAffix #2: no affix text ever contains 'Toughness'",
        cmp.Affixes.All(a => !a.Text.Contains("Toughness", StringComparison.OrdinalIgnoreCase)));
    Check("ParseAffix #2: the 'All Resist (-Toughness)' comparison line is not an affix",
        !cmp.Affixes.Any(a => a.Text.Contains("All Resist", StringComparison.OrdinalIgnoreCase)));
    Eq("ParseAffix #2: only the rolled '+892 Armor' Armor affix survives (the summary delta is dropped)",
        1, cmp.Affixes.Count(a => a.Text.Equals("Armor", StringComparison.OrdinalIgnoreCase)));
    Check("ParseAffix #2: the surviving Armor affix is the rolled one (has a [range])",
        cmp.Affixes.First(a => a.Text.Equals("Armor", StringComparison.OrdinalIgnoreCase)).Min == 780);
}

// (3) SKILL-RANK / CLARIFIER NOISE — "+2 to Heartseeker" -> "Heartseeker"; the "(0.8% at level 70)"
//     clarifier is stripped off "Bonus Kill Experience". (Real lines 1569, 7689.)
var skn = GearParser.ParseTooltipLines(new[] {
    "DARK SHROUD GLOVES",
    "Legendary Gloves",
    "800 Item Power",
    "+2 to Heartseeker [1 - 2] (+2)",
    "+8% Bonus Kill Experience (0.8% at level 70) [2 - 10]%[0.2 - 1.0]%",
});
Check("ParseAffix #3: skill-rank gloves parsed", skn != null);
if (skn != null)
{
    Check("ParseAffix #3: '+2 to Heartseeker' -> clean 'Heartseeker' affix",
        skn.Affixes.Any(a => a.Text.Equals("Heartseeker", StringComparison.Ordinal)));
    Check("ParseAffix #3: no affix keeps the dangling 'to Heartseeker'",
        !skn.Affixes.Any(a => a.Text.Contains("to Heartseeker", StringComparison.OrdinalIgnoreCase)));
    Check("ParseAffix #3: trailing '(... at level ...)' clarifier stripped from 'Bonus Kill Experience'",
        skn.Affixes.Any(a => a.Text.Equals("Bonus Kill Experience", StringComparison.Ordinal)));
    Check("ParseAffix #3: no affix retains a letter-bearing trailing clarifier paren",
        skn.Affixes.All(a => !(a.Text.EndsWith(")") && a.Text.Contains("level", StringComparison.OrdinalIgnoreCase))));
}

// (4) RECOVER DROPPED AFFIXES (conservative) — only the clean "value-first" seal/charm power-name shape is
//     recovered; multi-clause "Lucky Hit: Up to a …" lines (value NOT first) route to PowerText, since their
//     value can't be picked safely (taking the last number yields a wrong roll — verified on real data).
//   (4a) value-NOT-first: "Lucky Hit: Up to a 15% Chance to Restore +6 Primary Resource [6-8]" -> PowerText.
var lh = GearParser.ParseTooltipLines(new[] {
    "RESTORATIVE RING", "Legendary Ring", "800 Item Power",
    "Lucky Hit: Up to a 15% Chance to Restore +6 Primary Resource [6 - 8] (+6)",
});
Check("ParseAffix #4a: ring parsed", lh != null);
if (lh != null)
    Check("ParseAffix #4a: value-not-first Lucky-Hit line is NOT recovered as an affix (safe -> PowerText)",
        !lh.Affixes.Any(a => a.Text.Contains("Primary Resource", StringComparison.OrdinalIgnoreCase)
                          || a.Text.Contains("Chance", StringComparison.OrdinalIgnoreCase)));
//   (4a-2) the wrong-token bug: "...Slow for 2 Seconds [3.0-4.0]%" must NOT yield a bogus value-2 affix.
var slow = GearParser.ParseTooltipLines(new[] {
    "SLOWING CHARM", "Set Charm",
    "Lucky Hit: Up to a +3.5% Chance to Slow for 2 Seconds [3.0 - 4.0]% (+3.5%)",
});
if (slow != null)
    Check("ParseAffix #4a-2: 'Slow for 2 Seconds' never recovers a bogus value-2 affix",
        !slow.Affixes.Any(a => a.Value == 2));
//   (4b) seal/charm power-name roll, value + name AFTER a "Name:." prefix and even after the bracket:
//        "Way of the Blurring Blade:. +22% [13-25]% Critical Strike Damage" (real line 7759).
var bb = GearParser.ParseTooltipLines(new[] {
    "PHOBA OF MASTERY",
    "Set Charm",
    "Way of the Blurring Blade:. +22% [13 - 25]% Critical Strike Damage",
});
Check("ParseAffix #4b: power-name seal/charm roll parsed", bb != null);
if (bb != null)
    Check("ParseAffix #4b: 'Way of the Blurring Blade:. +22% [..] Critical Strike Damage' -> 'Critical Strike Damage' affix",
        bb.Affixes.Any(a => a.Text.Equals("Critical Strike Damage", StringComparison.Ordinal) && a.Value == 22 && a.IsPercent));

//   (4c) S8 Horadric Seal '%[x]' (multiplicative) value-first rolls — recover a CLEAN name, SET the multiplier
//        flag, and leak NO '[x]'/'%'/'(+…)' markup into the name (the exact real lines the first cut corrupted).
var sealMul = GearParser.ParseTooltipLines(new[] {
    "FOCUSED HORADRIC SEAL OF TOXINS", "Horadric Seal", "Requires Level 70",
    "24.0%[x] Critical Strike Damage [13.0 - 25.0]%[x] (+7.0%[x])",
});
if (sealMul != null)
{
    var csd = sealMul.Affixes.FirstOrDefault(a => a.Text.Contains("Critical Strike Damage", StringComparison.Ordinal));
    Check("ParseAffix #4c: seal '%[x]' roll recovered with a CLEAN name (no markup)",
        csd != null && csd.Text == "Critical Strike Damage");
    Check("ParseAffix #4c: seal '%[x]' roll sets IsMultiplier=true, value 24",
        csd != null && csd.IsMultiplier && csd.Value == 24);
}
var sealMul2 = GearParser.ParseTooltipLines(new[] {
    "SEAL TWO", "Horadric Seal", "Requires Level 70",
    "Spellbound Steel:. +8%[x] [7 - 10]% Shadow Damage",
});
if (sealMul2 != null)
    Check("ParseAffix #4c: 'Spellbound Steel:. +8%[x] [..] Shadow Damage' -> 'Shadow Damage' mul=true val=8",
        sealMul2.Affixes.Any(a => a.Text == "Shadow Damage" && a.IsMultiplier && a.Value == 8));
Check("ParseAffix #4c: no recovered seal affix retains '['/']'/'%'/'(' markup",
    (sealMul?.Affixes ?? new()).Concat(sealMul2?.Affixes ?? new())
        .All(a => !a.Text.Contains('[') && !a.Text.Contains(']') && !a.Text.Contains('%') && !a.Text.Contains('(')));

// (4 GATE) a multi-sentence Imprinted/legendary power that ALSO has a [range] must NOT be recovered as an
//     affix — it stays in PowerText (the ". " sentence-boundary + length gate). (Real bow power, line 104.)
var pwr = GearParser.ParseTooltipLines(new[] {
    "FROSTBITTEN MAMMALBANE BOW",
    "Unique Bow",
    "900 Item Power",
    "Enemies hit by your Stun Grenades have a chance equal to your Critical Strike Chance to be Frozen for 2 seconds. . You deal 150%[x] [100 - 150]% increased Critical Strike Damage against Frozen or Stunned enemies.",
});
Check("ParseAffix #4 gate: multi-sentence legendary power parsed", pwr != null);
if (pwr != null)
{
    Check("ParseAffix #4 gate: the multi-sentence power is NOT turned into an affix",
        pwr.Affixes.Count == 0);
    Check("ParseAffix #4 gate: the multi-sentence power stays in PowerText",
        pwr.PowerText.Any(p => p.Contains("Stun Grenades", StringComparison.OrdinalIgnoreCase)));
}

// ---- #1 item freshness: CleanWithTime parses the '[ISO]' prefix; Clean's string output is unchanged ----
{
    // Prefixed line: time is parsed, and the cleaned text is byte-identical to plain Clean().
    var cleaned = GearParser.CleanWithTime("[2026-06-06T15:37:50Z]LIFEBINDING AMULET", out var t);
    Eq("CleanWithTime: prefix stripped from text", "LIFEBINDING AMULET", cleaned);
    Eq("CleanWithTime: text identical to Clean()", GearParser.Clean("[2026-06-06T15:37:50Z]LIFEBINDING AMULET"), cleaned);
    Check("CleanWithTime: time parsed from '[ISO]' prefix", t != null);
    Eq("CleanWithTime: time == 2026-06-06T15:37:50Z",
        new DateTimeOffset(2026, 6, 6, 15, 37, 50, TimeSpan.Zero), t);
    // Un-prefixed line (old shim / sample_tts.log): no time, text still cleaned exactly as before.
    var bare = GearParser.CleanWithTime("ARCHON SPELLBLADE", out var t2);
    Check("CleanWithTime: no prefix -> null time (fall back to system clock)", t2 == null);
    Eq("CleanWithTime: un-prefixed text unchanged", "ARCHON SPELLBLADE", bare);
    Eq("CleanWithTime: un-prefixed matches Clean()", GearParser.Clean("ARCHON SPELLBLADE"), bare);
    // A bracketed-but-not-a-timestamp line must NOT be mis-parsed as a time and must keep its text.
    Eq("CleanWithTime: non-timestamp bracket text preserved",
        "[FAVORITED ITEM]. SOME BOW", GearParser.CleanWithTime("[FAVORITED ITEM]. SOME BOW", out var t3));
    Check("CleanWithTime: non-timestamp bracket -> null time", t3 == null);
}

// ---- #1 item freshness: a Feed-completed item carries LogTimeUtc from its block's prefix ----
{
    var seg = new GearParser();
    Item? built = null;
    foreach (var ln in new[] {
        "[2026-06-06T15:37:50Z]EQUIPPED",
        "[2026-06-06T15:37:50Z]FROSTBITTEN MAMMALBANE BOW",
        "[2026-06-06T15:37:50Z]Legendary Bow",
        "[2026-06-06T15:37:50Z]900 Item Power",
        "[2026-06-06T15:37:50Z]+100 Dexterity [80 - 120]",
        "[2026-06-06T15:37:50Z]Right mouse button" })
        built = seg.Feed(ln) ?? built;
    Check("Feed: completed item is non-null", built != null);
    Check("Feed: completed item carries LogTimeUtc from its '[ISO]' prefix", built?.LogTimeUtc != null);
    Eq("Feed: LogTimeUtc == 2026-06-06T15:37:50Z",
        new DateTimeOffset(2026, 6, 6, 15, 37, 50, TimeSpan.Zero), built?.LogTimeUtc);
}

// ---- #1 item freshness: an OLD-session item is OLDER than a CURRENT-session one (two-session input) ----
// One file, two sessions ~8h apart (the real log spans 07:49 and 15:37). An item hovered in the morning
// session must carry the older LogTimeUtc so it can be deprioritized/expired vs the afternoon scan.
{
    var twoSession = new[]
    {
        // morning session — old helm (slot header + Unequip tail = the real worn-hover shape; a bare
        // EQUIPPED line no longer classifies as worn since it also precedes bag/vendor hover names)
        "[2026-06-06T07:49:15Z]=== d4scanner tts shim attached v2 ===",
        "[2026-06-06T07:49:16Z]Head",
        "[2026-06-06T07:49:16Z]EQUIPPED",
        "[2026-06-06T07:49:16Z]OLD MORNING HELM",
        "[2026-06-06T07:49:16Z]Legendary Helm",
        "[2026-06-06T07:49:16Z]800 Item Power",
        "[2026-06-06T07:49:16Z]+100 Dexterity [80 - 120]",
        "[2026-06-06T07:49:16Z]Right mouse button",
        "[2026-06-06T07:49:16Z]Unequip",
        // afternoon session — new helm in the same slot
        "[2026-06-06T15:37:50Z]=== d4scanner tts shim attached v2 ===",
        "[2026-06-06T15:37:51Z]Head",
        "[2026-06-06T15:37:51Z]EQUIPPED",
        "[2026-06-06T15:37:51Z]NEW AFTERNOON HELM",
        "[2026-06-06T15:37:51Z]Legendary Helm",
        "[2026-06-06T15:37:51Z]925 Item Power",
        "[2026-06-06T15:37:51Z]+150 Dexterity [80 - 120]",
        "[2026-06-06T15:37:51Z]Right mouse button",
        "[2026-06-06T15:37:51Z]Unequip",
    };
    var rep = LogWatcher.DiagnoseLines(twoSession);
    Eq("TwoSession: both session markers counted", 2, rep.SessionMarkers);
    var oldHelm = rep.Items.First(it => it.RawName == "OLD MORNING HELM");
    var newHelm = rep.Items.First(it => it.RawName == "NEW AFTERNOON HELM");
    // DiagnoseLines exposes TtsDiagItem (no LogTimeUtc), so verify ordering/freshness via Feed directly:
    var seg2 = new GearParser();
    Item? oldItem = null, newItem = null;
    foreach (var ln in twoSession)
    {
        var it = seg2.Feed(ln);
        if (it?.RawName == "OLD MORNING HELM") oldItem = it;
        if (it?.RawName == "NEW AFTERNOON HELM") newItem = it;
    }
    Check("TwoSession: old-session helm parsed", oldItem != null);
    Check("TwoSession: new-session helm parsed", newItem != null);
    Check("TwoSession: old helm LogTimeUtc is from the morning session (07:49Z)",
        oldItem!.LogTimeUtc == new DateTimeOffset(2026, 6, 6, 7, 49, 16, TimeSpan.Zero));
    Check("TwoSession: new helm LogTimeUtc is from the afternoon session (15:37Z)",
        newItem!.LogTimeUtc == new DateTimeOffset(2026, 6, 6, 15, 37, 51, TimeSpan.Zero));
    Check("TwoSession: the prior-session item is strictly OLDER (deprioritizable/expirable)",
        oldItem!.LogTimeUtc < newItem!.LogTimeUtc);
    // Both reach the diagnostics list; the afternoon scan supersedes the morning one in the final set.
    Check("TwoSession: newest-per-slot keeps the afternoon helm",
        rep.FinalEquipped.Any(x => x.RawName == "NEW AFTERNOON HELM"));
    Check("TwoSession: morning helm is dropped from the final set (superseded)",
        !rep.FinalEquipped.Any(x => x.RawName == "OLD MORNING HELM"));
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

// ---- SOCKET capture: "Socket (N)" = total capacity, "Empty Socket" = one unfilled ----
var sockItem = GearParser.ParseTooltipLines(new[] {
    "LURKING SHELL", "Rare Chest Armor", "850 Item Power",
    "+90 Dexterity +[83 - 99]", "+434 Lightning Resistance [416 - 523]",
    "Empty Socket", "Socket (1)", "Requires Level 70" });
Check("Socket: armor block parses", sockItem != null);
Check("Socket: SocketCount = 1 from 'Socket (1)'", sockItem?.SocketCount == 1);
Eq("Socket: one 'Empty Socket' line counted", 1, sockItem?.EmptySockets ?? -1);
Check("Socket: bare socket lines do NOT leak into PowerText",
    sockItem != null && !sockItem.PowerText.Any(p => p.StartsWith("Socket") || p == "Empty Socket"));
var sockFull = GearParser.ParseTooltipLines(new[] {
    "SHADOW SHELL", "Rare Chest Armor", "850 Item Power",
    "+1,212 Maximum Life [1,016 - 1,225]", "Socket (2)", "Requires Level 70" });
Check("Socket: SocketCount = 2, EmptySockets = 0 (filled)", sockFull?.SocketCount == 2 && sockFull?.EmptySockets == 0);

// ---- SET CHARM capture: "<Set> (active/total). (T) Set:. <bonus>" (passes via the already-applied LooksLikeItem charm fix) ----
var setItem = GearParser.ParseTooltipLines(new[] {
    "PHOBA OF MASTERY", "Set Charm",
    "+2 to Subterfuge Skills [1 - 2]", "+167 Physical Resistance [165 - 210]",
    "Mastery", "Phoba of Mastery", "Fer of Mastery",
    "Mastery (0/2). (2) Set:. +2 to All Skills",
    "Requires Level 54" });
Check("Set: charm survives parse (LooksLikeItem charm fix)", setItem != null);
if (setItem != null)
{
    Eq("Set: name = 'Mastery' (member lines ignored)", "Mastery", setItem.SetName ?? "");
    Eq("Set: active = 0", 0, setItem.SetActive ?? -1);
    Eq("Set: total = 2 (piece count, not the (2) tier marker)", 2, setItem.SetTotal ?? -1);
}
var setBlade = GearParser.ParseTooltipLines(new[] {
    "LINTA OF THE BLURRING BLADE", "Set Charm",
    "6.5% Maximum Life [6.5 - 8.0]%",
    "Way of the Blurring Blade (0/5). (2) Set:. Cutthroat Skills deal 45%[x] increased damage.. (3) Set:. x. (5) Set:. y",
    "Requires Level 70" });
if (setBlade != null)
{
    Eq("Set: 5-piece set total parsed", 5, setBlade.SetTotal ?? -1);
    Eq("Set: 5-piece set name", "Way of the Blurring Blade", setBlade.SetName ?? "");
}

// ---- runeword counts as a FILLED socket (no bare Socket line) ----
var runeItem = GearParser.ParseTooltipLines(new[] {
    "SHROUDED GIFT", "Unique Pants", "850 Item Power",
    "+109 Dexterity +[83 - 99]",
    "NeoVex (200/100) - Graceful Heart of the Oak",
    "Requires Level 70" });
Check("Socket(runeword): RunewordName captured, no bare Socket line, EmptySockets 0",
    runeItem != null && runeItem.RunewordName != null && runeItem.SocketCount == null && runeItem.EmptySockets == 0);

// ---- DiffEngine: socket HAVE/NEED is conditional + presence-style (does NOT move Matched/Total) ----
{
    var tWantSock = new TargetBuild { Gear = {
        new TargetGear { Slot = "chest", Sockets = { "Rune of Invocation" },
            Affixes = { new TargetAffix { Name = "Maximum Life" } } } } };
    var liveEmpty = new LiveBuild { Gear = {
        new Item { Name = "Lurking Shell", Slot = "chest", SocketCount = 1, EmptySockets = 1,
            Affixes = { new Affix { Text = "Maximum Life", Value = 1000 } } } } };
    var grpEmpty = DiffEngine.Diff(tWantSock, liveEmpty).Categories.First(c => c.Id == "gear").Groups[0];
    Check("Diff socket: empty socket -> SocketsDone false", !grpEmpty.SocketsDone);
    Check("Diff socket: status mentions empty", grpEmpty.SocketStatus != null && grpEmpty.SocketStatus.Contains("empty"));
    var liveRune = new LiveBuild { Gear = {
        new Item { Name = "Shrouded Gift", Slot = "chest", RunewordName = "Graceful Heart of the Oak",
            SocketedRunes = { "Neo", "Vex" },
            Affixes = { new Affix { Text = "Maximum Life", Value = 1000 } } } } };
    var grpRune = DiffEngine.Diff(tWantSock, liveRune).Categories.First(c => c.Id == "gear").Groups[0];
    Check("Diff socket: runeword present -> SocketsDone true", grpRune.SocketsDone);
    var tNoSock = new TargetBuild { Gear = {
        new TargetGear { Slot = "chest", Affixes = { new TargetAffix { Name = "Maximum Life" } } } } };
    var grpNo = DiffEngine.Diff(tNoSock, liveEmpty).Categories.First(c => c.Id == "gear").Groups[0];
    Check("Diff socket: non-socket target has null SocketStatus (no behavior change)", grpNo.SocketStatus == null);
    Eq("Diff socket: presence-line does not inflate Total", grpNo.Total, grpEmpty.Total);
}

// ---- capture-health verdict (Core heuristic in DiagnoseLines) ----
{
    var brokenSweep = Enumerable.Range(0, 10)
        .SelectMany(_ => new[] { "Legendary", "850 Item Power" })   // 20 tooltip-shaped lines, 0 blocks
        .ToArray();
    var hb = LogWatcher.DiagnoseLines(brokenSweep);
    Eq("Health: broken sweep parses 0 items", 0, hb.Items.Count);
    Eq("Health: broken sweep has 0 EQUIPPED tokens", 0, hb.EquippedTokens);
    Check("Health: broken sweep counts tooltip-shaped lines", hb.TooltipShapedLines >= 8);
    Eq("Health: tooltip-shaped + no items + no EQUIPPED -> Warning", CaptureHealth.Warning, hb.Health);
    Check("Health: warning summary mentions format change", hb.HealthSummary.Contains("format"));

    // (a-2) EQUIPPED tokens present but STILL 0 parsed items (format break) -> Warning, NOT a false Healthy.
    var brokenEquipped = new[] {
        "EQUIPPED", "Legendary", "850 Item Power", "EQUIPPED", "Unique", "900 Item Power",
        "EQUIPPED", "Legendary", "800 Item Power", "EQUIPPED", "Rare", "850 Item Power",
    };
    var he = LogWatcher.DiagnoseLines(brokenEquipped);
    Check("Health: EQUIPPED tokens present but 0 items parsed", he.EquippedTokens >= 1 && he.Items.Count == 0);
    Eq("Health: EQUIPPED-with-zero-parsed -> Warning (not a false Healthy)", CaptureHealth.Warning, he.Health);

    var healthNav = new[] {
        "=== d4scanner tts shim attached ===",
        "Equipment", "Head", "EQUIPPED",
        "ARCHON SPELLBLADE", "Legendary Helm", "780 Item Power",
        "+1,540 Maximum Life [1,300 - 1,600]", "Requires Level 60",
        "Right mouse button",
    };
    var hh = LogWatcher.DiagnoseLines(healthNav);
    Eq("Health: nav fixture completed-blocks == items", hh.Items.Count, hh.CompletedBlocks);
    Check("Health: nav fixture counted the EQUIPPED token", hh.EquippedTokens >= 1);
    Eq("Health: a parsed+equipped fixture -> Healthy", CaptureHealth.Healthy, hh.Health);

    Eq("Health: empty input -> NoData", CaptureHealth.NoData, LogWatcher.DiagnoseLines(Array.Empty<string>()).Health);

    var quiet = new[] { "Main Menu", "Inventory", "Left mouse button", "Settings" };
    Eq("Health: quiet non-tooltip input -> NoPanel", CaptureHealth.NoPanel, LogWatcher.DiagnoseLines(quiet).Health);

    if (File.Exists(rogueLog))
    {
        var hr = LogWatcher.Diagnose(rogueLog);
        Check("Health: real rogue fixture parsed >0 items", hr.CompletedBlocks > 0);
        Eq("Health: real rogue fixture -> Healthy", CaptureHealth.Healthy, hr.Health);
    }
}

// ---- session expiry: a new "=== d4scanner attached" marker drops prior-session gear the new session never
//      re-hovers. Discriminating: the ring exists ONLY in session 1, so LatestPerSlot cannot fake its removal. ----
{
    var twoSession = Path.Combine(Path.GetTempPath(), "d4s_twosession_test.log");
    File.WriteAllLines(twoSession, new[] {
        "=== d4scanner tts shim attached ===",
        "Head", "EQUIPPED", "SESSION ONE HELM", "Legendary Helm", "780 Item Power",
        "+1,000 Maximum Life [900 - 1,100]", "Requires Level 60", "Right mouse button",
        "Ring", "EQUIPPED", "SESSION ONE RING", "Legendary Ring", "800 Item Power",
        "+90 Dexterity [80 - 100]", "Requires Level 60", "Right mouse button",
        "=== d4scanner tts shim attached ===",
        "Head", "EQUIPPED", "SESSION TWO HELM", "Legendary Helm", "790 Item Power",
        "+1,200 Maximum Life [900 - 1,300]", "Requires Level 60", "Right mouse button",
    });
    try
    {
        var lb2 = LogWatcher.BuildFromFile(twoSession, equippedOnly: false).Gear;
        Check("Session expiry: a prior-session ring the new session never re-hovers is dropped",
            !lb2.Any(g => g.Slot == "ring"));
        Check("Session expiry: the current-session helm is kept", lb2.Any(g => g.Name.Contains("Session Two Helm")));
        Eq("Session expiry: only the current session's gear remains", 1, lb2.Count);
    }
    finally { try { File.Delete(twoSession); } catch { } }
}

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

// ---- GearList: fingerprint id + "All Items" table (filter / sort / dedup) ----
{
    Item Mk(string name, string slot, int ip, params (string t, double v)[] affs) => new Item
    {
        Name = name, Slot = slot, ItemPower = ip,
        Affixes = affs.Select(a => new Affix { Text = a.t, Value = a.v }).ToList(),
    };

    var razorA = Mk("Crushing Jaguar's Razor", "weapon", 850, ("Dexterity", 139), ("Maximum Life", 1637));
    var razorB = Mk("Crushing Jaguar's Razor", "weapon", 850, ("Dexterity", 139), ("Maximum Life", 1637)); // same roll
    var razorC = Mk("Crushing Jaguar's Razor", "weapon", 850, ("Dexterity", 176), ("Maximum Life", 1637)); // diff dex roll
    Eq("Fingerprint: identical roll -> same id", GearList.Fingerprint(razorA), GearList.Fingerprint(razorB));
    Check("Fingerprint: different roll -> different id", GearList.Fingerprint(razorA) != GearList.Fingerprint(razorC));
    Check("Fingerprint: stable 12-hex id", GearList.Fingerprint(razorA).Length == 12);

    var helm = Mk("Adventurer's Helm", "helm", 850, ("Dexterity", 96), ("Lucky Hit Chance", 7.4));
    helm.LogTimeUtc = new DateTimeOffset(2026, 6, 8, 1, 28, 0, TimeSpan.Zero);
    var ring = Mk("Conceited Nightborne Signet", "ring", 900, ("Maximum Life", 1414));
    ring.LogTimeUtc = new DateTimeOffset(2026, 6, 8, 1, 30, 0, TimeSpan.Zero);   // newer hover

    var live = new LiveBuild { Gear = { razorA, helm, ring }, Inventory = { razorB } };  // razorB is a dup of razorA
    var all = GearList.Build(live);
    Eq("Build: dedups identical captures by fingerprint", 3, all.Count);

    // Capture-artifact filter: a completed block with an Item Power line but NO slot, type, rarity, OR affixes
    // (stale Talisman-panel "RING" residue, found in the user's real live.json) is uncategorizable junk —
    // dropped from the displayed pool. An item with even ONE of slot/type/rarity/affixes is NOT an artifact.
    var artifact = new Item { Name = "Ring", RawName = "RING", ItemPower = 850 };
    artifact.PowerText.Add("118 All Resist (-6.2% Toughness)");
    var built = GearList.Build(new LiveBuild { Gear = { razorA, helm }, Inventory = { artifact } });
    Check("Build: drops pure capture artifact", !built.Any(i => i.Name == "Ring" && string.IsNullOrEmpty(i.Slot)));
    Eq("Build: keeps the 2 real items, drops the artifact", 2, built.Count);
    var slotOnly = new Item { Name = "Partial", Slot = "ring", ItemPower = 900 };
    Eq("Build: an item with a slot is NOT an artifact (not over-eager)", 1,
        GearList.Build(new LiveBuild { Inventory = { slotOnly } }).Count);

    Check("HasAffix: PhraseMatch hit", GearList.HasAffix(helm, "Dexterity"));
    Check("HasAffix: miss", !GearList.HasAffix(ring, "Dexterity"));
    Check("AffixKeys: distinct labels include Maximum Life", GearList.AffixKeys(all).Contains("Maximum Life"));

    var byDex = GearList.Apply(all, new[] { "Dexterity" }, null, GearSortMode.Slot);
    Eq("Apply: by-affix filter keeps only Dexterity items", 2, byDex.Count);
    Check("Apply: by-affix filter drops the non-Dex ring", !byDex.Any(i => i.Name.Contains("Signet")));
    Eq("Apply: multi-affix filter requires ALL selected affixes", 1,
        GearList.Apply(all, new[] { "Dexterity", "Maximum Life" }, null, GearSortMode.Slot).Count);

    var recent = GearList.Apply(all, null, null, GearSortMode.RecentlyAcquired);
    Eq("Apply: recently-acquired puts newest hover first", "Conceited Nightborne Signet", recent[0].Name);

    Check("MatchesSearch: name hit (case-insensitive)", GearList.MatchesSearch(helm, "adventurer"));
    Check("MatchesSearch: affix hit", GearList.MatchesSearch(ring, "maximum life"));
    Check("MatchesSearch: unrelated miss", !GearList.MatchesSearch(ring, "crossbow"));
    Eq("Apply: free-text search narrows to the razor", 1, GearList.Apply(all, null, "razor", GearSortMode.Slot).Count);
}

// ---- CharacterParser: total attributes + paragon level from the character sheet ----
{
    var cp = new CharacterParser();
    string[] lines =
    {
        "[2026-06-09T05:43:37Z]Strength", "[2026-06-09T05:43:37Z]316",
        "[2026-06-09T05:43:37Z]Intelligence", "[2026-06-09T05:43:37Z]307",
        "[2026-06-09T05:43:37Z]Willpower", "[2026-06-09T05:43:37Z]162",
        "[2026-06-09T05:43:37Z]Dexterity", "[2026-06-09T05:43:37Z]1,501",
        "[2026-06-09T05:43:37Z]499,825,013 Gold",
        "[2026-06-09T05:43:33Z]PARAGON 186",
    };
    foreach (var l in lines) cp.Feed(l);
    Eq("CharacterParser: Strength", 316, cp.Character.Strength);
    Eq("CharacterParser: Dexterity (comma value)", 1501, cp.Character.Dexterity);
    Eq("CharacterParser: Intelligence", 307, cp.Character.Intelligence);
    Eq("CharacterParser: Willpower", 162, cp.Character.Willpower);
    Eq("CharacterParser: Paragon level", 186, cp.Character.ParagonLevel);

    // label not immediately followed by a value must NOT mis-capture
    var cp2 = new CharacterParser();
    cp2.Feed("[2026-06-09T05:43:37Z]Dexterity"); cp2.Feed("[2026-06-09T05:43:37Z]Left mouse button"); cp2.Feed("[2026-06-09T05:43:37Z]1234");
    Check("CharacterParser: ignores value not adjacent to label", cp2.Character.Dexterity == null);

    // gold/obol numbers after stats must not overwrite an attribute
    var cp3 = new CharacterParser();
    cp3.Feed("[2026-06-09T05:43:37Z]Strength"); cp3.Feed("[2026-06-09T05:43:37Z]316"); cp3.Feed("[2026-06-09T05:43:37Z]499,825,013 Gold");
    Eq("CharacterParser: gold line ignored", 316, cp3.Character.Strength);
}

// ---- SkillParser: skill/passive name + rank from the skill tree ----
{
    var sp = new SkillParser();
    string[] lines =
    {
        "[2026-06-09T05:43:37Z]Dance of Knives", "[2026-06-09T05:43:37Z]RANK 20/15", "[2026-06-09T05:43:37Z](Item Contribution: 5)", "[2026-06-09T05:43:37Z]Core",
        "[2026-06-09T05:43:37Z]Unhindered characters can move through enemies.", "[2026-06-09T05:43:37Z]Concealment", "[2026-06-09T05:43:37Z]RANK 17/15",
        "[2026-06-09T05:43:37Z]Smoke Grenade", "[2026-06-09T05:43:37Z]RANK 11/15",
    };
    foreach (var l in lines) sp.Feed(l);
    var sk = sp.Skills;
    Eq("SkillParser: captured 3 skills", 3, sk.Count);
    Eq("SkillParser: Dance of Knives rank", 20, sk.First(s => s.Name == "Dance of Knives").Rank);
    Eq("SkillParser: Dance of Knives base max (the /Y in RANK X/Y)", 15, sk.First(s => s.Name == "Dance of Knives").BaseMax);
    Eq("SkillParser: Concealment rank", 17, sk.First(s => s.Name == "Concealment").Rank);
    Check("SkillParser: prose line before RANK isn't mis-captured as a skill",
        !sk.Any(s => s.Name.StartsWith("Unhindered")));

    // re-hover updates the rank (dedup by name), and a bare RANK with no preceding name is ignored
    var sp2 = new SkillParser();
    sp2.Feed("[2026-06-09T05:43:37Z]Concealment"); sp2.Feed("[2026-06-09T05:43:37Z]RANK 5/15");
    sp2.Feed("[2026-06-09T05:43:37Z]Concealment"); sp2.Feed("[2026-06-09T05:43:37Z]RANK 17/15");
    Eq("SkillParser: dedup keeps latest rank", 1, sp2.Skills.Count);
    Eq("SkillParser: latest rank wins", 17, sp2.Skills[0].Rank);
    Eq("SkillParser: base max captured alongside rank", 15, sp2.Skills[0].BaseMax);
}

// ---- AffixAggregate (Gear & Affixes overview roll-up) ----
{
    Group G(params ReqItem[] items) => new Group { Kind = "gear", Items = items.ToList() };
    var cat = new Category
    {
        Id = "gear",
        Groups = new()
        {
            // Helm: Max Life met (+1500, max roll 1600); DoT mult met (x50%, max roll 60)
            G(new ReqItem { Label = "Maximum Life", Status = "met", Done = true, ValueNum = 1500, MaxNum = 1600 },
              new ReqItem { Label = "Damage Over Time Multiplier", Status = "met", Done = true, ValueNum = 50, MaxNum = 60, IsMultiplier = true, IsPercent = true }),
            // Chest: Max Life under (+800, max roll 1600, 40% roll); DoT mult missing (no range captured)
            G(new ReqItem { Label = "Maximum Life", Status = "under", Done = true, ValueNum = 800, MaxNum = 1600, RollPct = 40 },
              new ReqItem { Label = "Damage Over Time Multiplier", Status = "missing", Done = false }),
        },
    };
    var agg = AffixAggregate.ForGear(cat);
    Eq("AffixAggregate: 2 distinct affixes", 2, agg.Count);

    // Maximum Life: target = max roll (1600) × 2 pieces = 3200; have = 1500+800 = 2300
    var life = agg.First(a => a.Name == "Maximum Life");
    Eq("AffixAggregate: life target pieces", 2, life.TargetPieces);
    Eq("AffixAggregate: life have pieces (met+under)", 2, life.HavePieces);
    Eq("AffixAggregate: life met pieces", 1, life.MetPieces);
    Eq("AffixAggregate: life haveTotal sum", 2300.0, life.HaveTotal);
    Eq("AffixAggregate: life target = max roll × pieces (1600×2)", 3200.0, life.WantsTotal);
    Check("AffixAggregate: life target is real (a max was captured)", life.WantsKnown);
    Eq("AffixAggregate: life status under (not all met)", "under", life.Status);
    Eq("AffixAggregate: life progress = 2300/3200", Math.Round(100.0 * 2300 / 3200, 6), Math.Round(life.ProgressPct, 6));

    // DoT mult: target = max roll (60) × 2 pieces = 120 even though only 1 piece is owned; have = 50
    var dot = agg.First(a => a.Name == "Damage Over Time Multiplier");
    Eq("AffixAggregate: dot have pieces", 1, dot.HavePieces);
    Eq("AffixAggregate: dot haveTotal", 50.0, dot.HaveTotal);
    Eq("AffixAggregate: dot multiplier prefix", "x", dot.Prefix);
    Eq("AffixAggregate: dot percent suffix", "%", dot.Suffix);
    Eq("AffixAggregate: dot fmt", "x50%", dot.Fmt(dot.HaveTotal));
    Check("AffixAggregate: dot target is the captured max roll (no estimate)", dot.WantsKnown);
    Eq("AffixAggregate: dot target = max roll × pieces (60×2)", 120.0, dot.WantsTotal);
    Eq("AffixAggregate: dot progress = 50/120", Math.Round(100.0 * 50 / 120, 6), Math.Round(dot.ProgressPct, 6));

    // an affix owned on one piece with a captured range: the max roll IS the target (10) on every wanted piece
    var q = AffixAggregate.ForGear(new Category { Id = "gear", Groups = new()
        { G(new ReqItem { Label = "Crit", Status = "met", Done = true, ValueNum = 5, MaxNum = 10 },
            new ReqItem { Label = "Crit", Status = "missing", Done = false }) } });
    Check("AffixAggregate: max-roll target is real", q[0].WantsKnown);
    Eq("AffixAggregate: target = max roll × pieces (10×2)", 20.0, q[0].WantsTotal);
    Eq("AffixAggregate: progress vs max-roll goal (5/20)", 25.0, q[0].ProgressPct);

    // no captured range anywhere → no max target; the bar falls back to blended roll quality, value shows none
    var q2 = AffixAggregate.ForGear(new Category { Id = "gear", Groups = new()
        { G(new ReqItem { Label = "Crit", Status = "met", Done = true },
            new ReqItem { Label = "Crit", Status = "missing", Done = false }) } });
    Check("AffixAggregate: no range captured -> no max target", !q2[0].WantsKnown && q2[0].WantsTotal == 0);
    Eq("AffixAggregate: no range -> quality-based progress (met 100, missing 0 -> 50)", 50.0, q2[0].ProgressPct);

    // a fully-missing affix reads as missing with zero progress
    var miss = AffixAggregate.ForGear(new Category { Id = "gear", Groups = new()
        { G(new ReqItem { Label = "All Damage Multiplier", Status = "missing", Done = false }) } });
    Eq("AffixAggregate: missing affix status", "missing", miss[0].Status);
    Eq("AffixAggregate: missing affix progress 0", 0.0, miss[0].ProgressPct);
    Check("AffixAggregate: missing affix has no haveTotal", !miss[0].HaveAny);
}

// ---- SelfRows (unique items' own affixes) + skills Need (no blanket "wants 1") ----
{
    var uniq = new Item { Name = "Etna's Lost Dagger", Slot = "weapon", IsUnique = true, Affixes = {
        new Affix { Text = "Critical Strike Chance", Value = 9, IsPercent = true, Min = 5, Max = 10 },
        new Affix { Text = "Maximum Life", Value = 1500 } } };
    var rows = DiffEngine.SelfRows(uniq);
    Eq("SelfRows: one row per affix", 2, rows.Count);
    Eq("SelfRows: value formatted", "+9%", rows[0].Val);
    Eq("SelfRows: roll quality computed from the range", 80.0, Math.Round(rows[0].RollPct ?? -1));
    Check("SelfRows: rows read as met with no target", rows.All(x => x.Status == "met" && x.Need == null));

    var skT = new TargetBuild { Skills = {
        new TargetSkill { Name = "Dance of Knives", Rank = 5 },
        new TargetSkill { Name = "Concealment" } } };           // no explicit rank from the planner
    var skL = new LiveBuild { Skills = { new LiveSkill { Name = "Dance of Knives", Rank = 20 } } };
    var rep = DiffEngine.Diff(skT, skL);
    var sk = rep.Categories.First(c => c.Id == "skills").Groups[0].Items;
    Eq("Skills: explicit rank target shows", "≥ 5", sk[0].Need);
    Check("Skills: planner rank omitted -> NO 'wants 1' target", sk[1].Need == null);
}

// ---- UpgradeScorer (All Items scored upgrade list) ----
{
    var upTarget = new TargetBuild { Gear =
    {
        new TargetGear { Slot = "Helm", Affixes = { new TargetAffix { Name = "Maximum Life", Min = 1000 }, new TargetAffix { Name = "Dexterity" } } },
        new TargetGear { Slot = "Ring", Affixes = { new TargetAffix { Name = "Critical Strike Chance" }, new TargetAffix { Name = "Maximum Life", Min = 1000 } } },
    } };

    var weights = UpgradeScorer.GoalWeights(upTarget);
    Eq("GoalWeights: Maximum Life wanted on 2 slots", 2, weights["Maximum Life"]);
    Eq("GoalWeights: Dexterity wanted on 1 slot", 1, weights["Dexterity"]);
    Eq("GoalWeights: Critical Strike Chance wanted on 1 slot", 1, weights["Critical Strike Chance"]);

    // equipped helm has only Maximum Life met (no Dexterity) -> the bar to beat for the helm slot is 1
    var equippedHelm = new Item { Name = "Old Helm", Slot = "Helm", Equipped = true, Affixes = { new Affix { Text = "Maximum Life", Value = 1200 } } };
    var upLive = new LiveBuild { Gear = { equippedHelm } };

    var helm3 = new Item { Name = "Helm3", Slot = "Helm", ItemPower = 800, Affixes = {
        new Affix { Text = "Maximum Life", Value = 1500 }, new Affix { Text = "Dexterity", Value = 60 }, new Affix { Text = "Critical Strike Chance", Value = 5 } } };
    var helm2 = new Item { Name = "Helm2", Slot = "Helm", ItemPower = 800, Affixes = {
        new Affix { Text = "Maximum Life", Value = 1500 }, new Affix { Text = "Dexterity", Value = 60 } } };
    var helm1 = new Item { Name = "Helm1", Slot = "Helm", ItemPower = 800, Affixes = {
        new Affix { Text = "Maximum Life", Value = 800 }, new Affix { Text = "Dexterity", Value = 60 } } };   // Max Life under 1000
    var ringJunk = new Item { Name = "RingJunk", Slot = "Ring", ItemPower = 800, Affixes = { new Affix { Text = "Strength", Value = 100 } } };
    var candidates = new List<Item> { ringJunk, helm1, helm2, helm3 };   // deliberately unsorted

    var scored = UpgradeScorer.Score(upTarget, upLive, candidates);
    Eq("UpgradeScorer: 4 items scored", 4, scored.Count);
    // Helm2/Helm3 tie on every key (affixes, IP, rarity) — name order decides; goal score no longer sorts
    Eq("UpgradeScorer: rank 1 is Helm2 (full tie -> name order)", "Helm2", scored[0].Item.Name);
    Eq("UpgradeScorer: rank 2 is Helm3", "Helm3", scored[1].Item.Name);
    Eq("UpgradeScorer: rank 3 is Helm1 (present 2 but under-rolled life)", "Helm1", scored[2].Item.Name);
    Eq("UpgradeScorer: rank 4 is RingJunk (nothing present)", "RingJunk", scored[3].Item.Name);

    Eq("UpgradeScorer: Helm3 present count", 2, scored.First(x => x.Item.Name == "Helm3").SlotPresent);
    Eq("UpgradeScorer: Helm3 goal score (life 2 + dex 1 + crit 1)", 4.0, scored.First(x => x.Item.Name == "Helm3").GoalScore);
    Check("UpgradeScorer: Helm3 is an upgrade", scored[0].IsUpgrade);
    Check("UpgradeScorer: Helm1 IS an upgrade now — presence at any value beats the equipped 1/2",
        scored[2].IsUpgrade);
    Check("UpgradeScorer: RingJunk is NOT an upgrade", !scored[3].IsUpgrade);
    Eq("UpgradeScorer: equipped-present bar exposed", 1, scored[0].EquippedPresent);

    // count dominance: 3 wanted affixes at terrible rolls outrank 2 at perfect rolls
    var four = new TargetBuild { Gear = { new TargetGear { Slot = "Chest", Affixes = {
        new TargetAffix { Name = "W", MinPercent = 80 }, new TargetAffix { Name = "X", MinPercent = 80 },
        new TargetAffix { Name = "Y", MinPercent = 80 }, new TargetAffix { Name = "Z", MinPercent = 80 } } } } };
    Affix Lo(string n) => new() { Text = n, Value = 1, Min = 0, Max = 100 };     // 1% roll — fails the gate
    Affix Hi(string n) => new() { Text = n, Value = 99, Min = 0, Max = 100 };    // 99% roll
    var threeLow = new Item { Name = "ThreeLow", Slot = "Chest", Affixes = { Lo("W"), Lo("X"), Lo("Y") } };
    var twoHigh = new Item { Name = "TwoHigh", Slot = "Chest", Affixes = { Hi("W"), Hi("X") } };
    var fourLow = new Item { Name = "FourLow", Slot = "Chest", Affixes = { Lo("W"), Lo("X"), Lo("Y"), Lo("Z") } };
    var s2 = UpgradeScorer.Score(four, new LiveBuild(), new[] { twoHigh, threeLow, fourLow });
    Eq("UpgradeScorer: 3-of-4 at any roll outranks 2-of-4 at perfect rolls",
        "TwoHigh", s2[2].Item.Name);
    Check("UpgradeScorer: one-short item is fixable (enchant credit)", s2.First(x => x.Item.Name == "ThreeLow").Fixable);
    // the fixable 3/4 with high rolls beats a complete 4/4 with terrible rolls (eff tier equal, met/quality decide)
    var threeHigh = new Item { Name = "ThreeHigh", Slot = "Chest", Affixes = { Hi("W"), Hi("X"), Hi("Y") } };
    var s3 = UpgradeScorer.Score(four, new LiveBuild(), new[] { fourLow, threeHigh });
    Eq("UpgradeScorer: hot 3/4 (fixable) outranks cold 4/4 in the same tier", "ThreeHigh", s3[0].Item.Name);

    // aspect rule: uniques can't take an imprinted aspect, so they can't upgrade over a non-unique
    var asp = new TargetBuild { Gear = { new TargetGear { Slot = "Gloves", Aspect = "Edgemaster's", Affixes = {
        new TargetAffix { Name = "Attack Speed" }, new TargetAffix { Name = "Dexterity" } } } } };
    var eqGloves = new Item { Name = "Plain Gloves", Slot = "Gloves", Equipped = true, Affixes = { new Affix { Text = "Attack Speed", Value = 5 } } };
    var uniqGloves = new Item { Name = "Fists of Fate", Slot = "Gloves", IsUnique = true, Affixes = {
        new Affix { Text = "Attack Speed", Value = 9 }, new Affix { Text = "Dexterity", Value = 60 } } };
    var rareGloves = new Item { Name = "Rare Gloves", Slot = "Gloves", Affixes = {
        new Affix { Text = "Attack Speed", Value = 9 }, new Affix { Text = "Dexterity", Value = 60 } } };
    var s4 = UpgradeScorer.Score(asp, new LiveBuild { Gear = { eqGloves } }, new[] { uniqGloves, rareGloves });
    Check("UpgradeScorer: unique is aspect-blocked on an aspect slot", s4.First(x => x.Item.IsUnique).AspectBlocked);
    Check("UpgradeScorer: unique is NOT an upgrade over a non-unique on an aspect slot",
        !s4.First(x => x.Item.IsUnique).IsUpgrade);
    Check("UpgradeScorer: same affixes on a rare ARE an upgrade there", s4.First(x => !x.Item.IsUnique).IsUpgrade);
    Eq("UpgradeScorer: the rare sorts above the aspect-blocked unique", "Rare Gloves", s4[0].Item.Name);

    // upgrades always sort above non-upgrades, even when the non-upgrade has fancier numbers
    var two = new TargetBuild { Gear = {
        new TargetGear { Slot = "Helm", Affixes = { new TargetAffix { Name = "A" }, new TargetAffix { Name = "B" }, new TargetAffix { Name = "C" } } },
        new TargetGear { Slot = "Boots", Affixes = { new TargetAffix { Name = "D" } } } } };
    var eqHelmPerfect = new Item { Name = "PerfectHelm", Slot = "Helm", Equipped = true, Affixes = { Hi("A"), Hi("B"), Hi("C") } };
    var helmAlmost = new Item { Name = "AlmostHelm", Slot = "Helm", Affixes = { Hi("A"), Hi("B"), Hi("C") } };   // ties equipped — not an upgrade
    var bootsUp = new Item { Name = "BootsUp", Slot = "Boots", Affixes = { Hi("D") } };                          // empty slot — upgrade
    var s5 = UpgradeScorer.Score(two, new LiveBuild { Gear = { eqHelmPerfect } }, new[] { helmAlmost, bootsUp });
    Check("UpgradeScorer: the modest UPGRADE sorts above the impressive non-upgrade",
        s5[0].Item.Name == "BootsUp" && s5[0].IsUpgrade && !s5[1].IsUpgrade);
}

// ---- RosterParser + CharacterResolver (multi-character identity) ----
{
    var e1 = RosterParser.ParseLine("[2026-06-05T23:20:06Z]MementoMori | 70 (220) (VII)");
    Check("RosterParser: parses a roster line", e1 != null);
    Eq("RosterParser: name", "MementoMori", e1!.Name);
    Eq("RosterParser: level", 70, e1.Level);
    Eq("RosterParser: paragon", 220, e1.Paragon);
    Eq("RosterParser: tier", "VII", e1.Tier);

    var e2 = RosterParser.ParseLine("Memento Mori | 65 (208) (VI)");
    Eq("RosterParser: name with a space", "Memento Mori", e2!.Name);
    Eq("RosterParser: paragon (spaced name)", 208, e2.Paragon);

    Check("RosterParser: non-roster prose is null", RosterParser.ParseLine("Torment VII") == null);
    Check("RosterParser: a plain item name is null", RosterParser.ParseLine("FROSTBITTEN MAMMALBANE BOW") == null);

    var rp = new RosterParser();
    rp.Feed("Zuri | 70 (208) (VI)");
    rp.Feed("MementoMori | 70 (220) (VII)");
    Eq("RosterParser: two characters captured", 2, rp.Entries.Count);
    rp.Feed("Zuri | 70 (210) (VII)");   // re-voiced with a new paragon
    Eq("RosterParser: re-voiced character updates in place (no dup)", 2, rp.Entries.Count);
    Eq("RosterParser: updated paragon", 210, rp.Entries.First(e => e.Name == "Zuri").Paragon);

    var roster = rp.Entries;
    Eq("CharacterResolver: paragon 220 -> MementoMori", "MementoMori", CharacterResolver.ByParagon(roster, 220)!.Name);
    Eq("CharacterResolver: paragon 210 -> Zuri", "Zuri", CharacterResolver.ByParagon(roster, 210)!.Name);
    Check("CharacterResolver: unknown paragon -> null", CharacterResolver.ByParagon(roster, 999) == null);
    Check("CharacterResolver: null paragon -> null", CharacterResolver.ByParagon(roster, null) == null);
    var dup = new List<RosterEntry> { new() { Name = "A", Paragon = 50 }, new() { Name = "B", Paragon = 50 } };
    Check("CharacterResolver: ambiguous paragon -> null", CharacterResolver.ByParagon(dup, 50) == null);

    // Resolve(): unique => Resolved, collision => Ambiguous (prompt), none => None
    var u = CharacterResolver.Resolve(roster, 220);
    Eq("Resolve: unique paragon kind", CharacterResolver.IdKind.Resolved, u.Kind);
    Eq("Resolve: unique paragon name", "MementoMori", u.Name);
    var amb = CharacterResolver.Resolve(dup, 50);
    Eq("Resolve: collision kind is Ambiguous", CharacterResolver.IdKind.Ambiguous, amb.Kind);
    Eq("Resolve: collision exposes both candidates", 2, amb.Candidates.Count);
    Eq("Resolve: no match kind is None", CharacterResolver.IdKind.None, CharacterResolver.Resolve(roster, 999).Kind);

    // ReconcileOwn(): match a saved profile by the player's OWN paragon (+ class tiebreak), never a roster line
    var profs = new List<CharacterProfile>
    {
        new() { Slug = "rogue1", Name = "Zuri", Class = "Rogue", Paragon = 210 },
        new() { Slug = "barb1", Name = "Bob", Class = "Barbarian", Paragon = 300 },
        new() { Slug = "barb2", Name = "Bill", Class = "Barbarian", Paragon = 300 },
    };
    Eq("ReconcileOwn: unique paragon -> that profile", "rogue1", CharacterResolver.ReconcileOwn(profs, 210, null)!.Slug);
    Check("ReconcileOwn: same-paragon same-class with no class hint -> null (no guess)", CharacterResolver.ReconcileOwn(profs, 300, null) == null);
    Check("ReconcileOwn: same-paragon two Barbarians even with class hint -> still null", CharacterResolver.ReconcileOwn(profs, 300, "Barbarian") == null);
    Eq("ReconcileOwn: class tiebreak picks the matching class", "rogue1", CharacterResolver.ReconcileOwn(profs, 210, "Rogue")!.Slug);
    Check("ReconcileOwn: unknown paragon -> null", CharacterResolver.ReconcileOwn(profs, 999, null) == null);
}

// ---- ClassDetector (weapon + skill inference) ----
{
    var bowGuy = new LiveBuild { Gear = { new Item { Name = "Some Bow", ItemType = "Bow", Slot = "ranged" } } };
    Eq("ClassDetector: a bow implies Rogue", "Rogue", ClassDetector.Detect(bowGuy));
    var plain = new LiveBuild { Gear = { new Item { Name = "Helm", ItemType = "Helm", Slot = "helm" } } };
    Check("ClassDetector: non-class-locked gear -> null", ClassDetector.Detect(plain) == null);
    // Barbarian has NO class-locked weapon — only its skills identify it
    var barb = new LiveBuild { Gear = { new Item { Name = "Some Sword", ItemType = "Sword", Slot = "weapon" } },
        Skills = { new LiveSkill { Name = "Whirlwind", Rank = 5 } } };
    Eq("ClassDetector: a Barbarian skill implies Barbarian (despite a shared-weapon sword)", "Barbarian", ClassDetector.Detect(barb));
    var rogueSkill = new LiveBuild { Skills = { new LiveSkill { Name = "Dance of Knives", Rank = 20 } } };
    Eq("ClassDetector: a Rogue skill implies Rogue", "Rogue", ClassDetector.Detect(rogueSkill));
}

// ---- CharSelectParser (identity from the character-select screen; verbatim shapes from a real log) ----
{
    Check("CharSelectParser: valid char name", CharSelectParser.IsValidCharName("Heoki"));
    Check("CharSelectParser: clan-decorated name invalid", !CharSelectParser.IsValidCharName("<1p1> Vespera"));
    Check("CharSelectParser: spaced name invalid", !CharSelectParser.IsValidCharName("My Character"));
    Check("CharSelectParser: empty invalid", !CharSelectParser.IsValidCharName(""));
    // the footer uses U+00A0 separators in the raw log — must still be recognized after Clean
    Check("CharSelectParser: NBSP footer recognized",
        CharSelectParser.IsCharSelectMarker(GearParser.Clean("R Undo Character Delete")));

    // full visit (verbatim from a real 2026-06-09 log): footer -> detail blocks (class voiced!) -> START GAME -> QUEUED
    var cs = new CharSelectParser();
    int visits = 0; CharSelectIdentity? confirmed = null;
    cs.VisitStarted += () => visits++;
    cs.Confirmed += id => confirmed = id;
    var visit = new[]
    {
        "[2026-06-09T08:04:18Z]R Undo Character Delete",
        "[2026-06-09T08:04:20Z]Heoki", "[2026-06-09T08:04:20Z]Seasonal", "[2026-06-09T08:04:20Z]186",
        "[2026-06-09T08:04:20Z]CREATE NEW CHARACTER",
        // the create-character class list voices bare class names — these must NOT become identities
        "Warlock", "Paladin", "Spiritborn", "Barbarian", "Sorcerer", "Necromancer", "Druid", "Rogue",
        "[2026-06-09T08:04:54Z]HEOKI", "[2026-06-09T08:04:54Z]Eternal", "[2026-06-09T08:04:54Z]Barbarian",
        "[2026-06-09T08:04:54Z]Level 1", "[2026-06-09T08:04:54Z](83)", "[2026-06-09T08:04:54Z]Normal",
        "[2026-06-09T08:04:54Z]R Undo Character Delete      S Change Campaign State      D Delete Character      C Change Difficulty",
        "[2026-06-09T08:05:16Z]HEOKI", "[2026-06-09T08:05:16Z]Seasonal", "[2026-06-09T08:05:16Z]Rogue",
        "[2026-06-09T08:05:16Z]Paragon 186", "[2026-06-09T08:05:16Z]Torment XI",
        "[2026-06-09T08:05:16Z]R Undo Character Delete      D Delete Character      C Change Difficulty",
        "[2026-06-09T08:05:17Z]START GAME",
        "[2026-06-09T08:05:22Z]QUEUED FOR GAME - START GAME PENDING...",
    };
    foreach (var l in visit) cs.Feed(l);
    Eq("CharSelectParser: one visit", 1, visits);
    Check("CharSelectParser: confirmed an identity on world entry", confirmed != null);
    Eq("CharSelectParser: confirmed name", "HEOKI", confirmed!.Name);
    Eq("CharSelectParser: confirmed CLASS (last highlighted before START GAME)", "Rogue", confirmed.Class);
    Eq("CharSelectParser: confirmed paragon", 186, confirmed.Paragon);
    Eq("CharSelectParser: confirmed Torment tier (XI → 11)", 11, confirmed.Torment ?? -1);
    Eq("CharSelectParser: confirmed realm", "Seasonal", confirmed.Realm);
    Eq("CharSelectParser: both highlighted characters recorded", 2, cs.Seen.Count);
    Check("CharSelectParser: Barbarian detail block captured too",
        cs.Seen.Any(s => s.Class == "Barbarian" && s.Level == 1));
    Check("CharSelectParser: create-character class list produced no identities",
        cs.Seen.All(s => s.Name.Equals("HEOKI", StringComparison.OrdinalIgnoreCase)));
    Check("CharSelectParser: gate closed after entering world", !cs.InCharSelect);

    // entering without highlighting (real 2026-06-05 23:19 visit): list rows only -> name confirmed, class null
    var cs2 = new CharSelectParser();
    CharSelectIdentity? c2 = null; cs2.Confirmed += id => c2 = id;
    foreach (var l in new[]
    {
        "[2026-06-05T23:18:51Z]R Undo Character Delete",
        "[2026-06-05T23:19:18Z]START GAME",
        "[2026-06-05T23:19:19Z]Heoki", "[2026-06-05T23:19:19Z]Seasonal", "[2026-06-05T23:19:19Z]171",
        "[2026-06-05T23:19:23Z]QUEUED FOR GAME - START GAME PENDING...",
    }) cs2.Feed(l);
    Check("CharSelectParser: list-row fallback confirms the name", c2 != null && c2.Name == "Heoki");
    Check("CharSelectParser: list-row fallback has no class", c2!.Class == null);

    // in-game nameplates never start a visit or confirm anything
    var cs3 = new CharSelectParser();
    int v3 = 0; cs3.VisitStarted += () => v3++;
    foreach (var l in new[]
    {
        "[2026-06-05T15:39:17Z]Basaba | 70 (234) (VII)",
        "[2026-06-05T15:39:19Z]&lt;Muld&gt; Sverren | 70 (211) (VII)",
        "[2026-06-05T15:39:19Z]Forzajuve | 70 (131) (VII)",
    }) cs3.Feed(l);
    Check("CharSelectParser: nameplates never open a char-select visit", v3 == 0 && !cs3.InCharSelect);
}

// ---- ProfileStore ----
{
    var root = Path.Combine(Path.GetTempPath(), "d4scanner-test-" + Guid.NewGuid().ToString("N"));
    var store = new ProfileStore(root);

    Eq("ProfileStore: empty to start", 0, store.All().Count);
    Eq("ProfileStore: Slugify", "memento-mori", ProfileStore.Slugify("Memento Mori!"));

    var prof = new CharacterProfile { Name = "Zuri", Paragon = 208, TargetPath = @"C:\builds\dance-of-knives.json", TargetSource = "dance-of-knives", Live = new LiveBuild { Gear = { new Item { Name = "Helm", Slot = "helm" } } } };
    store.Save(prof);
    Eq("ProfileStore: slug auto-filled on save", "zuri", prof.Slug);
    var got = store.Get("zuri");
    Check("ProfileStore: round-trips", got != null);
    Eq("ProfileStore: round-trip name", "Zuri", got!.Name);
    Eq("ProfileStore: round-trip gear", 1, got.Live.Gear.Count);
    Eq("ProfileStore: round-trip target build path", @"C:\builds\dance-of-knives.json", got.TargetPath);
    Eq("ProfileStore: round-trip target source", "dance-of-knives", got.TargetSource);

    store.ActiveSlug = "zuri";
    Eq("ProfileStore: active pointer persists", "zuri", store.ActiveSlug);

    store.Save(new CharacterProfile { Name = "Bob", LastSeenUtcTicks = DateTime.UtcNow.Ticks + 1000 });
    Eq("ProfileStore: two profiles", 2, store.All().Count);
    Eq("ProfileStore: most-recent first", "Bob", store.All()[0].Name);

    // legacy migration: a fresh store with a legacy live.json and no profiles
    var root2 = Path.Combine(Path.GetTempPath(), "d4scanner-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root2);
    var legacy = Path.Combine(root2, "live.json");
    File.WriteAllText(legacy, System.Text.Json.JsonSerializer.Serialize(
        new LiveBuild { Gear = { new Item { Name = "Old Helm", Slot = "helm" } }, Character = new LiveCharacter { ParagonLevel = 150 } },
        D4Scanner.Core.Json.Opts));
    var store2 = new ProfileStore(Path.Combine(root2, "profiles"));
    var migrated = store2.MigrateLegacy(legacy);
    Check("ProfileStore: legacy migrated", migrated != null);
    Eq("ProfileStore: migrated gear preserved", 1, migrated!.Live.Gear.Count);
    Eq("ProfileStore: migrated paragon captured", 150, migrated.Paragon);
    Eq("ProfileStore: migration sets active", migrated.Slug, store2.ActiveSlug);
    Check("ProfileStore: second migration is a no-op (profiles exist)", store2.MigrateLegacy(legacy) == null);
}

// ---- LogWatcher end-to-end: identity from char-select; nameplates are noise; gear survives town ----
{
    var idLog = Path.Combine(Path.GetTempPath(), "d4s_identity_test_" + Guid.NewGuid().ToString("N") + ".log");
    File.WriteAllLines(idLog, new[]
    {
        "=== d4scanner tts shim attached ===",
        // character select: highlight the Rogue, enter the world
        "R Undo Character Delete",
        "HEOKI", "Seasonal", "Rogue", "Paragon 186", "Torment XI",
        "R Undo Character Delete      D Delete Character      C Change Difficulty",
        "START GAME",
        "QUEUED FOR GAME - START GAME PENDING...",
        // in town: other players' nameplates stream by (incl. clan-tagged, contiguous pairs)
        "Basaba | 70 (234) (VII)",
        "&lt;Muld&gt; Sverren | 70 (211) (VII)",
        "Forzajuve | 70 (131) (VII)",
        // then the player hovers an equipped item
        "Helm", "EQUIPPED", "ARCHON SPELLBLADE", "Legendary Helm", "780 Item Power",
        "+1,540 Maximum Life [1,300 - 1,600]", "Right mouse button",
    });
    var lb = LogWatcher.BuildFromFile(idLog, equippedOnly: false);
    Eq("LogWatcher: own roster = chars seen at char-select (not nameplates)", 1, lb.Roster.Count);
    Eq("LogWatcher: own roster carries the CLASS", "Rogue", lb.Roster[0].Class);
    Eq("LogWatcher: own roster name", "HEOKI", lb.Roster[0].Name);
    Check("LogWatcher: nameplates are not in the roster", !lb.Roster.Any(e => e.Name.Contains("Basaba") || e.Name.Contains("Sverren")));
    Check("LogWatcher: nameplates are not parsed as gear", !lb.Gear.Any(g => g.Name.Contains("|")));
    Check("LogWatcher: gear scanned AFTER town nameplates survives (no bogus wipe)",
        lb.Gear.Any(g => g.Name.Contains("ARCHON", StringComparison.OrdinalIgnoreCase)));
    try { File.Delete(idLog); } catch { }

    // Regression (found by live verification): a char-select visit must reset the CHARACTER SHEET and
    // SKILLS too — stale Rogue paragon/skills after switching to the Barbarian pulled identity back to
    // the Rogue via the paragon reconciler.
    var swLog = Path.Combine(Path.GetTempPath(), "d4s_switch_test_" + Guid.NewGuid().ToString("N") + ".log");
    File.WriteAllLines(swLog, new[]
    {
        "=== d4scanner tts shim attached ===",
        // playing the Rogue: char sheet + a skill captured
        "Dexterity", "1,501", "Paragon 187",
        "Dance of Knives", "RANK 20/15",
        // back to char-select, enter with the Barbarian
        "R Undo Character Delete",
        "HEOKI", "Seasonal", "Barbarian", "Paragon 186", "Penitent",
        "R Undo Character Delete      D Delete Character      C Change Difficulty",
        "START GAME",
        "QUEUED FOR GAME - START GAME PENDING...",
    });
    var sw = LogWatcher.BuildFromFile(swLog, equippedOnly: false);
    Check("LogWatcher: char sheet reset by the char-select visit (no stale Rogue paragon)",
        sw.Character.ParagonLevel == null && sw.Character.Dexterity == null);
    Eq("LogWatcher: skills reset by the char-select visit", 0, sw.Skills.Count);
    Eq("LogWatcher: the Barbarian is the (only) own-roster entry", "Barbarian", sw.Roster.Single().Class);
    try { File.Delete(swLog); } catch { }
}

// ---- LogWatcher.LastSessionStartPos (startup skips dead history before the last session marker) ----
{
    var posLog = Path.Combine(Path.GetTempPath(), "d4s_pos_test_" + Guid.NewGuid().ToString("N") + ".log");
    var older = "[2026-06-05T07:49:15Z]=== d4scanner tts shim attached v2 ===\n[old]stale gear line\n";
    var newer = "[2026-06-09T08:04:18Z]=== d4scanner tts shim attached v2 ===\n[new]current line\n";
    File.WriteAllText(posLog, older + newer);
    long pos = LogWatcher.LastSessionStartPos(posLog);
    Eq("LastSessionStartPos: points at the LAST attach marker's line start",
        (long)System.Text.Encoding.UTF8.GetByteCount(older), pos);
    var tail = File.ReadAllText(posLog).Substring((int)pos);
    Check("LastSessionStartPos: tail begins with the marker line", tail.StartsWith("[2026-06-09T08:04:18Z]==="));
    File.WriteAllText(posLog, "no markers here at all\njust gameplay\n");
    Eq("LastSessionStartPos: no marker -> 0 (full replay)", 0L, LogWatcher.LastSessionStartPos(posLog));
    Eq("LastSessionStartPos: missing file -> 0", 0L, LogWatcher.LastSessionStartPos(posLog + ".nope"));
    try { File.Delete(posLog); } catch { }
}

// ---- ClassRules / ClassLock / SharedCandidates / ItemCompare (shared-stash All Items) ----
{
    // class-lock parsed from the requires line (class glued by the reader: "Account BoundRogue. Only.")
    var lockSeg = new GearParser();
    Item? lockItem = null;
    foreach (var ln in new[] { "FROSTBITTEN MAMMALBANE BOW", "Ancestral Legendary Bow", "900 Item Power",
        "+275 Weapon Damage [188 - 314]", "Requires Level 70. Account BoundRogue. Only. Vessel of Hatred Item ", "Right mouse button" })
    { var r = lockSeg.Feed(ln); if (r != null) lockItem = r; }
    Eq("GearParser: class lock parsed from requires line", "Rogue", lockItem?.ClassLock);

    // weapon-type tables
    Check("ClassRules: Rogue can equip a bow", ClassRules.CanEquip("Rogue", new Item { ItemType = "Bow", Slot = "ranged" }));
    Check("ClassRules: Barbarian cannot equip a bow", !ClassRules.CanEquip("Barbarian", new Item { ItemType = "Bow", Slot = "ranged" }));
    Check("ClassRules: Barbarian can equip a two-handed sword", ClassRules.CanEquip("Barbarian", new Item { ItemType = "Two-Handed Sword", Slot = "weapon" }));
    Check("ClassRules: Rogue can equip a one-handed sword", ClassRules.CanEquip("Rogue", new Item { ItemType = "Sword", Slot = "weapon" }));
    Check("ClassRules: Rogue CANNOT equip a two-handed sword (live-verified miss)",
        !ClassRules.CanEquip("Rogue", new Item { ItemType = "Two-Handed Sword", Slot = "weapon" }));
    Check("ClassRules: Necromancer can equip a two-handed sword", ClassRules.CanEquip("Necromancer", new Item { ItemType = "Two-Handed Sword", Slot = "weapon" }));
    Check("ClassRules: Druid cannot equip a two-handed sword", !ClassRules.CanEquip("Druid", new Item { ItemType = "Two-Handed Sword", Slot = "weapon" }));
    Check("ClassRules: Rogue cannot equip a polearm", !ClassRules.CanEquip("Rogue", new Item { ItemType = "Polearm", Slot = "weapon" }));
    Check("ClassRules: crossbow doesn't false-match the 'bow' table for Sorcerer either",
        !ClassRules.CanEquip("Sorcerer", new Item { ItemType = "Crossbow", Slot = "ranged" }));
    Check("ClassRules: armor is class-free", ClassRules.CanEquip("Barbarian", new Item { ItemType = "Helm", Slot = "helm" }));
    Check("ClassRules: explicit class lock beats everything", !ClassRules.CanEquip("Barbarian", new Item { ItemType = "Helm", Slot = "helm", ClassLock = "Rogue" }));
    Check("ClassRules: unknown class is never filtered", ClassRules.CanEquip("Paladin", new Item { ItemType = "Bow", Slot = "ranged" }));
    Eq("ClassRules: Barbarian carries 4 weapons", 4, ClassRules.WeaponSlots("Barbarian"));
    Eq("ClassRules: Rogue carries 3 weapons", 3, ClassRules.WeaponSlots("Rogue"));
    Eq("ClassRules: Sorcerer carries 2 weapons", 2, ClassRules.WeaponSlots("Sorcerer"));

    // shared pool: other characters' gear shows up, current-equipped is excluded, class filter applies
    var myHelm = new Item { Name = "My Helm", Slot = "helm", Equipped = true, Affixes = { new Affix { Text = "Maximum Life", Value = 100 } } };
    var myBag = new Item { Name = "Bag Ring", Slot = "ring", Affixes = { new Affix { Text = "Dexterity", Value = 50 } } };
    var cur = new LiveBuild { Gear = { myHelm }, Inventory = { myBag } };
    var barbProfile = new CharacterProfile { Slug = "heoki-barbarian", Name = "Heoki", Class = "Barbarian", Live = new LiveBuild {
        Gear = { new Item { Name = "Barb Mace", Slot = "weapon", ItemType = "Two-Handed Mace", Equipped = true, Affixes = { new Affix { Text = "Strength", Value = 90 } } },
                 new Item { Name = "Shared Amulet", Slot = "amulet", Affixes = { new Affix { Text = "Maximum Life", Value = 200 } } } } } };
    var pool = GearList.SharedCandidates(cur, new[] { barbProfile }, "Rogue");
    Check("SharedCandidates: current equipped is excluded", !pool.Any(o => o.Item.Name == "My Helm"));
    Check("SharedCandidates: own bag item present with no owner tag", pool.Any(o => o.Item.Name == "Bag Ring" && o.Owner == null));
    Check("SharedCandidates: other character's amulet present with owner tag",
        pool.Any(o => o.Item.Name == "Shared Amulet" && o.Owner == "Heoki · Barbarian"));
    Check("SharedCandidates: other character's Rogue-unusable mace filtered out", !pool.Any(o => o.Item.Name == "Barb Mace"));
    var poolBarb = GearList.SharedCandidates(barbProfile.Live, new[] { new CharacterProfile { Name = "Heoki", Class = "Rogue", Live = cur } }, "Barbarian");
    Check("SharedCandidates: from the Barbarian's side the mace is its own equipped (excluded) but the helm shows",
        !poolBarb.Any(o => o.Item.Name == "Barb Mace") && poolBarb.Any(o => o.Item.Name == "My Helm"));

    // hover compare rows: shared affixes get a delta; one-sided affixes show a dash
    var candItem = new Item { Name = "Cand", Slot = "helm", Affixes = {
        new Affix { Text = "Maximum Life", Value = 1500 }, new Affix { Text = "Dexterity", Value = 60 } } };
    var eqItem = new Item { Name = "Eq", Slot = "helm", Affixes = {
        new Affix { Text = "Maximum Life", Value = 1200 }, new Affix { Text = "Cooldown Reduction", Value = 7, IsPercent = true } } };
    var rows = ItemCompare.Rows(candItem, eqItem);
    Eq("ItemCompare: union of affixes", 3, rows.Count);
    var life = rows.First(r => r.Label.Contains("Life"));
    Eq("ItemCompare: shared affix delta", 300.0, life.Delta);
    var dex = rows.First(r => r.Label.Contains("Dexterity"));
    Check("ItemCompare: candidate-only affix has no delta and a dash on the equipped side", dex.Delta == null && dex.EquippedText == "—");
    var cdr = rows.First(r => r.Label.Contains("Cooldown"));
    Check("ItemCompare: equipped-only affix renders last with a dash on the candidate side", cdr.CandidateText == "—");
    Eq("ItemCompare: no equipped item -> all rows one-sided", 2, ItemCompare.Rows(candItem, null).Count);
}

// ---- Updater.CleanUpSuperseded (old exes used to accumulate forever after updates) ----
{
    var dir = Path.Combine(Path.GetTempPath(), "d4s_upd_test_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var current = Path.Combine(dir, "D4Scanner-v0.5.0-win-x64.exe");          // pretend this is the running image
    File.WriteAllText(current, "current");
    File.WriteAllText(Path.Combine(dir, "D4Scanner-v0.4.0-win-x64.exe"), "older");
    File.WriteAllText(Path.Combine(dir, "D4Scanner-v0.4.0-win-x64.exe.old"), "sidecar");
    File.WriteAllText(Path.Combine(dir, "D4Scanner-v9.9.9-win-x64.exe"), "newer-mid-update");
    Updater.CleanUpSuperseded(current);
    // test host's running version is ~1.0.0, so v0.x are superseded and v9.9.9 is "newer"
    Check("CleanUpSuperseded: older versioned exe deleted", !File.Exists(Path.Combine(dir, "D4Scanner-v0.4.0-win-x64.exe")));
    Check("CleanUpSuperseded: .old sidecar deleted", !File.Exists(Path.Combine(dir, "D4Scanner-v0.4.0-win-x64.exe.old")));
    Check("CleanUpSuperseded: the running image survives", File.Exists(current));
    Check("CleanUpSuperseded: a NEWER exe (mid-flight update) survives", File.Exists(Path.Combine(dir, "D4Scanner-v9.9.9-win-x64.exe")));
    try { Directory.Delete(dir, recursive: true); } catch { }
}

// ---- Updater: robust tag handling (a naive Split('-')[1] truncated hyphenated pre-release tags) ----
{
    Eq("Updater: tag from clean asset name", "v0.6.3", Updater.TagFromAssetFile("D4Scanner-v0.6.3-win-x64.exe") ?? "");
    Eq("Updater: tag keeps a hyphenated pre-release suffix", "v1.0.0-rc1", Updater.TagFromAssetFile("D4Scanner-v1.0.0-rc1-win-x64.exe") ?? "");
    Check("Updater: a non-asset filename -> null", Updater.TagFromAssetFile("something-else.exe") == null);
    Check("Updater: IsNewer compares the numeric core (clean tags)", Updater.IsNewer("v1.0.1", "v1.0.0"));
    Check("Updater: IsNewer parses a pre-release suffix (core 1.0.0 > 0.9.0)", Updater.IsNewer("v1.0.0-rc1", "v0.9.0"));
    Check("Updater: IsNewer not-newer when cores tie across a suffix", !Updater.IsNewer("v1.0.0", "v1.0.0-rc1"));
}

// ---- Salvage upgrades, sort tiers, rarity rank, stale-copy exclusion ----
{
    Eq("RarityRank: mythic top", 5, GearList.RarityRank("Mythic Unique"));
    Eq("RarityRank: legendary", 3, GearList.RarityRank("Ancestral Legendary"));
    Eq("RarityRank: unknown", 0, GearList.RarityRank(null));

    // salvage: a legendary carrying a WANTED aspect flags + outranks plain non-upgrades
    var sTarget = new TargetBuild { Gear = { new TargetGear { Slot = "Gloves", Aspect = "Edgemaster's Aspect",
        Affixes = { new TargetAffix { Name = "Attack Speed" } } } } };
    var eqGl = new Item { Name = "EqGloves", Slot = "Gloves", Equipped = true, Affixes = { new Affix { Text = "Attack Speed", Value = 9 } } };
    var salv = new Item { Name = "SalvageGloves", Slot = "Gloves", Rarity = "Legendary", Aspect = "Edgemaster's Aspect" };
    var plain = new Item { Name = "PlainGloves", Slot = "Gloves", Rarity = "Legendary", ItemPower = 900 };
    var uniqSalv = new Item { Name = "UniqGloves", Slot = "Gloves", Rarity = "Unique", IsUnique = true, Aspect = "Edgemaster's Aspect" };
    var sScored = UpgradeScorer.Score(sTarget, new LiveBuild { Gear = { eqGl } }, new[] { plain, salv, uniqSalv });
    Check("Salvage: wanted-aspect legendary is flagged", sScored.First(x => x.Item.Name == "SalvageGloves").SalvageAspect != null);
    Check("Salvage: uniques are never salvage candidates", sScored.First(x => x.Item.IsUnique).SalvageAspect == null);
    Eq("Salvage: salvage upgrade outranks the plain non-upgrade", "SalvageGloves", sScored[0].Item.Name);

    // sort tiers: equal affix score -> item power decides, then rarity
    var tTarget = new TargetBuild { Gear = { new TargetGear { Slot = "Helm", Affixes = { new TargetAffix { Name = "Dexterity" } } } } };
    var ipLow = new Item { Name = "A LowIP", Slot = "Helm", Rarity = "Legendary", ItemPower = 700 };
    var ipHigh = new Item { Name = "B HighIP", Slot = "Helm", Rarity = "Rare", ItemPower = 900 };
    var ipTieLeg = new Item { Name = "C TieLeg", Slot = "Helm", Rarity = "Legendary", ItemPower = 800 };
    var ipTieRare = new Item { Name = "D TieRare", Slot = "Helm", Rarity = "Rare", ItemPower = 800 };
    var tScored = UpgradeScorer.Score(tTarget, new LiveBuild(), new[] { ipLow, ipTieRare, ipHigh, ipTieLeg });
    Eq("Sort: item power is the secondary key", "B HighIP", tScored[0].Item.Name);
    Eq("Sort: rarity breaks the IP tie (legendary over rare)", "C TieLeg", tScored[1].Item.Name);
    Eq("Sort: rare tie follows", "D TieRare", tScored[2].Item.Name);

    // stale-copy guard, refined: only an OLDER MASTERWORK capture of the equipped item (a value pushed past
    // its own [min..max] max, dominated by the equipped copy) is excluded — NOT a fresh base-roll spare.
    var eqNow = new Item { Name = "Etna's Lost Dagger", Slot = "weapon", Equipped = true,
        Affixes = { new Affix { Text = "Dexterity", Value = 176, Min = 125, Max = 149 } } };   // masterworked above max
    var staleMw = new Item { Name = "Etna's Lost Dagger", Slot = "weapon",
        Affixes = { new Affix { Text = "Dexterity", Value = 160, Min = 125, Max = 149 } } };   // older, lower-MW capture (still > max)
    var sPool = GearList.SharedCandidates(new LiveBuild { Gear = { eqNow }, Inventory = { staleMw } },
        Array.Empty<CharacterProfile>(), "Rogue");
    Check("SharedCandidates: older masterwork re-scan of the equipped item is excluded (was a false upgrade)",
        !sPool.Any(o => o.Item.Name == "Etna's Lost Dagger"));

    // …but a genuine base-roll duplicate you own (values within range, even if lower than your masterworked
    // equipped copy) MUST show, so you can compare it. This is the user-reported "extra Etna's dagger" case.
    var freshSpare = new Item { Name = "Etna's Lost Dagger", Slot = "weapon",
        Affixes = { new Affix { Text = "Dexterity", Value = 140, Min = 125, Max = 149 } } };   // fresh base roll, within range
    var sPool2 = GearList.SharedCandidates(new LiveBuild { Gear = { eqNow }, Inventory = { freshSpare } },
        Array.Empty<CharacterProfile>(), "Rogue");
    Check("SharedCandidates: a fresh base-roll duplicate of an equipped item IS shown (for comparison)",
        sPool2.Any(o => o.Item.Name == "Etna's Lost Dagger"));
    // a duplicate that ROLLS HIGHER on an affix (a real potential upgrade) is never suppressed
    var betterSpare = new Item { Name = "Etna's Lost Dagger", Slot = "weapon",
        Affixes = { new Affix { Text = "Dexterity", Value = 149, Min = 125, Max = 149 },
                    new Affix { Text = "Maximum Life", Value = 1800, Min = 1526, Max = 1830 } } };  // extra affix the equipped lacks
    var sPool3 = GearList.SharedCandidates(new LiveBuild { Gear = { eqNow }, Inventory = { betterSpare } },
        Array.Empty<CharacterProfile>(), "Rogue");
    Check("SharedCandidates: a duplicate with an affix the equipped lacks IS shown",
        sPool3.Any(o => o.Item.Name == "Etna's Lost Dagger"));
}

// ---- v0.27: weapon-gated upgrades, per-slot bars, channel-merged inventory, gear sanitizing, aspect matching ----
{
    // WeaponSlotCompatible: the physical gate
    var xbowSlot = new TargetGear { Slot = "weapon1", ItemId = "2HCrossbow_Legendary_S8" };
    Check("WeaponSlotCompatible: melee can't fill a ranged slot",
        !DiffEngine.WeaponSlotCompatible(xbowSlot, new Item { Slot = "weapon", ItemType = "Sword" }));
    Check("WeaponSlotCompatible: a crossbow fits its slot",
        DiffEngine.WeaponSlotCompatible(xbowSlot, new Item { Slot = "weapon", ItemType = "Crossbow" }));
    Check("WeaponSlotCompatible: a one-hander can't fill a two-hand slot",
        !DiffEngine.WeaponSlotCompatible(new TargetGear { Slot = "weapon", ItemId = "2HSword_Leg" }, new Item { Slot = "weapon", ItemType = "Sword" }));
    Check("WeaponSlotCompatible: a two-handed sword fits the 2H slot",
        DiffEngine.WeaponSlotCompatible(new TargetGear { Slot = "weapon", ItemId = "2HSword_Leg" }, new Item { Slot = "weapon", ItemType = "Two-Handed Sword" }));
    Check("WeaponSlotCompatible: non-weapon slots always pass",
        DiffEngine.WeaponSlotCompatible(new TargetGear { Slot = "helm" }, new Item { Slot = "helm", ItemType = "Helm" }));

    // a stash SWORD loaded with the crossbow slot's affixes must never badge as an upgrade for it
    var wTarget = new TargetBuild { Gear = {
        new TargetGear { Slot = "weapon1", Label = "Crossbow", ItemId = "2HCrossbow_Legendary_S8", Affixes = {
            new TargetAffix { Name = "Vulnerable Damage" }, new TargetAffix { Name = "Maximum Life" },
            new TargetAffix { Name = "Dexterity" }, new TargetAffix { Name = "Critical Strike Damage" } } },
        new TargetGear { Slot = "weapon2", Label = "Dagger", ItemId = "1HDagger_Legendary_S8", Affixes = {
            new TargetAffix { Name = "Attack Speed" }, new TargetAffix { Name = "Core Skill Damage" },
            new TargetAffix { Name = "Damage to Close Enemies" } } } } };
    var eqXbow = new Item { Name = "EqXbow", Slot = "weapon", ItemType = "Crossbow", Equipped = true, Affixes = {
        new Affix { Text = "Vulnerable Damage", Value = 20 }, new Affix { Text = "Maximum Life", Value = 500 } } };
    var eqDag = new Item { Name = "EqDagger", Slot = "weapon", ItemType = "Dagger", Equipped = true, Affixes = {
        new Affix { Text = "Attack Speed", Value = 5 } } };
    var swordCand = new Item { Name = "StashSword", Slot = "weapon", ItemType = "Sword", Affixes = {
        new Affix { Text = "Vulnerable Damage", Value = 25 }, new Affix { Text = "Maximum Life", Value = 700 },
        new Affix { Text = "Dexterity", Value = 60 } } };
    var xbowCand = new Item { Name = "StashXbow", Slot = "weapon", ItemType = "Crossbow", Affixes = {
        new Affix { Text = "Vulnerable Damage", Value = 25 }, new Affix { Text = "Maximum Life", Value = 700 },
        new Affix { Text = "Dexterity", Value = 60 } } };
    var wScored = UpgradeScorer.Score(wTarget, new LiveBuild { Gear = { eqXbow, eqDag } }, new[] { swordCand, xbowCand });
    Check("WeaponGate: a sword is never an upgrade for the crossbow slot",
        !wScored.First(x => x.Item.Name == "StashSword").IsUpgrade);
    Check("WeaponGate: a real crossbow with more slot affixes IS the upgrade",
        wScored.First(x => x.Item.Name == "StashXbow").IsUpgrade);
    Eq("WeaponGate: the crossbow upgrade compares against the crossbow slot", "Crossbow",
        wScored.First(x => x.Item.Name == "StashXbow").SlotLabel);
    Eq("WeaponGate: the sword's honest comparison is the dagger slot (its only compatible one)", 1,
        wScored.First(x => x.Item.Name == "StashSword").CompareSlotIndex);

    // per-slot bar: a 3/4 ring upgrades your WORSE ring even though your better ring is 4/4
    TargetAffix[] R() => new[] { new TargetAffix { Name = "Critical Strike Chance" }, new TargetAffix { Name = "Attack Speed" },
                                 new TargetAffix { Name = "Maximum Life" }, new TargetAffix { Name = "Lucky Hit Chance" } };
    var rTarget = new TargetBuild { Gear = {
        new TargetGear { Slot = "ring1", Label = "Ring #1", Affixes = new(R()) },
        new TargetGear { Slot = "ring2", Label = "Ring #2", Affixes = new(R()) } } };
    var ringGood = new Item { Name = "GoodRing", Slot = "ring", Equipped = true, Affixes = {
        new Affix { Text = "Critical Strike Chance", Value = 5 }, new Affix { Text = "Attack Speed", Value = 8 },
        new Affix { Text = "Maximum Life", Value = 900 }, new Affix { Text = "Lucky Hit Chance", Value = 10 } } };
    var ringBad = new Item { Name = "BadRing", Slot = "ring", Equipped = true, Affixes = {
        new Affix { Text = "Maximum Life", Value = 400 } } };
    var ringCand = new Item { Name = "CandRing", Slot = "ring", Affixes = {
        new Affix { Text = "Critical Strike Chance", Value = 4 }, new Affix { Text = "Attack Speed", Value = 7 },
        new Affix { Text = "Maximum Life", Value = 800 } } };
    var rScored = UpgradeScorer.Score(rTarget, new LiveBuild { Gear = { ringGood, ringBad } }, new[] { ringCand });
    Check("PerSlotBar: 3/4 ring IS an upgrade over the worse equipped ring", rScored[0].IsUpgrade);
    Eq("PerSlotBar: it compares against the weaker ring's presence", 1, rScored[0].EquippedPresent);
    Eq("PerSlotBar: affix delta vs the displaced ring (4 effective − 1)", 3, rScored[0].AffixDelta);

    // channel-aware inventory merge: TTS and OCR no longer wipe each other
    var ttsRing = new Item { Name = "Tts Ring", Slot = "ring", Source = ItemSource.Tts };
    var ocrHelm = new Item { Name = "Ocr Helm", Slot = "helm", Source = ItemSource.Ocr };
    var m1 = LiveGearResolver.MergeInventory(new List<Item> { ttsRing, ocrHelm },
        new List<Item> { new Item { Name = "Ocr Boots", Slot = "boots", Source = ItemSource.Ocr } });
    Check("MergeInventory: an OCR batch keeps the TTS item", m1.Any(i => i.Name == "Tts Ring"));
    Check("MergeInventory: an OCR batch replaces its OWN channel's list",
        !m1.Any(i => i.Name == "Ocr Helm") && m1.Any(i => i.Name == "Ocr Boots"));
    var m2 = LiveGearResolver.MergeInventory(new List<Item> { ttsRing },
        new List<Item> { new Item { Name = "Tts Ring", Slot = "ring", Source = ItemSource.Ocr } });
    Check("MergeInventory: Tts wins a cross-channel name+slot collision", m2.Count == 1 && m2[0].Source == ItemSource.Tts);
    var m3 = LiveGearResolver.MergeInventory(new List<Item> { new Item { Name = "X", Slot = "ring", Source = ItemSource.Ocr } },
        new List<Item> { new Item { Name = "X", Slot = "ring", Source = ItemSource.Tts } });
    Check("MergeInventory: a fresh Tts capture replaces the persisted Ocr copy", m3.Count == 1 && m3[0].Source == ItemSource.Tts);
    Eq("MergeInventory: an empty fresh batch changes nothing", 2,
        LiveGearResolver.MergeInventory(new List<Item> { ttsRing, ocrHelm }, new List<Item>()).Count);

    // equipped-gear sanity pass: class gate + slotless junk + per-class weapon capacity
    var rogueGear = new List<Item> {
        new Item { Name = "Helm", Slot = "helm", ItemType = "Helm" },
        new Item { Name = "OldBow", Slot = "weapon", ItemType = "Bow", LastScannedTicks = 100 },
        new Item { Name = "NewXbow", Slot = "weapon", ItemType = "Crossbow", LastScannedTicks = 200 },
        new Item { Name = "Dag1", Slot = "weapon", ItemType = "Dagger", LastScannedTicks = 150 },
        new Item { Name = "Dag2", Slot = "weapon", ItemType = "Dagger", LastScannedTicks = 160 },
        new Item { Name = "OldSword", Slot = "weapon", ItemType = "Sword", LastScannedTicks = 90 },
        new Item { Name = "Bitter Bash", Slot = null },
        new Item { Name = "TwoHander", Slot = "weapon", ItemType = "Two-Handed Mace", LastScannedTicks = 300 },
    };
    var keptR = LiveGearResolver.SanitizeEquipped(rogueGear, "Rogue", out var demR);
    Check("Sanitize: slotless capture artifact demoted", demR.Any(i => i.Name == "Bitter Bash"));
    Check("Sanitize: class-impossible two-hander demoted from the Rogue", demR.Any(i => i.Name == "TwoHander"));
    Check("Sanitize: only the NEWEST ranged weapon stays (bow↔crossbow swap)",
        keptR.Any(i => i.Name == "NewXbow") && demR.Any(i => i.Name == "OldBow"));
    Check("Sanitize: the two newest melee weapons stay", keptR.Any(i => i.Name == "Dag1") && keptR.Any(i => i.Name == "Dag2") && demR.Any(i => i.Name == "OldSword"));
    Eq("Sanitize: rogue arsenal trimmed to 3 weapons", 3, keptR.Count(i => DiffEngine.SlotBaseName(i.Slot) == "weapon"));
    var barbGear = new List<Item> {
        new Item { Name = "2H1", Slot = "weapon", ItemType = "Two-Handed Mace", LastScannedTicks = 10 },
        new Item { Name = "2H2", Slot = "weapon", ItemType = "Two-Handed Sword", LastScannedTicks = 20 },
        new Item { Name = "2H3", Slot = "weapon", ItemType = "Two-Handed Axe", LastScannedTicks = 30 },
        new Item { Name = "1H1", Slot = "weapon", ItemType = "Sword", LastScannedTicks = 5 },
        new Item { Name = "1H2", Slot = "weapon", ItemType = "Mace", LastScannedTicks = 6 },
    };
    var keptB = LiveGearResolver.SanitizeEquipped(barbGear, "Barbarian", out var demB);
    Check("Sanitize: barb keeps the 2 newest two-handers + both one-handers",
        keptB.Count == 4 && demB.Count == 1 && demB[0].Name == "2H1");
    Eq("Sanitize: unknown class only drops slotless junk", rogueGear.Count - 1,
        LiveGearResolver.SanitizeEquipped(rogueGear, null, out _).Count);

    // aspect matching that actually works on captured data (name / imprint text / power text)
    Check("ItemCarriesAspect: legendary NAME carries the aspect",
        DiffEngine.ItemCarriesAspect("Aspect of Mending Obscurity",
            new Item { Name = "Boneweave Armor of Mending Obscurity", Rarity = "Legendary" }));
    Check("ItemCarriesAspect: possessive aspect matches the name",
        DiffEngine.ItemCarriesAspect("Edgemaster's Aspect",
            new Item { Name = "Doombringer of the Edgemaster", Rarity = "Legendary" }));
    Check("ItemCarriesAspect: a Rare's random suffix can't false-match",
        !DiffEngine.ItemCarriesAspect("Aspect of Winter", new Item { Name = "Cruel Band of Winter", Rarity = "Rare" }));
    Check("ItemCarriesAspect: imprinted effect text still matches",
        DiffEngine.ItemCarriesAspect("Aspect of the Expectant", new Item { Aspect = "Aspect of the Expectant" }));
    Check("ItemCarriesAspect: power-text fallback",
        DiffEngine.ItemCarriesAspect("Aspect of Mending Obscurity",
            new Item { Rarity = "Rare", PowerText = { "Mending Obscurity: gain Stealth when standing still" } }));
    Eq("AspectDisplayName: suffix form", "Aspect of Encircling Blades", MaxrollImporter.AspectDisplayName("of Encircling Blades"));
    Eq("AspectDisplayName: possessive form", "Edgemaster's Aspect", MaxrollImporter.AspectDisplayName("Edgemaster's"));
    Eq("AspectDisplayName: already-named passthrough", "Aspect of X", MaxrollImporter.AspectDisplayName("Aspect of X"));

    // salvage flagged from the item NAME alone — the production path (imprint text never matched Maxroll's)
    var nTarget = new TargetBuild { Gear = { new TargetGear { Slot = "Chest", Aspect = "Aspect of Mending Obscurity",
        Affixes = { new TargetAffix { Name = "Maximum Life" } } } } };
    var byName = new Item { Name = "Boneweave Armor of Mending Obscurity", Slot = "Chest", Rarity = "Legendary" };
    var nScored = UpgradeScorer.Score(nTarget, new LiveBuild(), new[] { byName });
    Eq("Salvage: matched by ITEM NAME (no imprint text needed)", "Aspect of Mending Obscurity", nScored[0].SalvageAspect);

    // v0.41 contract change: same-name copies WITHOUT stale-rescan evidence are GENUINE duplicates —
    // both rows show (the old name+slot collapse hid one). Only a provably-stale capture (masterwork-
    // inflated AND strictly dominated by another copy) is dropped.
    var freshCopy = new Item { Name = "Tal Ring", Slot = "ring", LastScannedTicks = 200,
        Affixes = { new Affix { Text = "Critical Strike Chance", Value = 10 } } };
    var staleCopy = new Item { Name = "Tal Ring", Slot = "ring", LastScannedTicks = 100,
        Affixes = { new Affix { Text = "Critical Strike Chance", Value = 9 } } };
    var poolX = GearList.SharedCandidates(
        new LiveBuild { Inventory = { freshCopy } },
        new[] { new CharacterProfile { Slug = "heoki-barbarian", Name = "Heoki", Class = "Barbarian",
                                       Live = new LiveBuild { Inventory = { staleCopy } } } },
        "Rogue");
    Eq("SharedCandidates: two genuine same-name rolls BOTH show (no blanket name collapse)",
        2, poolX.Count(o => o.Item.Name == "Tal Ring"));
    // …but a masterwork-inflated copy strictly dominated by another copy is a stale re-scan: one row.
    var inflatedStale = new Item { Name = "Tal Ring", Slot = "ring", LastScannedTicks = 100,
        Affixes = { new Affix { Text = "Critical Strike Chance", Value = 9, Min = 5, Max = 8 } } };   // 9 > its own max
    var freshBetter = new Item { Name = "Tal Ring", Slot = "ring", LastScannedTicks = 200,
        Affixes = { new Affix { Text = "Critical Strike Chance", Value = 10, Min = 5, Max = 8 } } };
    var poolY = GearList.SharedCandidates(
        new LiveBuild { Inventory = { freshBetter } },
        new[] { new CharacterProfile { Slug = "heoki-barbarian", Name = "Heoki", Class = "Barbarian",
                                       Live = new LiveBuild { Inventory = { inflatedStale } } } },
        "Rogue");
    Eq("SharedCandidates: an inflated, strictly-dominated stale copy collapses away", 1, poolY.Count(o => o.Item.Name == "Tal Ring"));
    Check("SharedCandidates: the surviving copy is the dominating (active) one",
        poolY.First(o => o.Item.Name == "Tal Ring").Owner == null);
    Check("SharedCandidates: other-character rows carry their profile slug for deletes",
        GearList.SharedCandidates(new LiveBuild(),
            new[] { new CharacterProfile { Slug = "heoki-barbarian", Name = "Heoki", Class = "Barbarian",
                                           Live = new LiveBuild { Inventory = { staleCopy } } } }, "Rogue")
            .First().OwnerSlug == "heoki-barbarian");
}

// ---- v0.29: Greater Affix inference (Season 13 fixture) ----
{
    var s13Log = Path.Combine(AppContext.BaseDirectory, "sample_tts_s13.log");
    Check("sample_tts_s13.log fixture present", File.Exists(s13Log));
    var lb13 = LogWatcher.BuildFromFile(s13Log, equippedOnly: false);
    var all13 = lb13.Gear.Concat(lb13.Inventory).ToList();
    Item Find(string frag) => all13.First(i => i.Name.Contains(frag, StringComparison.OrdinalIgnoreCase));
    bool AffGreater(Item it, string frag) => it.Affixes.First(a => a.Text.Contains(frag, StringComparison.OrdinalIgnoreCase)).IsGreater;

    // 2-GA helm (Tempers 5/5, Quality 25): Life + Intelligence are the 1.5× rolls; CDR/Crit are max-but-not-GA
    var helm = Find("Starless");
    Eq("GA: Tempers 5/5 ⇒ 2 Greater Affixes", 2, helm.GreaterAffixCount ?? -1);
    Check("GA: Maximum Life starred", AffGreater(helm, "Maximum Life"));
    Check("GA: Intelligence starred", AffGreater(helm, "Intelligence"));
    Check("GA: max-roll Cooldown Reduction is NOT a GA", !AffGreater(helm, "Cooldown"));
    Check("GA: mid-roll Crit is NOT a GA", !AffGreater(helm, "Critical Strike"));
    Check("GA: no capstone detected when candidates fit the budget", helm.CapstoneAffix == null);

    // 1-GA gloves (Tempers 4/4): a Greater Damage Multiplier (× affix) at low Quality
    var gloves = Find("Grasp");
    Eq("GA: Tempers 4/4 ⇒ 1 Greater Affix", 1, gloves.GreaterAffixCount ?? -1);
    Check("GA: a Damage Multiplier can be a GA", AffGreater(gloves, "Vulnerable Damage Multiplier"));
    Check("GA: the multiplier flag survives GA detection", gloves.Affixes.First(a => a.Text.Contains("Vulnerable")).IsMultiplier);

    // 0-GA chest (Tempers 3/3) with high-but-in-range rolls — nothing starred
    var chest = Find("Penitent");
    Eq("GA: Tempers 3/3 ⇒ 0 Greater Affixes", 0, chest.GreaterAffixCount ?? -1);
    Check("GA: in-range rolls are never starred", chest.Affixes.All(a => !a.IsGreater));

    // Rare (Tempers 0/1) — the denominator of 1 means Rare, no GA budget
    var ring = Find("Novice");
    Eq("GA: Rare (Tempers x/1) ⇒ 0 Greater Affixes", 0, ring.GreaterAffixCount ?? -1);

    // capstone-excess: 1 GA budget (Tempers 4/4) but TWO 1.5× lines at Quality 25 → the lower one is the Capstone
    var amu = Find("Liars");
    Eq("GA: capstone case still reports 1 Greater Affix", 1, amu.GreaterAffixCount ?? -1);
    Check("GA: the higher-ratio line is the real GA (Intelligence)", AffGreater(amu, "Intelligence"));
    Check("GA: the excess 1.5× line is NOT starred (it's the capstone)", !AffGreater(amu, "All Damage Multiplier"));
    Check("GA: the capstone affix is recorded", amu.CapstoneAffix != null && amu.CapstoneAffix.Contains("All Damage", StringComparison.OrdinalIgnoreCase));

    // transfigure pushes Quality past 25 — the inference divides it out and still finds the GA
    var boots = Find("Sundered");
    Eq("GA: Quality>25 (transfigure) still ⇒ 1 GA", 1, boots.GreaterAffixCount ?? -1);
    Check("GA: GA found despite Quality 34 inflation", AffGreater(boots, "Intelligence"));

    // REAL-DATA SHAPE: an un-masterworked GA item (no Quality line) — the count is reliable from the temper
    // denominator, but each GA value sits WITHIN its already-elevated displayed range, so no affix is provably
    // starred. The honest behaviour: show "2 GA" from the count, star nothing (we can't pinpoint the lines).
    var band = Find("Silent");
    Eq("GA(real-shape): Tempers 5/5 ⇒ count 2 even with no Quality line", 2, band.GreaterAffixCount ?? -1);
    Check("GA(real-shape): no affix is falsely starred on an un-masterworked item", band.Affixes.All(a => !a.IsGreater));

    // ---- scoring: a GA on a wanted affix outranks an equal-presence item without it ----
    var gaTarget = new TargetBuild { Gear = { new TargetGear { Slot = "Helm", Affixes = {
        new TargetAffix { Name = "Maximum Life" }, new TargetAffix { Name = "Intelligence" },
        new TargetAffix { Name = "Cooldown Reduction" }, new TargetAffix { Name = "Critical Strike Chance" } } } } };
    var plainHelm = new Item { Name = "Plain Helm", Slot = "helm", Affixes = {
        new Affix { Text = "Maximum Life", Value = 1500, Min = 1300, Max = 1600 },
        new Affix { Text = "Intelligence", Value = 85, Min = 70, Max = 90 },
        new Affix { Text = "Cooldown Reduction", Value = 12, Min = 8, Max = 13.5, IsPercent = true },
        new Affix { Text = "Critical Strike Chance", Value = 7, Min = 5, Max = 9, IsPercent = true } } };
    var gaScored = UpgradeScorer.Score(gaTarget, new LiveBuild(), new[] { plainHelm, helm });
    Check("GA scoring: the 2-GA helm outranks the equal-presence plain helm", gaScored[0].Item.Name.Contains("Starless"));
    Check("GA scoring: useful-GA estimate is counted", gaScored.First(s => s.Item.Name.Contains("Starless")).GreaterOnWanted == 2);

    // scoring works on the REAL-DATA SHAPE too: an un-masterworked 2-GA ring (no per-affix stars) still beats a
    // 0-GA ring with the same presence — because scoring uses the reliable item-level count, not per-affix stars
    var ringTarget = new TargetBuild { Gear = { new TargetGear { Slot = "Ring", Affixes = {
        new TargetAffix { Name = "Dexterity" }, new TargetAffix { Name = "All Damage Multiplier" },
        new TargetAffix { Name = "Vulnerable Damage Multiplier" } } } } };
    var plainRing = new Item { Name = "Plain Ring", Slot = "ring", Affixes = {
        new Affix { Text = "Dexterity", Value = 110 }, new Affix { Text = "All Damage Multiplier", Value = 15, IsMultiplier = true },
        new Affix { Text = "Vulnerable Damage Multiplier", Value = 11, IsMultiplier = true } } };
    var ringScored = UpgradeScorer.Score(ringTarget, new LiveBuild(), new[] { plainRing, band });
    Check("GA scoring(real-shape): the un-masterworked 2-GA ring outranks the 0-GA ring", ringScored[0].Item.Name.Contains("Silent"));
    Check("GA scoring(real-shape): useful-GA credited from the count despite zero stars",
        ringScored.First(s => s.Item.Name.Contains("Silent")).GreaterOnWanted >= 2);

    // ---- FixDestroysGA: completing the slot would enchant away the only extra, a Greater Affix ----
    var fixTarget = new TargetBuild { Gear = { new TargetGear { Slot = "Gloves", Affixes = {
        new TargetAffix { Name = "Attack Speed" }, new TargetAffix { Name = "Critical Strike Chance" },
        new TargetAffix { Name = "Lucky Hit Chance" }, new TargetAffix { Name = "Dexterity" } } } } };
    var fixCand = new Item { Name = "Fix Gloves", Slot = "gloves", GreaterAffixCount = 1, Affixes = {
        new Affix { Text = "Attack Speed", Value = 8 }, new Affix { Text = "Critical Strike Chance", Value = 8 },
        new Affix { Text = "Lucky Hit Chance", Value = 10 },
        new Affix { Text = "Vulnerable Damage", Value = 40 } } };   // item carries a GA somewhere
    var fixScored = UpgradeScorer.Score(fixTarget, new LiveBuild(), new[] { fixCand });
    Check("FixDestroysGA: 3/4 item is fixable", fixScored[0].Fixable);
    Check("FixDestroysGA: a fixable item with a Greater Affix trips the enchant caution", fixScored[0].FixDestroysGA);
    var fixCandSafe = new Item { Name = "Safe Gloves", Slot = "gloves", GreaterAffixCount = 0, Affixes = {
        new Affix { Text = "Attack Speed", Value = 8 }, new Affix { Text = "Critical Strike Chance", Value = 8 },
        new Affix { Text = "Lucky Hit Chance", Value = 10 },
        new Affix { Text = "Vulnerable Damage", Value = 40 } } };   // no GA on the item — safe to enchant
    Check("FixDestroysGA: a fixable item with NO Greater Affix is safe",
        !UpgradeScorer.Score(fixTarget, new LiveBuild(), new[] { fixCandSafe })[0].FixDestroysGA);
}

// ---- v0.30: per-item verdicts ----
{
    ScoredItem SC(string name, Action<ScoredItem> set)
    {
        var s = new ScoredItem { Item = new Item { Name = name, Slot = "helm" }, SlotLabel = "Helm", SlotTarget = 4 };
        set(s); return s;
    }
    var emptyCtx = new VerdictContext(new TargetBuild(), "Rogue", new List<Item>());

    // 1. EQUIP — raw-beats the equipped piece
    var vEquip = Verdicts.For(SC("Up", s => { s.RawUpgrade = true; s.IsUpgrade = true; s.SlotPresent = 4; s.EquippedEff = 2; }), emptyCtx);
    Eq("Verdict EQUIP: raw upgrade ⇒ Equip", Verdict.Equip, vEquip.V);
    Check("Verdict EQUIP: action says equip", vEquip.Action == "Equip it");

    // 2. FIXABLE — wins only via the enchant credit (IsUpgrade but not RawUpgrade)
    var vFix = Verdicts.For(SC("Fix", s => { s.IsUpgrade = true; s.RawUpgrade = false; s.Fixable = true; }), emptyCtx);
    Eq("Verdict FIXABLE: credited-only upgrade ⇒ Fixable", Verdict.Fixable, vFix.V);
    Check("Verdict FIXABLE: action is an enchant", vFix.Action!.Contains("Enchant"));
    var vFixGA = Verdicts.For(SC("FixGA", s => { s.IsUpgrade = true; s.RawUpgrade = false; s.Fixable = true; s.FixDestroysGA = true; }), emptyCtx);
    Check("Verdict FIXABLE: warns when the enchant would hit a Greater Affix", vFixGA.Action!.Contains("Greater Affix"));

    // 3. KEEP-SALVAGE — aspect upgrades the Codex (not a gear upgrade)
    var vSalv = Verdicts.For(SC("Salv", s => { s.SalvageAspect = "Edgemaster's Aspect"; }), emptyCtx);
    Eq("Verdict SALVAGE: salvage aspect ⇒ KeepSalvage", Verdict.KeepSalvage, vSalv.V);
    Check("Verdict SALVAGE: reason names the aspect", vSalv.Reason.Contains("Edgemaster"));

    // 4. KEEP-DUPE — a duplicate Mythic → Spark
    var mythicItem = new Item { Name = "Shroud of False Death", Slot = "chest", IsMythic = true, Rarity = "Mythic Unique" };
    var dupeCtx = new VerdictContext(new TargetBuild(), "Rogue", new List<Item> { mythicItem, new Item { Name = "Shroud of False Death", Slot = "chest" } });
    var vDupeM = Verdicts.For(new ScoredItem { Item = mythicItem, SlotLabel = "Chest" }, dupeCtx);
    Eq("Verdict DUPE: duplicate Mythic ⇒ KeepDupe", Verdict.KeepDupe, vDupeM.V);
    Check("Verdict DUPE: Mythic action mentions a Spark", vDupeM.Action!.Contains("Spark"));

    // 4b. KEEP-DUPE — a build-relevant duplicate Unique → cube-recycle
    var uniTarget = new TargetBuild { Uniques = { new TargetUnique { Name = "Etna's Lost Dagger" } } };
    var uniItem = new Item { Name = "Etna's Lost Dagger", Slot = "weapon", IsUnique = true, Rarity = "Unique" };
    var uniCtx = new VerdictContext(uniTarget, "Rogue", new List<Item> { uniItem, new Item { Name = "Etna's Lost Dagger", Slot = "weapon" } });
    var vDupeU = Verdicts.For(new ScoredItem { Item = uniItem, SlotLabel = "Weapon" }, uniCtx);
    Eq("Verdict DUPE: 2 copies of a build Unique ⇒ KeepDupe", Verdict.KeepDupe, vDupeU.V);
    Check("Verdict DUPE: Unique action mentions cube-recycle", vDupeU.Action!.Contains("cube-recycle"));

    // 5. STASH — an unmodified tradeable 2-GA god-roll
    var godItem = new Item { Name = "God Ring", Slot = "ring", TemperUsed = 0, Quality = 0 };
    var vStash = Verdicts.For(new ScoredItem { Item = godItem, SlotLabel = "Ring", GreaterCount = 2 }, emptyCtx);
    Eq("Verdict STASH: unmodified 2-GA roll ⇒ Stash", Verdict.Stash, vStash.V);
    Check("Verdict STASH: warns that crafting binds it", vStash.Action!.Contains("binds"));
    // 5b. STASH — an off-class item with a GA
    var altItem = new Item { Name = "Druid Helm", Slot = "helm", ClassLock = "Druid" };
    var vAlt = Verdicts.For(new ScoredItem { Item = altItem, SlotLabel = "Helm", GreaterCount = 1 }, emptyCtx);
    Eq("Verdict STASH: off-class GA item ⇒ Stash", Verdict.Stash, vAlt.V);
    Check("Verdict STASH: routes to the right alt", vAlt.Action!.Contains("Druid"));

    // 6. JUNK — nothing the build needs
    var vJunk = Verdicts.For(SC("Junk", _ => { }), emptyCtx);
    Eq("Verdict JUNK: no signal ⇒ Junk", Verdict.Junk, vJunk.V);
    Check("Verdict JUNK: action is salvage", vJunk.Action!.Contains("Salvage"));

    // ladder ORDER: a fixable upgrade that ALSO has a salvage aspect resolves to Fixable (gear path wins)
    var vOrder = Verdicts.For(SC("Both", s => { s.IsUpgrade = true; s.RawUpgrade = false; s.Fixable = true; s.SalvageAspect = "Some Aspect"; }), emptyCtx);
    Eq("Verdict order: Fixable outranks KeepSalvage", Verdict.Fixable, vOrder.V);

    // integration: RawUpgrade is set by the scorer — a clean 2/2 candidate over an empty slot equips now
    var intTarget = new TargetBuild { Gear = { new TargetGear { Slot = "Boots", Affixes = {
        new TargetAffix { Name = "Movement Speed" }, new TargetAffix { Name = "Maximum Life" } } } } };
    var intCand = new Item { Name = "Good Boots", Slot = "boots", Affixes = {
        new Affix { Text = "Movement Speed", Value = 16 }, new Affix { Text = "Maximum Life", Value = 900 } } };
    var intScored = UpgradeScorer.Score(intTarget, new LiveBuild(), new[] { intCand })[0];
    Check("Verdict integration: scorer sets RawUpgrade for an empty-slot fill", intScored.RawUpgrade);
    Eq("Verdict integration: it reads as Equip", Verdict.Equip,
        Verdicts.For(intScored, new VerdictContext(intTarget, "Rogue", new[] { intCand })).V);
}

// ---- v0.31: per-slot upgrade path ----
{
    PathStep? Step(List<PathStep> ps, string verb) => ps.FirstOrDefault(s => s.Verb == verb);

    // a needy ancestral helm: missing one temper affix + one enchant affix, un-masterworked, empty socket, no aspect
    var upTarget = new TargetGear { Slot = "Helm", Aspect = "Aspect of Disobedience", Affixes = {
        new TargetAffix { Name = "Maximum Life", Min = 1000 },
        new TargetAffix { Name = "Dexterity" },
        new TargetAffix { Name = "Attack Speed", Tempered = true } } };
    var upItem = new Item { Name = "Needy Helm", Slot = "helm", IsAncestral = true, Quality = 0,
        SocketCount = 1, EmptySockets = 1, TemperUsed = 0, TemperMax = 3,
        Affixes = { new Affix { Text = "Maximum Life", Value = 1500, Min = 1000, Max = 1600 } } };
    var path = UpgradePath.ForSlot(upTarget, upItem);
    var verbs = path.Select(s => s.Verb).ToList();
    Check("UpgradePath: temper before enchant before masterwork before socket before imprint",
        verbs.IndexOf("TEMPER") < verbs.IndexOf("ENCHANT")
        && verbs.IndexOf("ENCHANT") < verbs.IndexOf("MASTERWORK")
        && verbs.IndexOf("MASTERWORK") < verbs.IndexOf("SOCKET")
        && verbs.IndexOf("SOCKET") < verbs.IndexOf("IMPRINT"));
    Check("UpgradePath: TEMPER targets the wanted tempered affix", Step(path, "TEMPER")!.Text.Contains("Attack Speed"));
    Check("UpgradePath: TEMPER shows the temper counter verbatim", Step(path, "TEMPER")!.Text.Contains("0/3"));
    Check("UpgradePath: TEMPER mentions the Scroll of Restoration", Step(path, "TEMPER")!.Warning!.Contains("Scroll of Restoration"));
    Check("UpgradePath: ENCHANT targets the missing non-temper affix", Step(path, "ENCHANT")!.Text.Contains("Dexterity"));
    Check("UpgradePath: ENCHANT warns it binds the item", Step(path, "ENCHANT")!.Warning!.Contains("binds"));
    Check("UpgradePath: MASTERWORK estimates Obducite", Step(path, "MASTERWORK")!.Cost!.Contains("Obducite"));
    Check("UpgradePath: SOCKET fills the empty socket", Step(path, "SOCKET")!.Text.Contains("empty socket"));
    Check("UpgradePath: IMPRINT names the wanted aspect", Step(path, "IMPRINT")!.Text.Contains("Aspect of Disobedience"));

    // a finished item: all wanted affixes present, masterworked to cap, socket filled, aspect carried ⇒ no steps
    var doneTarget = new TargetGear { Slot = "Boots", Aspect = "Aspect of the Expectant", Affixes = {
        new TargetAffix { Name = "Movement Speed" }, new TargetAffix { Name = "Maximum Life" } } };
    var doneItem = new Item { Name = "Boots of the Expectant", Slot = "boots", IsAncestral = true, Quality = 25,
        SocketCount = 0, EmptySockets = 0, Aspect = "Aspect of the Expectant",
        Affixes = { new Affix { Text = "Movement Speed", Value = 16 }, new Affix { Text = "Maximum Life", Value = 900 } } };
    Eq("UpgradePath: a finished item needs no steps", 0, UpgradePath.ForSlot(doneTarget, doneItem).Count);

    // GA caution on enchant; 2+ wrong affixes ⇒ replace/Focused-Reroll guidance
    var gaItem = new Item { Name = "GA Helm", Slot = "helm", IsAncestral = true, GreaterAffixCount = 1, Quality = 25,
        Affixes = { new Affix { Text = "Maximum Life", Value = 1500, Min = 1000, Max = 1600 } } };
    var gaPath = UpgradePath.ForSlot(new TargetGear { Slot = "Helm", Affixes = {
        new TargetAffix { Name = "Maximum Life", Min = 1000 }, new TargetAffix { Name = "Dexterity" } } }, gaItem);
    Check("UpgradePath: ENCHANT on a GA item warns the Greater Affix is at risk", Step(gaPath, "ENCHANT")!.Warning!.Contains("Greater Affix"));
    var twoWrong = UpgradePath.ForSlot(new TargetGear { Slot = "Helm", Affixes = {
        new TargetAffix { Name = "Maximum Life" }, new TargetAffix { Name = "Dexterity" }, new TargetAffix { Name = "Intelligence" } } },
        new Item { Name = "Bad", Slot = "helm", Affixes = { new Affix { Text = "Maximum Life", Value = 1500 } } });
    Check("UpgradePath: 2+ wrong affixes ⇒ replace or Focused Reroll", Step(twoWrong, "ENCHANT")!.Text.Contains("Focused Reroll"));

    // a unique can't be imprinted, so no IMPRINT step even when the slot wants an aspect
    var uniPath = UpgradePath.ForSlot(new TargetGear { Slot = "Chest", Aspect = "Aspect of Y", Affixes = { new TargetAffix { Name = "Maximum Life" } } },
        new Item { Name = "Shroud", Slot = "chest", IsUnique = true, Rarity = "Unique", Affixes = { new Affix { Text = "Maximum Life", Value = 1500 } } });
    Check("UpgradePath: uniques get no IMPRINT step (can't imprint a unique)", Step(uniPath, "IMPRINT") == null);

    // add-a-socket when below capacity (helm holds 2; item has 1, none empty)
    var addSock = UpgradePath.ForSlot(new TargetGear { Slot = "Helm", Affixes = { new TargetAffix { Name = "Maximum Life" } } },
        new Item { Name = "H", Slot = "helm", IsAncestral = true, Quality = 25, SocketCount = 1, EmptySockets = 0,
            Affixes = { new Affix { Text = "Maximum Life", Value = 1500 } } });
    Check("UpgradePath: SOCKET suggests adding one when below the slot's capacity",
        Step(addSock, "SOCKET")!.Text.Contains("Add a socket") && Step(addSock, "SOCKET")!.Cost!.Contains("Scattered Prism"));

    // capstone reroll when masterworked to cap but the +50% landed off-build
    var capItem = new Item { Name = "Cap Helm", Slot = "helm", IsAncestral = true, Quality = 25, CapstoneAffix = "Thorns",
        Affixes = { new Affix { Text = "Maximum Life", Value = 1500 } } };
    var capPath = UpgradePath.ForSlot(new TargetGear { Slot = "Helm", Affixes = { new TargetAffix { Name = "Maximum Life" } } }, capItem);
    Check("UpgradePath: off-build Capstone ⇒ reroll-Capstone step (Neathiron)",
        Step(capPath, "MASTERWORK")?.Text.Contains("Capstone") == true && Step(capPath, "MASTERWORK")!.Cost!.Contains("Neathiron"));
}

// ---- v0.32: Torment-tier gating + IP-tier rules ----
{
    // a build with a gap, at Torment 5 → a "push to the next gated tier" recommendation appears
    var gapTarget = new TargetBuild { Gear = { new TargetGear { Slot = "Helm", Affixes = {
        new TargetAffix { Name = "Maximum Life" }, new TargetAffix { Name = "Intelligence" } } } } };
    var gapLive = new LiveBuild { Gear = { new Item { Name = "Weak Helm", Slot = "helm", Affixes = {
        new Affix { Text = "Maximum Life", Value = 500 } } } } };   // Intelligence missing ⇒ build < 100%
    var gapReport = DiffEngine.Diff(gapTarget, gapLive);
    var t5 = Activities.Recommend(gapReport, new GuideContext(5, "Rogue"));
    Check("Tier gate: at Torment 5 a 'Push to Torment 6' rec appears (Greater Lair Keys)",
        t5.Any(a => a.Title.Contains("Push to Torment 6") && a.Detail.Contains("Greater Lair Keys")));
    Check("Tier gate: the push rec names the Pit tier to clear", t5.Any(a => a.Title.Contains("Pit 40")));
    // no torment known ⇒ no push rec (graceful), and at T12 there's nothing higher to push to
    Check("Tier gate: no push rec without a known Torment", !Activities.Recommend(gapReport).Any(a => a.Title.StartsWith("Push to Torment")));
    Check("Tier gate: no push rec at Torment 12 (capped)", !Activities.Recommend(gapReport, new GuideContext(12, "Rogue")).Any(a => a.Title.StartsWith("Push to Torment")));

    // IP-tier: at endgame, a 900 Ancestral candidate beats a sub-900 equipped piece at an otherwise-equal slot
    var ipTarget = new TargetBuild { Gear = { new TargetGear { Slot = "Gloves", Affixes = {
        new TargetAffix { Name = "Attack Speed" }, new TargetAffix { Name = "Critical Strike Chance" } } } } };
    var eq850 = new Item { Name = "Old Gloves", Slot = "gloves", ItemPower = 850, Equipped = true, Affixes = {
        new Affix { Text = "Attack Speed", Value = 8 }, new Affix { Text = "Critical Strike Chance", Value = 8 } } };
    var cand900 = new Item { Name = "New Gloves", Slot = "gloves", ItemPower = 900, IsAncestral = true, Affixes = {
        new Affix { Text = "Attack Speed", Value = 8 }, new Affix { Text = "Critical Strike Chance", Value = 8 } } };
    var ipLive = new LiveBuild { Gear = { eq850 } };
    Check("IP-tier: WITHOUT a Torment context the equal-affix 900 item is not an upgrade",
        !UpgradeScorer.Score(ipTarget, ipLive, new[] { cand900 })[0].IsUpgrade);
    Check("IP-tier: IN Torment the 900 Ancestral beats the equal-affix 850 equipped",
        UpgradeScorer.Score(ipTarget, ipLive, new[] { cand900 }, 8)[0].RawUpgrade);

    // Verdict: in Torment a sub-900 non-Ancestral off-build item reads as junk "below the floor"
    var lowItem = new Item { Name = "Low Boots", Slot = "boots", ItemPower = 800, IsAncestral = false };
    var vLow = Verdicts.For(new ScoredItem { Item = lowItem, SlotLabel = "Boots", SlotTarget = 2 },
        new VerdictContext(new TargetBuild(), "Rogue", new List<Item>(), 8));
    Eq("Verdict floor: sub-900 in Torment ⇒ Junk", Verdict.Junk, vLow.V);
    Check("Verdict floor: reason cites the 900 Ancestral floor", vLow.Reason.Contains("900 Ancestral floor"));
}

// ---- v0.33: name-based item icons (BaseIconIndex) ----
{
    // inline data.min.json-shaped fixture replicating the verified catalog shapes (real handles where known)
    const string iconJson = @"{ ""items"": {
        ""1HSword_Unique_Generic_002"":            { ""type"": ""Sword"", ""image"": 2397813553, ""name"": ""El'Druin, Sword of Justice"" },
        ""Talisman_Charm_Unique_1HSword_002"":     { ""type"": ""Charm"", ""image"": 1033042452, ""name"": ""El'Druin, Sword of Justice"" },
        ""Warplans_Currency"":                     { ""type"": ""Currency"", ""image"": 3589037566, ""name"": ""Marks of El'Druin"" },
        ""1HSword_Legendary_Generic_001"":         { ""type"": ""Sword"", ""image"": 2397813484, ""name"": ""Obsidian Blade"" },
        ""1HSword_Blade_001"":                     { ""type"": ""Sword"", ""image"": 5004, ""name"": ""Blade"" },
        ""ChestArmor_Tunic_001"":                  { ""type"": ""ChestArmor"", ""image"": 5001, ""name"": ""Tunic"" },
        ""1HSword_Common_Generic_Normal_001"":     { ""type"": ""Sword"", ""image"": 5002, ""name"": ""Sword"" },
        ""1HSword_Gambling"":                      { ""type"": ""Sword"", ""image"": 5003, ""name"": ""Sword"" },
        ""S01_CagedHeart"":                        { ""type"": ""Ring"", ""image"": 7001, ""name"": ""Caged Heart"" },
        ""MalignantOrb_CagedHeart"":               { ""type"": ""Ring"", ""image"": 7002, ""name"": ""Caged Heart"" },
        ""TwinMapped"":                            { ""type"": ""Sword"", ""image"": 6001, ""name"": ""Twin Item"" },
        ""TwinUnmapped"":                          { ""type"": ""Sword"", ""image"": 6002, ""name"": ""Twin Item"" }
    } }";
    BaseIconIndex.HasMapping = h => h != 6002;   // everything mapped except the unmapped twin
    BaseIconIndex.FromJson(iconJson);

    // a unique not in any build resolves to its OWN art by name (the El'Druin failure) — sword, not the
    // charm or currency twin
    Eq("Icon: unique resolves by name to its sword art", 2397813553L, BaseIconIndex.HandleFor("El'druin, Sword Of Justice", "Sword", "weapon") ?? -1);
    Eq("Icon: with no type, equipment beats the charm/currency twin", 2397813553L, BaseIconIndex.HandleFor("El'Druin, Sword of Justice", null, null) ?? -1);

    // a prefixed legendary resolves to its base-name art (the Crushing Obsidian Blade failure)
    Eq("Icon: affix-prefixed legendary → base-name handle", 2397813484L, BaseIconIndex.HandleFor("Crushing Obsidian Blade", "Sword", "weapon") ?? -1);
    // longest-first: 'obsidian blade' wins over the shorter 'blade' base
    Check("Icon: longest base-name wins (obsidian blade, not blade)", BaseIconIndex.HandleFor("Crushing Obsidian Blade", "Sword", "weapon") == 2397813484L);

    // a magic '<base> of <suffix>' resolves via the prefix path
    Eq("Icon: magic '<base> of …' → base handle (prefix path)", 5001L, BaseIconIndex.HandleFor("Tunic of the Stalwart", "ChestArmor", "chest") ?? -1);

    // a random rare name (no catalog entry) falls back to the representative base-type handle
    Eq("Icon: unknown rare name → type fallback handle", 5002L, BaseIconIndex.HandleFor("Grasping Fang", "Sword", "weapon") ?? -1);
    Check("Icon: wholly unknown item → null (silhouette)", BaseIconIndex.HandleFor("Zzqq Nonsense", "Bogus", null) == null);

    // disambiguation: an unmapped same-name twin is demoted (HasMapping), seasonal id deprioritized
    Eq("Icon: prefers the EXTRACTABLE twin over the unmapped one", 6001L, BaseIconIndex.HandleFor("Twin Item", "Sword", "weapon") ?? -1);
    Eq("Icon: prefers the non-seasonal twin on a name tie", 7002L, BaseIconIndex.HandleFor("Caged Heart", "Ring", "ring") ?? -1);

    // Reset clears the memo so a re-import is reflected
    BaseIconIndex.FromJson(@"{ ""items"": { ""1HSword_Unique_Generic_002"": { ""type"": ""Sword"", ""image"": 99999, ""name"": ""El'Druin, Sword of Justice"" } } }");
    Eq("Icon: re-import via FromJson clears the memo", 99999L, BaseIconIndex.HandleFor("El'Druin, Sword of Justice", "Sword", "weapon") ?? -1);
    BaseIconIndex.Reset();
    BaseIconIndex.HasMapping = h => GameDataIcons.HasMapping(h);   // restore the real predicate
}

// ---- v0.44.2: the item-DB self-heal must target the SAME file the cache clear deletes ----
// (BaseIconIndex reads maxroll_data.min.json to resolve gear names → icons; a divergence between
//  what the clear deletes and what the self-heal restores would silently leave icons broken)
{
    var p = MaxrollImporter.GameDataCachePath.Replace('/', '\\');
    Check("GameDataCachePath is maxroll_data.min.json", p.EndsWith("maxroll_data.min.json", StringComparison.OrdinalIgnoreCase));
    Check("GameDataCachePath lives under the d4scanner cache dir", p.Contains("\\d4scanner\\cache\\", StringComparison.OrdinalIgnoreCase));
}

// ---- v0.34: tombstones + inventory dedup ----
{
    Item Mk(string name, string slot, long ticks, ItemSource src = ItemSource.Tts) =>
        new() { Name = name, Slot = slot, LastScannedTicks = ticks, Source = src };
    long t0 = 1_000_000_000L;
    var tmpDir = Path.Combine(Path.GetTempPath(), "d4s_tomb_" + Guid.NewGuid().ToString("N"));
    var tombPath = Path.Combine(tmpDir, "tombstones.json");

    var ts = new TombstoneStore(tombPath);
    var ring = Mk("Tal Ring", "ring", t0);
    ts.Add(ring, t0 + 100);
    Check("Tombstone: hides an item whose sighting is no newer than the tombstone", ts.ShouldHide(Mk("Tal Ring", "ring", t0)));
    Check("Tombstone: a NEWER sighting is NOT hidden (player re-acquired it)", !ts.ShouldHide(Mk("Tal Ring", "ring", t0 + 500)));
    // Add clamps the tombstone past the item's own sighting, so a same-tick re-emission can't slip through
    var tsClamp = new TombstoneStore(Path.Combine(tmpDir, "b.json"));
    tsClamp.Add(Mk("X", "helm", t0 + 9_000_000_000L), t0);   // 'now' is BEFORE the item's sighting
    Check("Tombstone: Add clamps tick past the item's own sighting", tsClamp.ShouldHide(Mk("X", "helm", t0 + 9_000_000_000L)));

    // ObserveSightings purges the tombstone when a newer sighting shows up (re-hover/re-equip)
    Eq("Tombstone: a newer sighting purges the tombstone", 1, ts.ObserveSightings(new[] { Mk("Tal Ring", "ring", t0 + 500) }));
    Check("Tombstone: …and the item is visible afterwards", !ts.ShouldHide(Mk("Tal Ring", "ring", t0)));

    // Apply = observe + filter
    var ts3 = new TombstoneStore(Path.Combine(tmpDir, "c.json"));
    ts3.Add(Mk("Junk Boots", "boots", t0), t0 + 100);
    var applied = ts3.Apply(new List<Item> { Mk("Junk Boots", "boots", t0), Mk("Good Helm", "helm", t0) });
    Check("Tombstone Apply: hides the tombstoned item, keeps the rest", applied.Count == 1 && applied[0].Name == "Good Helm");

    // cap at 500 — oldest evicted
    var tsCap = new TombstoneStore(Path.Combine(tmpDir, "cap.json"));
    for (int i = 0; i < 520; i++) tsCap.Add(Mk("Item" + i, "ring", t0 + i), t0 + 1000 + i);
    Check("Tombstone: store caps at 500 entries", tsCap.Count <= 500);
    Check("Tombstone: the oldest tombstone was evicted", !tsCap.ShouldHide(Mk("Item0", "ring", t0)));

    // 30-day purge
    var tsPurge = new TombstoneStore(Path.Combine(tmpDir, "purge.json"));
    long now = t0 + TimeSpan.FromDays(40).Ticks;
    tsPurge.Add(Mk("Old", "ring", t0), t0);                                   // 40 days old
    tsPurge.Add(Mk("Recent", "ring", now - TimeSpan.FromDays(1).Ticks), now); // 1 day old
    Eq("Tombstone: purge drops only the >30-day entry", 1, tsPurge.PurgeOlderThan(TimeSpan.FromDays(30), now));
    Check("Tombstone: the recent tombstone survives the purge", tsPurge.ShouldHide(Mk("Recent", "ring", now - TimeSpan.FromDays(1).Ticks)));

    // JSON round-trip
    var tsRt = new TombstoneStore(Path.Combine(tmpDir, "rt.json"));
    tsRt.Add(Mk("Persist Me", "amulet", t0), t0 + 50); tsRt.Save();
    Check("Tombstone: survives a save/reload round-trip",
        new TombstoneStore(Path.Combine(tmpDir, "rt.json")).ShouldHide(Mk("Persist Me", "amulet", t0)));

    // RESURRECTION regression: a tombstoned item re-emitted by the merge path is still hidden by Apply
    var tsRes = new TombstoneStore(Path.Combine(tmpDir, "res.json"));
    var persisted = new List<Item> { Mk("Stash Item", "ring", t0) };
    tsRes.Add(persisted[0], t0 + 100);
    var merged = LiveGearResolver.MergeInventory(persisted, new List<Item> { Mk("Stash Item", "ring", t0) });
    Check("Resurrection: the re-emitted tombstoned item is filtered back out", tsRes.Apply(merged).All(i => i.Name != "Stash Item"));
    var mergedNewer = LiveGearResolver.MergeInventory(persisted, new List<Item> { Mk("Stash Item", "ring", t0 + 999) });
    Check("Resurrection: a newer sighting returns the item and clears the tombstone",
        tsRes.Apply(mergedNewer).Any(i => i.Name == "Stash Item"));

    // DedupeInventory: same name|slot collapses, Tts over Ocr, newest wins
    var deduped = LiveGearResolver.DedupeInventory(new List<Item> {
        Mk("Dup Ring", "ring", t0, ItemSource.Ocr), Mk("Dup Ring", "ring", t0 + 200, ItemSource.Tts), Mk("Other", "helm", t0) });
    Eq("DedupeInventory: collapses same name|slot", 2, deduped.Count);
    Check("DedupeInventory: keeps the Tts copy over Ocr", deduped.First(i => i.Name == "Dup Ring").Source == ItemSource.Tts);

    // MergeDemoted: name|slot guard prevents duplicate appends
    var inv = new List<Item> { Mk("Demote Me", "boots", t0) };
    Eq("MergeDemoted: a name|slot match is not appended twice", 1,
        LiveGearResolver.MergeDemoted(inv, new List<Item> { Mk("Demote Me", "boots", t0 + 5) }).Count);
    Eq("MergeDemoted: a genuinely new demoted item IS added", 2,
        LiveGearResolver.MergeDemoted(inv, new List<Item> { Mk("New Boots", "boots", t0) }).Count);

    try { Directory.Delete(tmpDir, recursive: true); } catch { }
}

// ---- v0.36: extras as rows, harvested max target, DO NEXT presence notes ----
{
    // ExtraRows: off-build affixes become rows with Status="extra", carrying value/range/IsGreater
    var extras = DiffEngine.ExtraRows(new[] {
        new Affix { Text = "Thorns", Value = 500, Min = 200, Max = 600 },
        new Affix { Text = "Lucky Hit Chance", Value = 12, IsPercent = true, IsGreater = true },
        new Affix { Text = "50 (+30/25) Quality" } });   // quality meta line is skipped
    Eq("ExtraRows: skips the quality meta line", 2, extras.Count);
    Check("ExtraRows: status is 'extra'", extras.All(r => r.Status == "extra"));
    Check("ExtraRows: carries value + range + GA", extras[0].ValueNum == 500 && extras[0].MaxNum == 600
        && extras.First(r => r.Label.Contains("Lucky")).IsGreater);

    // Diff populates Group.ExtraAffixes with the equipped item's off-build affixes (as Affix objects)
    var exTarget = new TargetBuild { Gear = { new TargetGear { Slot = "Helm", Affixes = { new TargetAffix { Name = "Maximum Life" } } } } };
    var exLive = new LiveBuild { Gear = { new Item { Name = "H", Slot = "helm", Affixes = {
        new Affix { Text = "Maximum Life", Value = 1500 }, new Affix { Text = "Thorns", Value = 400, Max = 600 } } } } };
    var exGroup = DiffEngine.Diff(exTarget, exLive).Categories.First(c => c.Id == "gear").Groups[0];
    Check("Diff: ExtraAffixes holds the off-build Thorns affix", exGroup.ExtraAffixes.Any(a => a.Text == "Thorns" && a.Max == 600));
    Check("Diff: ExtraAffixes excludes the build-matched affix", !exGroup.ExtraAffixes.Any(a => a.Text.Contains("Maximum Life")));

    // AffixAggregate harvests the max-roll ceiling from ANY owned copy — even when the equipped piece had no range
    var aggTarget = new TargetBuild { Gear = {
        new TargetGear { Slot = "Ring1", Affixes = { new TargetAffix { Name = "All Damage Multiplier" } } },
        new TargetGear { Slot = "Ring2", Affixes = { new TargetAffix { Name = "All Damage Multiplier" } } } } };
    var equippedNoRange = new Item { Name = "Band", Slot = "ring", Affixes = { new Affix { Text = "All Damage Multiplier", Value = 13, IsMultiplier = true } } };
    var aggLive = new LiveBuild { Gear = { equippedNoRange } };
    var aggCat = DiffEngine.Diff(aggTarget, aggLive).Categories.First(c => c.Id == "gear");
    var ownedCopy = new Item { Name = "Better Band", Slot = "ring", Affixes = { new Affix { Text = "All Damage Multiplier", Value = 16, Min = 10, Max = 18, IsMultiplier = true } } };
    var harvested = AffixAggregate.ForGear(aggCat, new[] { equippedNoRange, ownedCopy })[0];
    Check("Harvest: a max from an inventory copy gives a real target", harvested.WantsKnown);
    Eq("Harvest: target = harvested max (18) × pieces (2)", 36.0, harvested.WantsTotal);
    // with NO copy carrying a range, the best VALUE seen becomes the ceiling
    var valueOnly = AffixAggregate.ForGear(aggCat, new[] { equippedNoRange })[0];
    Eq("Harvest: value-only fallback ceiling = best value (13) × pieces (2)", 26.0, valueOnly.WantsTotal);

    // BuildGuide presence notes: aspect on a DIFFERENT owned piece, and a rune socketed elsewhere
    var bgTarget = new TargetBuild {
        Gear = { new TargetGear { Slot = "Helm", Sockets = { "Rune: Bac" }, Affixes = { new TargetAffix { Name = "Maximum Life" } } } },
        Aspects = { "Aspect of Disobedience" } };
    var bgLive = new LiveBuild {
        Gear = { new Item { Name = "Bare Helm", Slot = "helm", Affixes = { new Affix { Text = "Maximum Life", Value = 1500 } } } },
        Inventory = {
            new Item { Name = "Chest of Disobedience", Slot = "chest", Rarity = "Legendary" },   // carries the wanted aspect by name
            new Item { Name = "Old Boots", Slot = "boots", SocketedRunes = { "Bac" } } } };       // has the wanted rune socketed
    var bgRep = DiffEngine.Diff(bgTarget, bgLive);
    var bgSteps = BuildGuide.Steps(bgRep, bgLive);
    var imprint = bgSteps.FirstOrDefault(s => s.Verb == "IMPRINT");
    Check("BuildGuide: IMPRINT notes the aspect exists on another piece", imprint != null && imprint.Detail!.Contains("Chest of Disobedience"));
    var socket = bgSteps.FirstOrDefault(s => s.Verb == "SOCKET");
    Check("BuildGuide: a SOCKET step is emitted for the unfilled wanted socket", socket != null && socket.Text.Contains("Bac"));
    Check("BuildGuide: the SOCKET step notes the rune is socketed elsewhere", socket!.Detail!.Contains("Old Boots"));
    // back-compat: no live build → plain Occultist detail, no presence note
    var plain = BuildGuide.Steps(bgRep).FirstOrDefault(s => s.Verb == "IMPRINT");
    Check("BuildGuide: without a live build the IMPRINT detail stays plain", plain != null && plain.Detail == "at the Occultist");
}

// ---- v0.37: umbrella-affix matching (an umbrella ON THE ITEM satisfies a SPECIFIC want, one-directional) ----
{
    // AffixSatisfies (boolean, non-consuming) — the table, isolated from PhraseMatch where possible
    Check("Umbrella: All Stats → Dexterity", DiffEngine.AffixSatisfies("Dexterity", new Affix { Text = "All Stats" }));
    Check("Umbrella: All Stats → Willpower", DiffEngine.AffixSatisfies("Willpower", new Affix { Text = "All Stats" }));
    Check("Umbrella: All Damage Multiplier → Shadow Damage Multiplier",
        DiffEngine.AffixSatisfies("Shadow Damage Multiplier", new Affix { Text = "All Damage Multiplier" }));
    Check("Umbrella: All Damage Multiplier → Damage Over Time Multiplier",
        DiffEngine.AffixSatisfies("Damage Over Time Multiplier", new Affix { Text = "All Damage Multiplier" }));
    Check("Umbrella: Resistance to All Elements → Shadow Resistance",
        DiffEngine.AffixSatisfies("Shadow Resistance", new Affix { Text = "Resistance to All Elements" }));
    Check("Umbrella: +1 to All Skills → Ranks to Dance of Knives",
        DiffEngine.AffixSatisfies("Ranks to Dance of Knives", new Affix { Text = "+1 to All Skills" }));
    Check("Umbrella: bare Damage → Fire Damage", DiffEngine.AffixSatisfies("Fire Damage", new Affix { Text = "Damage" }));
    // one-directional: a SPECIFIC affix never satisfies an umbrella want
    Check("Umbrella: reverse never matches (Dexterity does NOT grant All Stats)",
        !DiffEngine.AffixSatisfies("All Stats", new Affix { Text = "Dexterity" }));
    Check("Umbrella: unrelated miss (All Stats does NOT grant Armor)",
        !DiffEngine.AffixSatisfies("Armor", new Affix { Text = "All Stats" }));
    Check("Umbrella: All Damage Multiplier does NOT grant a non-multiplier (Shadow Resistance)",
        !DiffEngine.AffixSatisfies("Shadow Resistance", new Affix { Text = "All Damage Multiplier" }));

    // EvalSlot: value flows from the umbrella + ViaUmbrella note set + NON-consumption (one covers many)
    var uTg = new TargetGear { Slot = "Amulet", Affixes = {
        new TargetAffix { Name = "Strength" }, new TargetAffix { Name = "Dexterity" } } };
    var uItem = new Item { Name = "All-Stat Amulet", Slot = "amulet", Affixes = {
        new Affix { Text = "All Stats", Value = 80, Min = 50, Max = 100 } } };
    var uRows = DiffEngine.EvalSlot(uTg, uItem, out var uExtras);
    Check("Umbrella EvalSlot: All Stats satisfies Strength", uRows.First(r => r.Label == "Strength").Done);
    Check("Umbrella EvalSlot: same All Stats also satisfies Dexterity (non-consuming)", uRows.First(r => r.Label == "Dexterity").Done);
    Eq("Umbrella EvalSlot: ViaUmbrella note carries the umbrella text", "All Stats", uRows.First(r => r.Label == "Dexterity").ViaUmbrella);
    Eq("Umbrella EvalSlot: the umbrella value flows into the row", 80d, uRows.First(r => r.Label == "Strength").ValueNum);
    Check("Umbrella EvalSlot: a load-bearing umbrella is NOT listed as an off-build extra", !uExtras.Any(e => e.Contains("All Stats")));

    // preference: a real specific affix is consumed for its want; the umbrella stays an extra when it covered nothing
    var pTg = new TargetGear { Slot = "Ring", Affixes = { new TargetAffix { Name = "Dexterity" } } };
    var pItem = new Item { Name = "R", Slot = "ring", Affixes = {
        new Affix { Text = "All Stats", Value = 60 },
        new Affix { Text = "Dexterity", Value = 90, Min = 10, Max = 100 } } };
    var pRows = DiffEngine.EvalSlot(pTg, pItem, out var pExtras);
    Check("Umbrella preference: the specific Dexterity is used, not the umbrella", pRows.First(r => r.Label == "Dexterity").ViaUmbrella == null);
    Eq("Umbrella preference: the specific value flows (90, not 60)", 90d, pRows.First(r => r.Label == "Dexterity").ValueNum);
    Check("Umbrella preference: the unused All Stats shows as an extra", pExtras.Any(e => e.Contains("All Stats")));

    // scorer integration: an item whose only match is via an umbrella still scores the slot
    var sTg = new TargetGear { Slot = "Amulet", Affixes = { new TargetAffix { Name = "Intelligence" } } };
    var sItem = new Item { Slot = "amulet", Affixes = { new Affix { Text = "All Stats", Value = 80, Min = 50, Max = 100 } } };
    Eq("Umbrella ScoreSlot: an umbrella-only match counts toward the slot", 1, DiffEngine.ScoreSlot(sTg, sItem));
}

// ---- v0.37: unique/mythic requirements (TargetUnique now carries the build's wanted secondary affixes) ----
{
    // round-trips through the saved-build serializer (back-compatible: empty list omits cleanly)
    var u = new TargetUnique { Name = "Etna's Lost Dagger", Slot = "weapon", Affixes = {
        new TargetAffix { Name = "Dexterity", Min = 125 }, new TargetAffix { Name = "Maximum Life" } } };
    var json = System.Text.Json.JsonSerializer.Serialize(u, Json.Opts);
    var back = System.Text.Json.JsonSerializer.Deserialize<TargetUnique>(json, Json.Opts)!;
    Eq("Unique round-trip: affix count preserved", 2, back.Affixes.Count);
    Eq("Unique round-trip: affix name preserved", "Dexterity", back.Affixes[0].Name);
    Eq("Unique round-trip: affix min preserved", 125d, back.Affixes[0].Min);

    // an OWNED unique is compared against the build's wanted secondaries via a synthesized TargetGear
    var synth = new TargetGear { Slot = u.Slot ?? "", Affixes = u.Affixes };
    var ownedHigh = new Item { Name = "Etna's Lost Dagger", Slot = "weapon", Affixes = {
        new Affix { Text = "Dexterity", Value = 140 },           // ≥ 125 → met
        new Affix { Text = "Maximum Life", Value = 1800 } } };   // present → met
    var rOwned = DiffEngine.EvalSlot(synth, ownedHigh, out _);
    Check("Unique compare: met affix shows met", rOwned.First(r => r.Label == "Dexterity").Status == "met");
    Check("Unique compare: present affix (no threshold) shows met", rOwned.First(r => r.Label == "Maximum Life").Status == "met");

    // a MISSING / under-rolled unique shows exactly what the build wants (the panel's BUILD-WANTS rows)
    var rMissing = DiffEngine.EvalSlot(synth, null, out _);
    Eq("Unique want-rows: one row per wanted affix even when unowned", 2, rMissing.Count);
    Check("Unique want-rows: unowned affix is missing", rMissing.All(r => r.Status == "missing"));
    Check("Unique want-rows: the threshold is still shown", rMissing.First(r => r.Label == "Dexterity").Need != null);
}

// ---- v0.37: socket truth — filled/wanted/known ints drive BOTH the text and the bar ----
{
    TargetBuild SockTarget() => new TargetBuild { Gear = { new TargetGear {
        Slot = "Helm", Sockets = { "Rune: A", "Rune: B" }, Affixes = { new TargetAffix { Name = "Maximum Life" } } } } };
    Group SockGroup(Item it) => DiffEngine.Diff(SockTarget(), new LiveBuild { Gear = { it } })
        .Categories.First(c => c.Id == "gear").Groups[0];

    // 1. capacity known, one empty → 1/2 filled
    var sg1 = SockGroup(new Item { Name = "H", Slot = "helm", SocketCount = 2, EmptySockets = 1,
        Affixes = { new Affix { Text = "Maximum Life", Value = 1000 } } });
    Eq("Socket cap-known: wanted 2", 2, sg1.SocketsWanted);
    Eq("Socket cap-known: filled 1", 1, sg1.SocketsFilled);
    Check("Socket cap-known: known", sg1.SocketsKnown);
    Check("Socket cap-known: status reads 1/2", sg1.SocketStatus!.Contains("1/2"));

    // 2. runeword present → fully filled, done
    var sg2 = SockGroup(new Item { Name = "H", Slot = "helm", RunewordName = "Graceful Heart",
        Affixes = { new Affix { Text = "Maximum Life", Value = 1000 } } });
    Eq("Socket runeword: filled == wanted", 2, sg2.SocketsFilled);
    Check("Socket runeword: known and done", sg2.SocketsKnown && sg2.SocketsDone);

    // 3. empties voiced, no capacity line → 0/2 filled (honest, not a lie)
    var sg3 = SockGroup(new Item { Name = "H", Slot = "helm", EmptySockets = 2,
        Affixes = { new Affix { Text = "Maximum Life", Value = 1000 } } });
    Eq("Socket empties: filled 0", 0, sg3.SocketsFilled);
    Check("Socket empties: known", sg3.SocketsKnown);
    Check("Socket empties: status reads 0/2 (not '2/2 filled')", sg3.SocketStatus!.Contains("0/2"));

    // 4. nothing captured at all → NOT known, bar empty, text honest (v0.37 honesty; v0.44.2 reword).
    //    D4 doesn't voice gem/empty sockets on equipped gear, so the message must NOT tell the user to
    //    toggle Advanced Tooltips (they're already on) — it's a game limitation, stated plainly.
    var sg4 = SockGroup(new Item { Name = "H", Slot = "helm",
        Affixes = { new Affix { Text = "Maximum Life", Value = 1000 } } });
    Check("Socket no-info: not known", !sg4.SocketsKnown);
    Eq("Socket no-info: filled 0", 0, sg4.SocketsFilled);
    Eq("Socket no-info: wanted still 2 (denominator for the bar)", 2, sg4.SocketsWanted);
    Check("Socket no-info: status is honest about D4 not voicing equipped sockets",
        sg4.SocketStatus!.Contains("aren't voiced by D4"));
    Check("Socket no-info: status no longer tells the user to toggle Advanced Tooltips",
        !sg4.SocketStatus!.Contains("Advanced Tooltips"));
    Check("Socket no-info: status never claims it's filled", !sg4.SocketStatus!.Contains("2/2 filled"));
}

// ---- v0.38: vendor-gear leak — fail-safe classification, chunk-safe lookahead, self-healing gate ----
// Shapes below are lifted from the user's real d4_tts.log where vendor/bag hovers replaced worn gear.
{
    // (a) Purveyor of Curiosities: the gamble category word ("Helm") must NOT flip the panel back to
    //     Character (verified live: 'BUYBACK' → 'Helm' poisoned the panel before the first hover).
    var purveyor = new[] {
        "Purveyor of Curiosities", "BUYBACK", "No more items left to purchase.",
        "Helm", "50 Obols",
        "EQUIPPED",
        "STEEL AMULET", "Legendary Amulet", "800 Item Power", "+100 Dexterity [80 - 120]",
        "Right mouse button", "Buy",
    };
    var dp = LogWatcher.DiagnoseLines(purveyor);
    Eq("VendorLeak: purveyor item parsed", 1, dp.Items.Count);
    Eq("VendorLeak: gamble category word does not flip panel to Character", "Vendor", dp.Items[0].Panel);
    Check("VendorLeak: purveyor hover is NOT equipped", !dp.Items[0].Equipped);
    Eq("VendorLeak: purveyor hover classified VendorItem", "VendorItem", dp.Items[0].Context);

    // (b) the gamble category 'Ring' is ALSO a char-sheet slot header — it must not hand the vendor
    //     hover worn-gear credentials (the FromCharPanel hijack).
    var gambleRing = new[] {
        "Purveyor of Curiosities", "BUYBACK",
        "Ring", "50 Obols",
        "EQUIPPED",
        "TRIUMPHANT RING", "Legendary Ring", "800 Item Power", "+100 Dexterity [80 - 120]",
        "Right mouse button", "Buy",
    };
    var dr = LogWatcher.DiagnoseLines(gambleRing);
    Eq("VendorLeak: gamble-Ring item parsed", 1, dr.Items.Count);
    Check("VendorLeak: a header word voiced at a vendor does not classify as worn", !dr.Items[0].Equipped);
    Eq("VendorLeak: hijacked-header hover classified VendorItem", "VendorItem", dr.Items[0].Context);

    // (b2) Purveyor marker scrolled out of the rolling window (panel unknown) while the gamble category
    //      'Ring' still arms FromCharPanel: the OLD null-trusting fast-path stamped this WORN. A positive
    //      Character panel is now required, so a missed-marker vendor hover stays non-worn (its tail / the
    //      fail-safe classify it instead). This case fails without that fix.
    var gambleRingNoMarker = new[] {
        "Ring", "50 Obols",
        "EQUIPPED",
        "ZEALOUS BAND", "Legendary Ring", "800 Item Power", "+100 Dexterity [80 - 120]",
        "Right mouse button", "Buy",
    };
    var dn = LogWatcher.DiagnoseLines(gambleRingNoMarker);
    Eq("VendorLeak: missed-marker gamble item parsed", 1, dn.Items.Count);
    Check("VendorLeak: missed-marker gamble-Ring is NOT worn (positive Character panel now required)", !dn.Items[0].Equipped);
    Check("VendorLeak: missed-marker gamble-Ring not classified WornGear", dn.Items[0].Context != "WornGear");

    // (c) favorited bag item: the tail is Equip/…/Mark as Favorite — no "Mark as Junk" anywhere.
    //     'Equip', 'Drop', 'Hide Comparison' and 'Mark as Favorite' are now demote tokens.
    var favBag = new[] {
        "EQUIPPED",
        "CRIMSON HUNTER'S BOW", "Legendary Bow", "800 Item Power", "+100 Dexterity [80 - 120]",
        "Right mouse button",
        "Equip", "Shift key", "Left mouse button", "Link", "Control key", "Left mouse button",
        "Drop", "Shift key", "Hide Comparison", "Spacebar", "Mark as Favorite",
    };
    var df = LogWatcher.DiagnoseLines(favBag);
    Eq("VendorLeak: favorited bag hover parsed", 1, df.Items.Count);
    Check("VendorLeak: favorited bag hover demoted (Equip/Favorite are tokens now)", !df.Items[0].Equipped);

    // (d) vendor sell tab: the bare 'Sell' action (verified standalone in the real log) demotes.
    var sellTab = new[] {
        "EQUIPPED",
        "VIGOROUS BOOT BLADE", "Legendary Dagger", "800 Item Power", "+100 Dexterity [80 - 120]",
        "Right mouse button",
        "Sell", "Shift key", "Left mouse button", "Link",
    };
    Check("VendorLeak: sell-tab hover demoted ('Sell' is a token now)",
        !LogWatcher.DiagnoseLines(sellTab).Items[0].Equipped);

    // (e) fail-safe default: a bare EQUIPPED voice line with NO panel, NO header and NO action tail is
    //     NOT worn evidence — D4 voices it before most comparison-enabled bag/vendor hover names too.
    var bare = new[] {
        "EQUIPPED",
        "LORD'S HELMET", "Legendary Helm", "800 Item Power", "+100 Dexterity [80 - 120]",
        "Right mouse button",
        "EQUIPPED",   // tail lost — the next hover starts immediately (window closes at this boundary)
        "ADROIT HELM", "Legendary Helm", "810 Item Power", "+90 Dexterity [80 - 120]",
        "Right mouse button",
    };
    var db = LogWatcher.DiagnoseLines(bare);
    Eq("VendorLeak: both bare-EQUIPPED hovers parsed", 2, db.Items.Count);
    Check("VendorLeak: bare EQUIPPED alone no longer classifies as worn", db.Items.All(x => !x.Equipped));

    // (f) …but a genuinely worn item keeps every rescue path: the Unequip tail wins even when the
    //     panel says Inventory and a paper-doll label armed the header (the inventory-screen shape).
    var invWorn = new[] {
        "Inventory",
        "Ring",
        "EQUIPPED",
        "FROSTBITTEN BAND", "Legendary Ring", "800 Item Power", "+100 Dexterity [80 - 120]",
        "Right mouse button", "Unequip",
    };
    var dw = LogWatcher.DiagnoseLines(invWorn);
    Check("VendorLeak: Unequip tail still classifies worn outside the char panel",
        dw.Items.Count == 1 && dw.Items[0].Equipped);

    // (g) word-boundary tokens: '…Chance to Restore Primary Resource' must not demote via 'store'
    //     (a verified false demote of genuinely worn gear under the old substring matching).
    var restoreSafe = new[] {
        "Inventory",
        "EQUIPPED",
        "GUARDIAN SIGNET", "Legendary Ring", "800 Item Power", "+100 Dexterity [80 - 120]",
        "Right mouse button",
        "Lucky Hit: Up to a 5% Chance to Restore Primary Resource.",
        "Unequip",
    };
    var dg = LogWatcher.DiagnoseLines(restoreSafe);
    Check("VendorLeak: 'Restore' does not substring-match the Store token",
        dg.Items.Count == 1 && dg.Items[0].Equipped);

    // (h) chunk-safe lookahead: a hover whose action tail lands in the NEXT 500ms poll chunk must wait
    //     for it instead of one-shot-defaulting to equipped (the probabilistic half of the leak).
    var wA = new LogWatcher(Path.Combine(Path.GetTempPath(), "d4s_chunk_test_does_not_exist.log"));
    wA.FeedChunk(new[] {
        "EQUIPPED", "STEEL AMULET", "Legendary Amulet", "800 Item Power", "+100 Dexterity [80 - 120]",
        "Right mouse button" });   // the chunk ends exactly at the end marker — tail not yet written
    Eq("ChunkSafe: classification deferred at the chunk edge (nothing committed yet)", 0, wA.Build.Gear.Count);
    wA.FeedChunk(new[] { "Sell", "Shift key", "Left mouse button", "Mark as Junk" });
    Check("ChunkSafe: the next-chunk tail demotes to inventory", wA.Build.Inventory.Any(g => g.RawName == "STEEL AMULET"));
    Eq("ChunkSafe: the vendor-sell hover never reaches gear", 0, wA.Build.Gear.Count);

    // (i) the char-panel fast path needs no tail — a worn hover commits in its own chunk…
    var wB = new LogWatcher(Path.Combine(Path.GetTempPath(), "d4s_chunk_test_does_not_exist.log"));
    wB.FeedChunk(new[] {
        "Head", "EQUIPPED", "COWL OF THE NAMELESS", "Unique Helm", "800 Item Power",
        "+100 Dexterity [80 - 120]", "Right mouse button" });
    Check("ChunkSafe: char-panel hover classifies immediately (no tail needed)",
        wB.Build.Gear.Any(g => g.RawName == "COWL OF THE NAMELESS"));
    // …and a pending tail-less hover force-resolves (with the safe default) once the game goes quiet.
    var wC = new LogWatcher(Path.Combine(Path.GetTempPath(), "d4s_chunk_test_does_not_exist.log"));
    wC.FeedChunk(new[] {
        "EQUIPPED", "FROSTBITTEN LICH BLADE", "Legendary Sword", "800 Item Power",
        "+100 Dexterity [80 - 120]", "Right mouse button" });
    wC.FeedChunk(Array.Empty<string>());
    wC.FeedChunk(Array.Empty<string>());
    Eq("ChunkSafe: quiet-game force-resolution lands in inventory, not gear", 0, wC.Build.Gear.Count);
    Check("ChunkSafe: force-resolved item is still visible in inventory",
        wC.Build.Inventory.Any(g => g.RawName == "FROSTBITTEN LICH BLADE"));

    // (B5) a UTF-8 multibyte char split across two byte chunks must reassemble (streaming Decoder), not
    //      mojibake — the old per-chunk GetString decoded each lone half to U+FFFD, corrupting the name.
    var wU = new LogWatcher(Path.Combine(Path.GetTempPath(), "d4s_utf8_split_does_not_exist.log"));
    var b5Block = "Head\nEQUIPPED\nFLEUR DÉ LIS\nUnique Helm\n800 Item Power\n+100 Dexterity [80 - 120]\nRight mouse button\n";
    var b5Bytes = System.Text.Encoding.UTF8.GetBytes(b5Block);
    int b5split = System.Array.IndexOf(b5Bytes, (byte)0xC3) + 1;   // split BETWEEN the two bytes of 'É' (UTF-8 0xC3 0x89)
    wU.FeedBytes(b5Bytes[..b5split], b5split);                     // chunk ends mid-character
    var b5rest = b5Bytes[b5split..];
    wU.FeedBytes(b5rest, b5rest.Length);                           // continuation completes 'É'
    wU.FeedChunk(System.Array.Empty<string>());                    // force-resolve any pending
    var b5helm = wU.Build.Gear.FirstOrDefault(g => g.RawName != null && g.RawName.Contains("FLEUR"));
    Check("B5: split-multibyte worn item parsed", b5helm != null);
    if (b5helm != null)
    {
        Check("B5: accented char reassembled correctly (contains 'DÉ')", b5helm.RawName.Contains("DÉ"));
        Check("B5: no U+FFFD replacement char in the name", !b5helm.RawName.Contains('�'));
    }

    // (j) self-healing gate: a later correctly-classified non-equipped sighting EVICTS the equipped
    //     copy (the gate used to be a one-way ratchet — junked gear stayed on the paper doll forever).
    var wD = new LogWatcher(Path.Combine(Path.GetTempPath(), "d4s_chunk_test_does_not_exist.log"));
    wD.FeedChunk(new[] {
        "Head", "EQUIPPED", "ADROIT HELM", "Legendary Helm", "800 Item Power",
        "+100 Dexterity [80 - 120]", "Right mouse button", "Unequip" });
    Check("SelfHeal: helm starts on the paper doll", wD.Build.Gear.Any(g => g.RawName == "ADROIT HELM"));
    wD.FeedChunk(new[] {
        "EQUIPPED", "ADROIT HELM", "Legendary Helm", "800 Item Power",
        "+100 Dexterity [80 - 120]", "Right mouse button", "Mark as Junk" });
    Eq("SelfHeal: the junked re-sighting evicts the equipped copy", 0, wD.Build.Gear.Count);
    Check("SelfHeal: the item moves to inventory instead", wD.Build.Inventory.Any(g => g.RawName == "ADROIT HELM"));

    // (k) LatestPerSlot recency: the genuine item re-hovered LATER reclaims a 1-cap slot from an
    //     alphabetically-earlier impostor at the same panel position (the 'Adventurer's vs Cowl' bug).
    var lps = new List<Item> {
        new Item { Name = "Adventurer's Helm", RawName = "ADVENTURER'S HELM", Slot = "helm", SlotPosition = 1, LastScannedTicks = 100 },
        new Item { Name = "Cowl Of The Nameless", RawName = "COWL OF THE NAMELESS", Slot = "helm", SlotPosition = 1, LastScannedTicks = 200 },
    };
    var kept = LogWatcher.LatestPerSlot(lps);
    Eq("Recency: 1-cap helm slot keeps exactly one item", 1, kept.Count);
    Eq("Recency: the LATER scan wins, not the alphabetically-earlier name", "COWL OF THE NAMELESS", kept[0].RawName);

    // stash 'Take' still classifies a stash hover (regression guard on the rewritten tail scan)
    var stashTail = new[] {
        "EQUIPPED",
        "CRANEQUIN OF MALICE", "Legendary Crossbow", "800 Item Power", "+100 Dexterity [80 - 120]",
        "Right mouse button", "Take", "Shift key",
    };
    var dsT = LogWatcher.DiagnoseLines(stashTail);
    Check("VendorLeak: 'Take' tail still classifies StashItem (not worn)",
        dsT.Items.Count == 1 && !dsT.Items[0].Equipped && dsT.Items[0].Context == "StashItem");
}

// ---- v0.38.1: game-data icon extraction must not latch a transient CASC failure for the session ----
// (Diablo IV itself holding the storage at app start is the NORMAL failure — the app runs beside the game.)
{
    var backoff = TimeSpan.FromSeconds(60);
    Check("CascRetry: never-failed -> probe immediately", GameDataIcons.ShouldRetryCasc(1000, 0, backoff));
    long t0 = new DateTime(2026, 6, 12, 10, 0, 0, DateTimeKind.Utc).Ticks;
    Check("CascRetry: within the backoff -> hold", !GameDataIcons.ShouldRetryCasc(t0 + TimeSpan.FromSeconds(30).Ticks, t0, backoff));
    Check("CascRetry: after the backoff -> retry", GameDataIcons.ShouldRetryCasc(t0 + TimeSpan.FromSeconds(61).Ticks, t0, backoff));
    Check("CascRetry: exactly at the backoff -> retry", GameDataIcons.ShouldRetryCasc(t0 + backoff.Ticks, t0, backoff));
}

// ---- v0.39: stateful tooltip lines (durability / sell value / menu hints) stay out of PowerText ----
// Item identity must be content-only: two captures of the SAME item differ only when the item differs.
{
    var statefulItem = GearParser.ParseTooltipLines(new[] {
        "WORN KEEPSAKE", "Legendary Ring", "800 Item Power",
        "+100 Dexterity [80 - 120]",
        "Lucky Hit: Up to a 5% Chance to Execute Injured Non-Elites.",
        "Durability: 37/100",
        "Sell Value: 12,345 Gold",
        "Armory Loadout",
        "Mousewheel scroll down",
        "Scroll Down",
        "Requires Level 60" });
    Check("Stateful: item parses", statefulItem != null);
    Check("Stateful: durability/sell-value/menu lines dropped from PowerText",
        statefulItem!.PowerText.All(p => !p.StartsWith("Durability") && !p.StartsWith("Sell Value")
            && p != "Armory Loadout" && !p.StartsWith("Mousewheel") && p != "Scroll Down"));
    Check("Stateful: genuine power prose survives",
        statefulItem.PowerText.Any(p => p.Contains("Lucky Hit", StringComparison.OrdinalIgnoreCase)));
    // the combined "Durability: N/100. Tempers: a/b" line still feeds the temper counters
    var temperItem = GearParser.ParseTooltipLines(new[] {
        "STURDY HELM", "Legendary Helm", "800 Item Power",
        "+100 Dexterity [80 - 120]", "Durability: 100/100. Tempers: 5/5" });
    Check("Stateful: combined durability+tempers line still parses tempers",
        temperItem?.TemperUsed == 5 && temperItem?.TemperMax == 5);
}

// ---- v0.41: item identity — fingerprint v2, content-aware dedup, structured upgrade refs ----
{
    // (a) fingerprint v2: non-stateful metadata is identity; capture context is not
    Item FpItem() => new Item { Name = "Twin Blade", Slot = "weapon", ItemPower = 800, Quality = 10,
        Affixes = { new Affix { Text = "Dexterity", Value = 100 } } };
    var fpBase = GearList.Fingerprint(FpItem());
    var ipDiff = FpItem(); ipDiff.ItemPower = 810;
    Check("FingerprintV2: item power differs -> different identity", GearList.Fingerprint(ipDiff) != fpBase);
    var qDiff = FpItem(); qDiff.Quality = 25;
    Check("FingerprintV2: masterwork quality differs -> different identity", GearList.Fingerprint(qDiff) != fpBase);
    var tDiff = FpItem(); tDiff.TemperUsed = 1; tDiff.TemperMax = 3;
    Check("FingerprintV2: temper counters differ -> different identity", GearList.Fingerprint(tDiff) != fpBase);
    var stateDiff = FpItem(); stateDiff.Equipped = true; stateDiff.UiPanel = "Vendor"; stateDiff.SlotPosition = 2;
    stateDiff.LastScannedTicks = 999; stateDiff.Source = ItemSource.Ocr; stateDiff.PowerText.Add("some prose");
    Check("FingerprintV2: stateful capture context does NOT change identity", GearList.Fingerprint(stateDiff) == fpBase);

    // (b) LatestPerSlot contentIdentity: genuine duplicates survive; identical re-hovers collapse
    var dupA = new Item { Name = "Twin Blade", RawName = "TWIN BLADE", Slot = "weapon", ItemPower = 800,
        Affixes = { new Affix { Text = "Dexterity", Value = 100 } } };
    var dupB = new Item { Name = "Twin Blade", RawName = "TWIN BLADE", Slot = "weapon", ItemPower = 810,
        Affixes = { new Affix { Text = "Dexterity", Value = 95 } } };
    var rehover = new Item { Name = "Twin Blade", RawName = "TWIN BLADE", Slot = "weapon", ItemPower = 800,
        Affixes = { new Affix { Text = "Dexterity", Value = 100 } } };
    Eq("ContentIdentity: two different rolls of the same name BOTH kept",
        2, LogWatcher.LatestPerSlot(new[] { dupA, dupB }, 15, contentIdentity: true).Count);
    Eq("ContentIdentity: an identical re-hover still collapses",
        2, LogWatcher.LatestPerSlot(new[] { dupA, dupB, rehover }, 15, contentIdentity: true).Count);
    Eq("ContentIdentity: name-keyed dedup (default) is unchanged",
        1, LogWatcher.LatestPerSlot(new[] { dupA, dupB }, 15).Count);

    // (c) DedupeInventory content-aware: TTS duplicates survive; OCR collapses to its TTS anchor
    var ttsRoll1 = new Item { Name = "Dup Amulet", Slot = "amulet", Source = ItemSource.Tts, LastScannedTicks = 100,
        Affixes = { new Affix { Text = "Dexterity", Value = 80 } } };
    var ttsRoll2 = new Item { Name = "Dup Amulet", Slot = "amulet", Source = ItemSource.Tts, LastScannedTicks = 200,
        Affixes = { new Affix { Text = "Dexterity", Value = 90 } } };
    var ocrEcho  = new Item { Name = "Dup Amulet", Slot = "amulet", Source = ItemSource.Ocr, LastScannedTicks = 300,
        Affixes = { new Affix { Text = "Dexterity", Value = 91 } } };   // OCR mis-read of one of them
    var dd = LiveGearResolver.DedupeInventory(new List<Item> { ttsRoll1, ttsRoll2, ocrEcho });
    Eq("DedupeInventory: two TTS rolls survive; the OCR echo collapses into them", 2, dd.Count);
    Check("DedupeInventory: only TTS copies remain when TTS anchors the name", dd.All(i => i.Source == ItemSource.Tts));

    // (d) IsStaleRescanOf truth table
    var domBy = new Item { Name = "X", Slot = "ring", Affixes = { new Affix { Text = "Dexterity", Value = 100, Min = 50, Max = 90 } } };
    var inflatedLesser = new Item { Name = "X", Slot = "ring", Affixes = { new Affix { Text = "Dexterity", Value = 95, Min = 50, Max = 90 } } };
    var cleanLesser = new Item { Name = "X", Slot = "ring", Affixes = { new Affix { Text = "Dexterity", Value = 80, Min = 50, Max = 90 } } };
    var equalTwin = new Item { Name = "X", Slot = "ring", Affixes = { new Affix { Text = "Dexterity", Value = 95, Min = 50, Max = 90 } } };
    Check("StaleRescan: inflated + strictly dominated -> stale", GearList.IsStaleRescanOf(inflatedLesser, domBy, strict: true));
    Check("StaleRescan: clean base roll is never stale", !GearList.IsStaleRescanOf(cleanLesser, domBy, strict: true));
    Check("StaleRescan(strict): equal-value twins never drop each other",
        !GearList.IsStaleRescanOf(inflatedLesser, equalTwin, strict: true) && !GearList.IsStaleRescanOf(equalTwin, inflatedLesser, strict: true));
    Check("StaleRescan(non-strict): an equal-value inflated re-capture IS stale vs the equipped copy",
        GearList.IsStaleRescanOf(inflatedLesser, equalTwin, strict: false));

    // (e) UpgradeRef: the diff carries WHICH bag item is the upgrade, with a jumpable fingerprint.
    //     (fixtures discriminate by affix PRESENCE so the later roll-gate phase can't move them)
    var upTarget = new TargetBuild { Gear = { new TargetGear { Slot = "Helm", Affixes = {
        new TargetAffix { Name = "Dexterity" }, new TargetAffix { Name = "Maximum Life" } } } } };
    var upEq = new Item { Name = "Worn Helm", Slot = "helm", Equipped = true,
        Affixes = { new Affix { Text = "Dexterity", Value = 50 } } };
    var upBag = new Item { Name = "Bag Helm", Slot = "helm",
        Affixes = { new Affix { Text = "Dexterity", Value = 60 }, new Affix { Text = "Maximum Life", Value = 900 } } };
    var upRep = DiffEngine.Diff(upTarget, new LiveBuild { Gear = { upEq }, Inventory = { upBag } });
    var upGrp = upRep.Categories.First(c => c.Id == "gear").Groups[0];
    Eq("UpgradeRef: the bag upgrade is found", 1, upGrp.UpgradeItems.Count);
    Eq("UpgradeRef: carries the item name", "Bag Helm", upGrp.UpgradeItems[0].Name);
    Eq("UpgradeRef: met/total counts", (2, 2), (upGrp.UpgradeItems[0].Met, upGrp.UpgradeItems[0].Total));
    Eq("UpgradeRef: fingerprint identifies the concrete item",
        GearList.Fingerprint(upBag), upGrp.UpgradeItems[0].Fingerprint);
    Check("UpgradeRef: legacy display string preserved via ToString",
        upGrp.UpgradeItems[0].ToString().Contains("Bag Helm") && upGrp.UpgradeItems[0].ToString().Contains("(2/2)"));
}

// ---- v0.42: the 100% baseline — gate removal, max-roll targets, exceeds/threshold display data ----
{
    // (a) no explicit minimums anywhere => Under is ALWAYS 0 (imported builds carry no minimums)
    var nbTarget = new TargetBuild { Gear = { new TargetGear { Slot = "Helm", Affixes = {
        new TargetAffix { Name = "Maximum Life" }, new TargetAffix { Name = "Dexterity" } } } } };
    var nbLive = new LiveBuild { Gear = { new Item { Name = "H", Slot = "helm", Affixes = {
        new Affix { Text = "Maximum Life", Value = 1100, Min = 1000, Max = 1600 },     // a 17% roll
        new Affix { Text = "Dexterity", Value = 55, Min = 50, Max = 120 } } } } };     // a 7% roll
    var nbRep = DiffEngine.Diff(nbTarget, nbLive);
    Eq("Baseline100: low rolls with no build minimum are MET (Under == 0)", 0,
        nbRep.Categories.First(c => c.Id == "gear").Under);
    var nbRows = nbRep.Categories.First(c => c.Id == "gear").Groups[0].Items;
    Check("Baseline100: rows carry the max-roll display target", nbRows.All(i => i.NeedIsMax && i.Need!.StartsWith("max ")));
    Check("Baseline100: no threshold tick without an explicit minimum", nbRows.All(i => i.ThresholdPct == null));

    // (b) explicit minimums still gate: Min maps to a threshold tick % inside the roll range
    var exTarget2 = new TargetBuild { Gear = { new TargetGear { Slot = "Helm", Affixes = {
        new TargetAffix { Name = "Maximum Life", Min = 1300 } } } } };
    var exRep2 = DiffEngine.Diff(exTarget2, nbLive);
    var exRow = exRep2.Categories.First(c => c.Id == "gear").Groups[0].Items[0];
    Eq("Baseline100: below an explicit Min is still 'under'", "under", exRow.Status);
    Check("Baseline100: the explicit Min becomes the bar's tick (50% into [1000..1600])",
        exRow.ThresholdPct is double tp && Math.Abs(tp - 50) < 0.01);
    Check("Baseline100: under rows never claim NeedIsMax", !exRow.NeedIsMax);

    // (c) upgrade-finding roll-quality tiebreak: equal presence, strictly better rolls => still an upgrade
    var tbTarget = new TargetBuild { Gear = { new TargetGear { Slot = "Helm", Affixes = {
        new TargetAffix { Name = "Maximum Life" } } } } };
    var tbEq = new Item { Name = "Eq Helm", Slot = "helm", Equipped = true, Affixes = {
        new Affix { Text = "Maximum Life", Value = 1050, Min = 1000, Max = 1600 } } };   // 8% roll
    var tbBag = new Item { Name = "Bag Helm", Slot = "helm", Affixes = {
        new Affix { Text = "Maximum Life", Value = 1550, Min = 1000, Max = 1600 } } };   // 92% roll
    var tbRep = DiffEngine.Diff(tbTarget, new LiveBuild { Gear = { tbEq }, Inventory = { tbBag } });
    Check("Baseline100: a same-presence, better-rolled bag item still badges as an upgrade",
        tbRep.Categories.First(c => c.Id == "gear").Groups[0].UpgradeItems.Any(u => u.Name == "Bag Helm"));
    var tbWorse = new Item { Name = "Worse Helm", Slot = "helm", Affixes = {
        new Affix { Text = "Maximum Life", Value = 1010, Min = 1000, Max = 1600 } } };
    var tbRep2 = DiffEngine.Diff(tbTarget, new LiveBuild { Gear = { tbEq }, Inventory = { tbWorse } });
    Check("Baseline100: a same-presence, WORSE-rolled bag item does not badge",
        !tbRep2.Categories.First(c => c.Id == "gear").Groups[0].UpgradeItems.Any(u => u.Name == "Worse Helm"));

    // (d) AffixCeilings: harvested from any owned copy — range max preferred, else best value
    var ownedPool = new[] {
        new Item { Name = "A", Slot = "ring", Affixes = { new Affix { Text = "Dexterity", Value = 80, Min = 50, Max = 120 } } },
        new Item { Name = "B", Slot = "helm", Affixes = { new Affix { Text = "Maximum Life", Value = 900 } } } };
    Eq("AffixCeilings: range max wins", 120d, AffixCeilings.For("Dexterity", ownedPool));
    Eq("AffixCeilings: bare value when no range", 900d, AffixCeilings.For("Maximum Life", ownedPool));
    Eq("AffixCeilings: unknown affix -> 0", 0d, AffixCeilings.For("Armor", ownedPool));
    Check("AffixCeilings: umbrella copies count toward the specific affix",
        AffixCeilings.For("Dexterity", new[] { new Item { Name = "C", Slot = "ring",
            Affixes = { new Affix { Text = "All Stats", Value = 40, Min = 30, Max = 60 } } } }) == 60d);

    // (e) BuildGuide: a max-roll display target is a GOAL — GET steps never word it as a requirement
    var bgT = new TargetBuild { Gear = { new TargetGear { Slot = "Helm", Affixes = { new TargetAffix { Name = "Dexterity" } } } } };
    var bgRep2 = DiffEngine.Diff(bgT, new LiveBuild());
    var bgRow = bgRep2.Categories.First(c => c.Id == "gear").Groups[0].Items[0];
    bgRow.Need = "max 120"; bgRow.NeedIsMax = true;   // as the App ceiling annotation would set it
    var bgSteps2 = BuildGuide.Steps(bgRep2);
    var getStep = bgSteps2.First(s => s.Verb == "GET");
    Check("Baseline100: GET detail rewords a max target as 'rolls up to ...', never '(max ...)' in the text",
        getStep.Detail == "rolls up to 120" && !getStep.Text.Contains("(max"));
}

// ---- v0.43: ProfileStore.ResetAllLive — wipe captured loadouts, preserve identity, skip strays ----
{
    var tmpProf = Path.Combine(Path.GetTempPath(), "d4s_resetlive_" + Guid.NewGuid().ToString("N"));
    var store = new ProfileStore(tmpProf);
    store.Save(new CharacterProfile { Slug = "heoki-rogue", Name = "Heoki", Class = "Rogue", Paragon = 186,
        Torment = 8, TargetPath = @"C:\builds\dok.json",
        Live = new LiveBuild { Gear = { new Item { Name = "Cowl", Slot = "helm" } },
                               Inventory = { new Item { Name = "Spare", Slot = "ring" } } } });
    store.ActiveSlug = "heoki-rogue";
    // a stray non-profile JSON in the folder (tombstones.json shape) must be skipped, never re-saved
    File.WriteAllText(Path.Combine(tmpProf, "tombstones.json"), "{\"Stones\":[{\"Key\":\"x|helm\"}]}");
    try
    {
        int n = store.ResetAllLive();
        Eq("ResetAllLive: exactly the one real profile reset", 1, n);
        // All() must skip the tombstones.json stray too — else a phantom blank profile shows in the switcher
        Check("ProfileStore.All: skips the non-profile stray (no phantom blank profile)",
            store.All().Count == 1 && store.All()[0].Slug == "heoki-rogue");
        var p = store.Get("heoki-rogue")!;
        Eq("ResetAllLive: gear wiped", 0, p.Live.Gear.Count);
        Eq("ResetAllLive: inventory wiped", 0, p.Live.Inventory.Count);
        Check("ResetAllLive: identity + progression + target preserved",
            p.Name == "Heoki" && p.Class == "Rogue" && p.Paragon == 186 && p.Torment == 8 && p.TargetPath == @"C:\builds\dok.json");
        Eq("ResetAllLive: active pointer untouched", "heoki-rogue", store.ActiveSlug);
        Check("ResetAllLive: no unknown.json invented from the stray file",
            !File.Exists(Path.Combine(tmpProf, "unknown.json")));
        Check("ResetAllLive: the stray file itself is untouched",
            File.ReadAllText(Path.Combine(tmpProf, "tombstones.json")).Contains("Stones"));
    }
    finally { try { Directory.Delete(tmpProf, recursive: true); } catch { } }
}

// ---- v0.44: LogStore — rotation, retention, session index; BuildFromLines parity; archive prefeed ----
{
    var tmpLog = Path.Combine(Path.GetTempPath(), "d4s_logstore_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tmpLog);
    var active = Path.Combine(tmpLog, "d4_tts.log");
    string[] SessionLines(string iso, string name) => new[]
    {
        $"[{iso}]=== d4scanner tts shim attached v2 ===",
        $"[{iso}]Head", $"[{iso}]EQUIPPED", $"[{iso}]{name}", $"[{iso}]Legendary Helm", $"[{iso}]800 Item Power",
        $"[{iso}]+100 Dexterity [80 - 120]", $"[{iso}]Right mouse button", $"[{iso}]Unequip",
        $"[{iso}]=== d4scanner tts shim detached ===",
    };
    try
    {
        // rotation: active -> dated archive; the active file is gone until the shim recreates it
        File.WriteAllLines(active, SessionLines("2026-06-10T08:00:00Z", "OLD SESSION HELM"));
        var archived = LogStore.Rotate(active, new DateTime(2026, 6, 10, 9, 0, 0, DateTimeKind.Utc));
        Check("LogStore: rotation produces a dated archive", archived != null && archived.Contains("2026-06-10_1"));
        Check("LogStore: the active file is moved away", !File.Exists(active));
        Eq("LogStore: archives lists the rotated file", 1, LogStore.Archives(active).Count);
        Check("LogStore: rotating a missing active file is a no-op", LogStore.Rotate(active) == null);

        // a second rotation the same day de-collides with the counter suffix
        File.WriteAllLines(active, SessionLines("2026-06-10T10:00:00Z", "SECOND HELM"));
        var archived2 = LogStore.Rotate(active, new DateTime(2026, 6, 10, 11, 0, 0, DateTimeKind.Utc));
        Check("LogStore: same-day rotation de-collides", archived2 != null && archived2.Contains("2026-06-10_2"));

        // session index spans archives + active, oldest -> newest, with parsed [ISO] start times
        File.WriteAllLines(active, SessionLines("2026-06-12T09:00:00Z", "CURRENT HELM"));
        var sessions = LogStore.Sessions(active);
        Eq("LogStore: sessions found across archives + active", 3, sessions.Count);
        Check("LogStore: sessions are oldest -> newest",
            sessions[0].Start < sessions[1].Start && sessions[1].Start < sessions[2].Start);
        Check("LogStore: session start parsed from the marker's [ISO] prefix",
            sessions[2].Start == new DateTimeOffset(2026, 6, 12, 9, 0, 0, TimeSpan.Zero));

        // a session slice replays into the SAME loadout BuildFromFile would produce
        var sliceLines = LogStore.ReadSession(sessions[0]);
        var fromSlice = LogWatcher.BuildFromLines(sliceLines);
        Check("LogStore: a session slice replays its loadout", fromSlice.Gear.Any(g => g.RawName == "OLD SESSION HELM"));
        var parityFile = Path.Combine(tmpLog, "parity.log");
        File.WriteAllLines(parityFile, SessionLines("2026-06-11T08:00:00Z", "PARITY HELM"));
        var viaFile = LogWatcher.BuildFromFile(parityFile);
        var viaLines = LogWatcher.BuildFromLines(File.ReadAllLines(parityFile));
        Check("BuildFromLines == BuildFromFile (parity)",
            viaFile.Gear.Count == viaLines.Gear.Count && viaFile.Gear[0].RawName == viaLines.Gear[0].RawName);

        // retention: count cap prunes OLDEST first; age cap prunes by last-write
        var pruned = LogStore.Prune(active, maxFiles: 1, maxAgeDays: 3650);
        Eq("LogStore: count cap pruned the oldest archive", 1, pruned);
        Eq("LogStore: one archive remains", 1, LogStore.Archives(active).Count);

        // prefeed: a full replay materializes gear from ARCHIVES before tailing the active file
        var w = new LogWatcher(Path.Combine(tmpLog, "does_not_exist.log"), equippedOnly: true);
        w.Prefeed(LogStore.Archives(active));
        w.FeedChunk(File.ReadAllLines(active));
        // (each archived session ends detached and the next attach clears accumulated gear — the
        // prefeed VALUE is that every archived update streams through Updated -> profile persistence;
        // at the Core level the observable contract is the final ACTIVE session's gear:)
        Check("Prefeed: the active session's gear is present after prefeed + tail",
            w.Build.Gear.Any(g => g.RawName == "CURRENT HELM"));
    }
    finally { try { Directory.Delete(tmpLog, recursive: true); } catch { } }
}

// ---- report ----
Console.WriteLine($"D4Scanner.Core tests: {passed} passed, {failed} failed");
foreach (var f in failures) Console.WriteLine("  FAIL: " + f);
return failed == 0 ? 0 : 1;
