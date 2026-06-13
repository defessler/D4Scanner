using D4Scanner.Core;

// Verify/report CLI:  D4Scanner.Cli (--target t.json | --maxroll URL [--profile X]) [--log L] [--all] [--watch] [--save out.json]
string log = TargetLoader.DefaultLogPath();
string? targetPath = null, maxroll = null, profile = null, save = null, livePath = null;
bool equippedOnly = true, watch = false;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--log" when i + 1 < args.Length: log = args[++i]; break;
        case "--live" when i + 1 < args.Length: livePath = args[++i]; break;   // diff a persisted live.json directly (validation/debug)
        case "--target" when i + 1 < args.Length: targetPath = args[++i]; break;
        case "--maxroll" when i + 1 < args.Length: maxroll = args[++i]; break;
        case "--profile" when i + 1 < args.Length: profile = args[++i]; break;
        case "--save" when i + 1 < args.Length: save = args[++i]; break;
        case "--all": equippedOnly = false; break;
        case "--watch": watch = true; break;
    }
}
if (targetPath == null && maxroll == null)
{
    Console.Error.WriteLine("usage: (--target <t.json> | --maxroll <URL> [--profile X]) [--log <L>] [--all] [--watch] [--save out.json]");
    return 2;
}

TargetBuild target;
if (maxroll != null)
{
    target = await MaxrollImporter.ImportAsync(maxroll, profile, s => Console.Error.WriteLine("  " + s));
    if (save != null)
    {
        await File.WriteAllTextAsync(save, System.Text.Json.JsonSerializer.Serialize(target, D4Scanner.Core.Json.Opts));
        Console.Error.WriteLine($"  saved target -> {save}");
    }
}
else target = TargetLoader.Load(targetPath!);

void Print(LiveBuild live)
{
    var r = DiffEngine.Diff(target, live);
    if (watch) Console.Clear();
    Console.WriteLine($"\n{r.TargetName}{(r.TargetClass != null ? "  [" + r.TargetClass + "]" : "")}");
    Console.WriteLine($"{r.Matched} / {r.Total} met  ({r.Pct}%)   |   {live.Gear.Count} equipped items   |   {r.Under} below build min\n");
    foreach (var c in r.Categories)
    {
        Console.WriteLine($"{c.Name}  —  {c.Matched}/{c.Total} ({c.Pct}%)" + (c.Under > 0 ? $"   [{c.Under} below build min]" : ""));
        foreach (var g in c.Groups)
        {
            var head = g.Kind == "gear" && g.LiveItems.Count > 0 ? "   your: " + g.LiveItems[0].Name : "";
            Console.WriteLine($"  {g.Name} ({g.Matched}/{g.Total}){head}");
            foreach (var it in g.Items)
            {
                string mark = it.Status == "met" ? "[x]" : it.Status == "under" ? "[~]" : "[ ]";
                string val = "";
                if (g.Kind == "gear")
                {
                    if (it.Done)
                    {
                        val = "  " + (it.Val ?? "ok");
                        if (it.RollPct != null)
                            val += $"  ({it.RollPct:0}% roll" + (it.Status == "under" && it.Need != null ? ", " + it.Need : "") + ")";
                    }
                    else val = "  -";
                }
                var have = it.Have != null ? "   have: " + it.Have : "";
                var note = it.RollNote != null ? "   (" + it.RollNote + ")" : "";
                Console.WriteLine($"    {mark} {it.Label}{val}{have}{note}");
            }
            if (g.Extras.Count > 0) Console.WriteLine("      also: " + string.Join(", ", g.Extras.Take(5)));
        }
        Console.WriteLine();
    }

    // the same prioritized "Do Next" plan the app shows, straight from Core
    var steps = BuildGuide.Steps(r);
    Console.WriteLine("DO NEXT:");
    if (steps.Count == 0) Console.WriteLine("  ✓ build complete");
    else
    {
        int? lt = null;
        foreach (var s in steps.Take(12))
        {
            if (s.Tier != lt) { Console.WriteLine("  " + BuildGuide.TierLabel(s.Tier)); lt = s.Tier; }
            Console.WriteLine($"    [{s.Verb,-7}] {s.Text}" + (s.Detail != null ? $"   ({s.Detail})" : ""));
        }
        if (steps.Count > 12) Console.WriteLine($"    … +{steps.Count - 12} more");
    }
    Console.WriteLine();

    // flexibility / best-owned substitutes per slot
    var subs = Substitutes.Plan(target, live).Where(s => s.Affixes.Count > 0).ToList();
    if (subs.Count > 0)
    {
        Console.WriteLine("FLEXIBILITY / SUBSTITUTES:");
        foreach (var s in subs)
        {
            var core = s.Affixes.Where(a => a.Core).Select(a => a.Name);
            var flex = s.Affixes.Where(a => !a.Core).Select(a => a.Name);
            Console.WriteLine($"  {s.Slot}:  core {s.CoreMet}/{s.CoreTotal}"
                + (s.BestOwned != null ? $"   best owned: {s.BestOwned}{(s.BestIsUpgrade ? "  (UPGRADE)" : "")}" : ""));
            Console.WriteLine($"     core:     {string.Join(", ", core)}");
            if (flex.Any()) Console.WriteLine($"     flexible: {string.Join(", ", flex)}");
        }
        Console.WriteLine();
    }

    // build-specific activities + crafting recommendations
    var activities = Activities.Recommend(r);
    if (activities.Count > 0)
    {
        Console.WriteLine("RECOMMENDED ACTIVITIES:");
        foreach (var a in activities) Console.WriteLine($"  • {a.Title}\n      {a.Detail}");
        Console.WriteLine();
    }
}

if (watch)
{
    using var w = new LogWatcher(log, equippedOnly);
    w.Updated += b => Print(b);
    w.Start();
    Print(w.Build);
    Console.WriteLine($"watching {log}  (Ctrl+C to stop)…");
    Thread.Sleep(Timeout.Infinite);
}
else if (livePath != null)
{
    var live = System.Text.Json.JsonSerializer.Deserialize<LiveBuild>(File.ReadAllText(livePath), D4Scanner.Core.Json.Opts) ?? new LiveBuild();
    Print(live);
}
else
{
    Print(LogWatcher.BuildFromFile(log, equippedOnly));
}
return 0;
