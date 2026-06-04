Build, test, bump version, commit, tag, and push a new release of D4Scanner.

Steps:
1. Run `/build` — must be 0 errors before continuing.
2. Run `/test` — all assertions must pass before continuing.
3. Ask the user which version to ship (patch / minor / major bump from the current `<Version>` in `csharp/D4Scanner.App/D4Scanner.App.csproj`).
4. Bump `<Version>` in that file to the new version.
5. Commit everything with a message summarising what changed since the last tag.
6. Push `main`.
7. Tag `vX.Y.Z` and push the tag — GitHub Actions CI builds the self-contained exe and creates the GitHub release automatically.
8. Wait for CI to succeed, then edit the release notes via `gh release edit vX.Y.Z --notes "..."`.

Do NOT push the tag if step 1 or 2 failed. Do NOT skip writing release notes.
