Add a new recommended activity to D4Scanner's build-tailored guidance system.

Activities appear in the collapsible "Recommended Activities" section of the app.
They are computed in `csharp/D4Scanner.Core/Activities.cs` → `Recommend(DiffReport r, string? targetClass)`.

Each activity is returned as a `(string Title, string Detail, string? Tag)` tuple. Activities are shown in order, so put the most impactful ones first.

Steps:
1. Open `csharp/D4Scanner.Core/Activities.cs`.
2. Find the `Recommend` method. It builds a `List<(string, string, string?)>` called `steps`.
3. Add a new `if (condition) steps.Add(...)` block. The condition should check something from the `DiffReport r` (e.g. `r.Pct < 100`, missing uniques, below-build-min affixes, open sockets).
4. The `Title` is the short heading (e.g. "Run Helltide"). The `Detail` is 1–2 sentences of concrete guidance. The `Tag` is optional (e.g. `"crafting"`, `"farming"`).
5. Add a test assertion in `csharp/D4Scanner.Tests/Program.cs` — construct a `DiffReport` that should trigger the activity and `Check(...)` that it appears in the result.
6. Run `/test` to confirm it passes.

Reference: the existing Helltide, Pit, Tormented Boss, Undercity, and Infernal Hordes blocks are good models. See also `InfernalHordesAdvisor.cs` for heuristics based on specific gap types.
