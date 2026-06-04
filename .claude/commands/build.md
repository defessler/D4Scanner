Build D4Scanner and report any errors or warnings.

```bash
cd /d/Projects/D4Scanner
dotnet build csharp/D4Scanner.App --no-incremental -c Release 2>&1
```

After building, summarise:
- Whether the build succeeded or failed
- Any errors (must be zero before shipping)
- Any warnings other than SYSLIB0014 (that one is expected — it's in the vendored CascLib third-party library and cannot be fixed)
