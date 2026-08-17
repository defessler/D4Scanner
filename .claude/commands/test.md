Run the D4Scanner Core test suite and report results.

```bash
cd /d/Projects/D4Scanner
dotnet run --project csharp/D4Scanner.Tests 2>&1
```

The suite is a dependency-free assertion console (no xUnit / NUnit). It exits non-zero on failure.
Report the pass/fail count and list any failing assertion names. The last line reads `D4Scanner.Core tests: N passed, M failed`. N should only go up. The last verified figure is recorded in the Test bullet of `CLAUDE.md`.

After adding new logic to Core, add matching assertions to `csharp/D4Scanner.Tests/Program.cs` using the `Check(name, bool)` / `Eq(name, expected, actual)` helpers already in that file.
