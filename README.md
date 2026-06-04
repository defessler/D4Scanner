# D4Scanner

A live build tracker for **Diablo IV**. It shows your equipped gear against a target build as a
Diablo-IV-style paper doll, plus a prioritized **"do this next"** plan — what to equip, find, craft
and improve to reach the build. PC only; it reads the game's own accessibility (screen-reader) text,
so it touches no game memory and injects nothing.

<img width="1429" height="1214" alt="image" src="https://github.com/user-attachments/assets/cbb2bca1-eea9-49c5-a081-d02e55d8637d" />


## Quick start

1. **Download & run** the latest `D4Scanner.exe` from the
   [Releases page](https://github.com/defessler/D4Scanner/releases). It's one self-contained file —
   no install, no .NET to set up.
2. **Import a build** — paste a Maxroll build-guide or planner URL into the box (or type a build
   name to search) and click **Import**.
3. **Turn on capture** — click **Install capture DLL**. The app copies its built-in capture file
   into your Diablo IV folder for you and trusts its certificate — there's nothing extra to
   download. Then, in **Diablo IV → Settings**:
   - **Accessibility** → *Use Screen Reader* = **On**, *Use 3rd-Party Screen Reader* = **On**
   - **Gameplay** → *Advanced Tooltip Information* = **On**
   - Game Language = **English**
4. **Play** — hover your equipped items in game. The doll fills in and updates live.
5. **(Optional) Skills & paragon** — click **Paragon / Skills** and pick screenshots of your paragon
   boards / glyphs / skill tree to track those too (needs an `ANTHROPIC_API_KEY` environment variable).

## Using the app

- **Paper doll** — your slots, like the in-game character screen. Toggle **My gear** (what you have)
  vs **Target** (what the build wants). **Hover** a slot to compare it; **click** to pin the compare.
- **DO NEXT** — the single highest-impact action first, then the rest of the plan. Click a step to
  focus that slot. **Compare all gaps** pins everything that still needs work.
- **Next Steps** / **Build details** — the full searchable plan, and the complete target build.
- **Builds** switches between imported builds; **Open on Maxroll** opens the source build.
- Press **`?`** for the keyboard shortcuts.

## If "Install capture DLL" doesn't work

The button always extracts the capture file to `%LOCALAPPDATA%\d4scanner\bin\` first, then copies it
into your Diablo IV folder. If the doll still isn't filling in when you hover gear:

1. **Fully quit Diablo IV** (while it's running it locks the file), then click **Install capture DLL**
   again.
2. Still nothing? **Copy it in by hand** — paste `%LOCALAPPDATA%\d4scanner\bin\` into Explorer's
   address bar, then:
   - Copy **`saapi64.dll`** from there into your **Diablo IV install folder** (the folder that
     contains `Diablo IV.exe`).
   - If Windows blocked the certificate, double-click **`d4scanner-tts.cer`** in that folder →
     **Install Certificate** → *Current User* → **Trusted Root Certification Authorities**.
3. Double-check the three in-game **Accessibility / Gameplay** settings above, relaunch Diablo IV,
   and hover an equipped item.

**To remove it later:** delete `saapi64.dll` from your Diablo IV folder and from
`%LOCALAPPDATA%\d4scanner\bin\`.

---

### More docs

- [`docs/architecture.md`](docs/architecture.md) — how it fits together (capture channels, data flow, project layout, schemas)
- [`docs/capture.md`](docs/capture.md) — the TTS capture mechanism, install internals, routes, risk, per-season maintenance
- [`docs/offline-pipeline.md`](docs/offline-pipeline.md) — the legacy Python/HTML offline flow
- [`csharp/README.md`](csharp/README.md) — building from source, the app's features, and the test suite

