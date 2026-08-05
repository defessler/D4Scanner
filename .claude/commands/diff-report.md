Generate a diff report comparing a target build against the current live gear, without launching the app.

```bash
cd /d/Projects/D4Scanner
dotnet run --project csharp/D4Scanner.Cli -- \
  --target "$LOCALAPPDATA/d4scanner/target.json" \
  --log "$LOCALAPPDATA/d4scanner/d4_tts.log"
```

Note: this used to read `%LOCALAPPDATA%`, which is cmd.exe syntax. The fence
says `bash`, and in Git Bash that expands to nothing, so the paths arrived
mangled. `$LOCALAPPDATA` is the form that works there.

Or for a one-shot report (no live watching):
```bash
dotnet run --project csharp/D4Scanner.Cli -- \
  --target <path/to/target.json>
```

The CLI output shows:
- Per-slot HAVE vs NEED with matched / missing / below-build-min affixes
- Overall completion percentage
- "Do Next" steps in impact order

Useful for:
- Verifying a parser fix produced the correct Item fields
- Checking that a new target.json imported correctly from Maxroll
- Debugging the diff engine without the full UI
