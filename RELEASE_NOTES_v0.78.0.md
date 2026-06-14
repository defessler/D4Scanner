## D4Scanner v0.78.0

**Internal: the headless `--render` export is now strictly read-only.**

D4Scanner has a developer/diagnostic mode (`D4Scanner.exe --render out.png`) that
renders the window to an image without showing a UI — used to check layout and
catch clipping. It turned out this export could overwrite your saved `live.json`
loadout mirror: closing the off-screen window fired the same "save on exit" handler
the real app uses, persisting whatever throwaway state the render had loaded.

The render path now flags itself as read-only and never writes `live.json` or
settings on close. Your equipped loadout is unaffected either way — the authoritative
copy lives in your per-character profile — but the legacy mirror is no longer touched
by a render.

No action needed; this only affects the diagnostic `--render` mode.
