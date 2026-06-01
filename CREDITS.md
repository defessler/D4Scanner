# Credits & third-party assets

D4Scanner bundles the following open-licensed assets. Diablo® IV is a trademark of
Blizzard Entertainment; this is an unofficial fan tool and is not affiliated with Blizzard.

## Fonts
- **Cinzel** — © The Cinzel Project Authors, licensed under the
  [SIL Open Font License 1.1](https://openfontlicense.org/).
  Bundled at `csharp/D4Scanner.App/Assets/Fonts/Cinzel.ttf` and used for headers/item names.

## Icons
- Equipment slot icons from **[game-icons.net](https://game-icons.net)**, licensed
  [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/). Authors used:
  - Lorc — visored-helm, breastplate, leather-boot, gem-pendant, crossed-swords
  - Delapouite — gloves, leg-armor, ring
  - Willdabeast — round-shield

  The SVG path data is embedded as geometries in `csharp/D4Scanner.App/Icons.cs`.

## Data
- Build data is imported from user-provided **[Maxroll.gg](https://maxroll.gg)** build URLs;
  the build-guide list for autocomplete is read from Maxroll's public sitemap.
- Affix names are resolved with help from
  [Diablo4Companion](https://github.com/josdemmers/Diablo4Companion) data.
