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

// ---- report ----
Console.WriteLine($"D4Scanner.Core tests: {passed} passed, {failed} failed");
foreach (var f in failures) Console.WriteLine("  FAIL: " + f);
return failed == 0 ? 0 : 1;
