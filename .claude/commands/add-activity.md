Add a new recommended activity to D4Scanner's build-tailored guidance.

Activities are the collapsible "RECOMMENDED ACTIVITIES" accordion in the guidance
rail (`MainWindow.ActivitiesPanel`). They come from
`csharp/D4Scanner.Core/Activities.cs`:

```csharp
public static List<Activity> Recommend(DiffReport r, GuideContext? ctx = null)
```

`Activity` is `record Activity(string Title, string Detail)`. There is no tag.
`GuideContext` is `record GuideContext(int? Torment = null, string? Class = null)`.
Both live at the top of `Activities.cs`. The list is shown in order, so the most
impactful recommendations go first.

Two kinds of activity exist. Pick the one that matches:

- **Data-driven (the usual case).** The title and detail live in
  `csharp/D4Scanner.Core/Assets/season_pack.json` under `"activities"`, keyed by
  need (`uniques`, `aspects`, `affixes`, `temper`, `sockets`, `currency`,
  `masterwork`, `greaterAffixes`, `glyphs`, `warPlans`). `Recommend` calls the
  local `Add("key")`, which reads `SeasonPack.Current.Activity(key)`. Season
  copy changes are then a JSON edit, not a code change.
- **Code-built.** Only when the copy has to interpolate report values. The
  "Push to Torment N" and "Infernal Hordes — take X" entries do this with
  `acts.Add(new(title, detail))`.

Steps:

1. Open `Activities.cs`. `Recommend` computes booleans off `r.Categories`
   (`missAffix`, `needTemper`, `under`, `wantSockets`, `missGlyph`, plus the
   `MissingIn(id)` helper for the `uniques` and `aspects` categories). Reuse one
   of those, or add a new one in the same style, as the trigger.
2. Data-driven: add a `"myKey": { "title": "...", "detail": "..." }` object to
   the `activities` block of `season_pack.json`, then add
   `if (condition) Add("myKey");` at the right position in `Recommend`. An
   unknown key doesn't throw. `SeasonPack.Activity` returns a placeholder whose
   title is the key, so a typo shows up as a bare key in the UI, not a crash.
3. Code-built: `acts.Add(new($"...", $"..."));` guarded by the condition. Only
   gate on `ctx` fields with a null check (`ctx?.Torment is int t`), since the
   CLI and the tests call `Recommend(r)` without a context.
4. Watch the count-based rules downstream in the same method. `warPlans` fires
   at `acts.Count >= 3`, and the Hordes entry always appends last while
   `r.Pct < 100`.
5. Add assertions to `csharp/D4Scanner.Tests/Program.cs` under the
   `// ---- Activities: build-tailored recommendations from the gaps ----` banner
   (or the `Tier gate:` block later in the file for context-gated entries).
   Build a `TargetBuild` and `LiveBuild`, run `DiffEngine.Diff`, and `Check(...)`
   that `Activities.Recommend(rep).Any(a => a.Title.Contains("..."))`. Add the
   negative case too. `Eq("Activities: none when complete", 0, ...)` must keep
   passing.
6. Keep the copy current-season. The stale-term tripwire test serialises the
   whole `SeasonPack` plus every `Recommend` output at every Torment tier and
   fails on pre-2026 vocabulary (`Tormented Boss`, `Living Steel`, `Ingolith`,
   `Torment IV`, and the rest of the list in that test).
7. Run `/test`.

Reference: `SeasonPack.cs` for the `ActivityCopy` shape and the load order
(user override at `%LOCALAPPDATA%\d4scanner\season_pack.json`, then the embedded
resource, then a built-in fallback). `InfernalHordesAdvisor.cs` is the model for
a recommendation that reasons over specific gap types.
