#!/usr/bin/env python3
"""
D4Scanner — Maxroll build importer (TARGET side).

Turns a Maxroll D4 build into a target.json (schema/target.schema.json) that the
tracker diffs against your live in-game capture. Pulls equipped gear + affixes,
uniques/mythics, legendary aspects, active skills, and paragon boards + glyphs.

Maxroll embeds the build in the page as `window.__remixContext` -> plannerProfile
-> data.profiles[]. IDs are resolved with Maxroll's own data.min.json plus the
Diablo4Companion affix name map (both cached locally on first run).

Usage:
    python d4_maxroll_import.py "https://maxroll.gg/d4/build-guides/bone-spear-necromancer-guide" --out target.json
    python d4_maxroll_import.py "https://maxroll.gg/d4/planner/abcd1234" --profile Endgame
    python d4_maxroll_import.py --file saved_guide.html --out target.json     # offline

Profiles: a build often has several (Leveling, Endgame, ...). Use --profile NAME
(substring) or --profile-index N. Default = the last profile (usually endgame).
"""

import argparse
import json
import os
import re
import sys
import urllib.error
import urllib.request

UA = {"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"}
DATA_URL = "https://assets-ng.maxroll.gg/d4-tools/game/data.min.json"
DC_AFFIX_URL = "https://raw.githubusercontent.com/josdemmers/Diablo4Companion/master/D4Companion/Data/Affixes.enUS.json"
CACHE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".cache")

TYPE_SLOT = [
    ("ChestArmor", "chest"), ("Chest", "chest"), ("Helm", "helm"), ("Gloves", "gloves"),
    ("Legs", "pants"), ("Pants", "pants"), ("Boots", "boots"), ("Amulet", "amulet"),
    ("Ring", "ring"), ("Focus", "offhand"), ("Shield", "offhand"), ("Totem", "offhand"),
    ("OffHand", "offhand"), ("Sword", "weapon"), ("Mace", "weapon"), ("Axe", "weapon"),
    ("Dagger", "weapon"), ("Bow", "weapon"), ("Crossbow", "weapon"), ("Wand", "weapon"),
    ("Staff", "weapon"), ("Polearm", "weapon"), ("Scythe", "weapon"), ("Glaive", "weapon"),
    ("Quarterstaff", "weapon"), ("Spear", "weapon"), ("Weapon", "weapon"),
]


def fetch(url, binary=False):
    data = urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=60).read()
    return data if binary else data.decode("utf-8", "replace")


def cached_json(url, name):
    os.makedirs(CACHE, exist_ok=True)
    path = os.path.join(CACHE, name)
    if not os.path.exists(path):
        print(f"[cache] downloading {name} ...", file=sys.stderr)
        open(path, "wb").write(fetch(url, binary=True))
    return json.load(open(path, encoding="utf-8"))


def extract_remix_object(html, varname="window.__remixContext"):
    i = html.find(varname)
    if i < 0:
        return None
    start = html.find("{", i)
    depth = 0; instr = False; esc = False
    for j in range(start, len(html)):
        c = html[j]
        if instr:
            if esc: esc = False
            elif c == "\\": esc = True
            elif c == '"': instr = False
        else:
            if c == '"': instr = True
            elif c == "{": depth += 1
            elif c == "}":
                depth -= 1
                if depth == 0:
                    return json.loads(html[start:j + 1])
    return None


def find_planner_profile(ctx):
    found = []
    def walk(o):
        if isinstance(o, dict):
            if isinstance(o.get("plannerProfile"), dict):
                found.append(o["plannerProfile"])
            for v in o.values():
                walk(v)
        elif isinstance(o, list):
            for v in o:
                walk(v)
    walk(ctx)
    return found[0] if found else None


def slot_for_type(t):
    for key, slot in TYPE_SLOT:
        if key.lower() in str(t).lower():
            return slot
    return None


def humanize(key):
    s = re.sub(r"_+", " ", str(key))
    s = re.sub(r"(?<=[a-z])(?=[A-Z])", " ", s)        # camelCase -> spaced
    s = re.sub(r"\b(Generic|Tier\d+|Greater|Single|Core ?Stat|Resource|S\d+)\b", " ", s)
    return re.sub(r"\s+", " ", s).strip()


def clean_affix(name):
    if not name:
        return None
    s = re.sub(r"\{[^}]*\}", "", name)
    s = re.sub(r"\[[^\]]*\]", "", s)
    s = re.sub(r"[#%+]|(?<![A-Za-z])x(?![A-Za-z])", " ", s)
    s = re.sub(r"\s+", " ", s).strip(" .,")
    # align common Maxroll wording to in-game tooltip wording
    s = re.sub(r"\bMax\b", "Maximum", s)
    return s or None


class Resolver:
    def __init__(self, dm, dc):
        self.dm = dm
        self.affix_key_by_id = {e["id"]: k for k, e in dm["affixes"].items()
                                if isinstance(e, dict) and "id" in e}
        self.tok = {}
        for d in dc:
            desc = d.get("Description")
            for t in (d.get("IdName") or "").split(";"):
                t = t.strip()
                if t and t not in self.tok:
                    self.tok[t] = desc

    def affix_name(self, nid):
        key = self.affix_key_by_id.get(nid)
        if not key:
            return None
        return clean_affix(self.tok.get(key)) or clean_affix(humanize(key))

    def item_def(self, item_id):
        return self.dm["items"].get(str(item_id), {})

    def glyph_name(self, gid):
        g = self.dm["paragonGlyphs"].get(str(gid))
        return g.get("name") if g else humanize(gid)

    def board_name(self, bid):
        b = self.dm["paragonBoards"].get(str(bid))
        return b.get("name") if b else humanize(bid)

    def skill_name(self, key):
        s = self.dm["skills"].get(str(key))
        return s.get("name") if s else humanize(key)


def build_target(pp, R, profile_name=None, profile_index=None):
    data = pp["data"]
    if isinstance(data, str):
        data = json.loads(data)
    profiles = data.get("profiles", [])
    if not profiles:
        raise SystemExit("no profiles in this Maxroll build")
    # choose profile
    idx = len(profiles) - 1
    if profile_index is not None:
        idx = profile_index
    elif profile_name:
        for k, p in enumerate(profiles):
            if profile_name.lower() in (p.get("name", "").lower()):
                idx = k; break
    prof = profiles[idx]
    items_db = data.get("items", {})

    klass = None
    skill_bar = prof.get("skillBar") or []
    if skill_bar:
        klass = str(skill_bar[0]).split("_")[0]

    gear, uniques, aspects = [], [], []
    ring_n = 0
    for slot_id, inst in (prof.get("items") or {}).items():
        item = items_db.get(str(inst)) or items_db.get(inst)
        if not item:
            continue
        idef = R.item_def(item.get("id"))
        name = idef.get("name") or str(item.get("id"))
        slot = slot_for_type(idef.get("type")) or slot_for_type(item.get("id")) or "unknown"
        iid = str(item.get("id", ""))
        is_unique = "Unique" in iid or idef.get("magicType") in (4, 5, 6)
        is_mythic = "Mythic" in iid or "UBER" in iid.upper()

        if is_unique:
            uniques.append({"name": name, "slot": slot, "mythic": bool(is_mythic)})
            continue
        # legendary aspect on the item (best effort)
        asp = item.get("aspect")
        if isinstance(asp, dict):
            an = R.affix_name(asp.get("nid") or asp.get("id"))
            if an:
                aspects.append(an)
        # normal affixes -> slot requirement
        names = []
        for af in (item.get("explicits") or []) + (item.get("tempered") or []):
            nm = R.affix_name(af.get("nid") or af.get("id"))
            if nm and nm not in names:
                names.append(nm)
        if names and slot != "unknown":
            label = slot.title()
            sid = slot
            if slot == "ring":
                ring_n += 1; sid = f"ring{ring_n}"; label = f"Ring #{ring_n}"
            gear.append({"slot": sid, "label": label, "affixes": names})

    skills = [{"name": R.skill_name(k), "rank": None} for k in skill_bar]

    boards, glyphs = [], []
    para = prof.get("paragon") or {}
    steps = para.get("steps") or []
    if steps:
        for b in steps[-1].get("data", []):
            boards.append(R.board_name(b.get("id")))
            g = b.get("glyph")
            if g:
                # Maxroll stores an internal glyphLevel (often a 150 sentinel = "maxed"),
                # not the in-game level. Only keep it as a target threshold if it's a
                # plausible in-game value; otherwise just require the glyph socketed.
                lvl = b.get("glyphLevel")
                lvl = lvl if (isinstance(lvl, (int, float)) and 0 < lvl <= 100) else None
                glyphs.append({"name": R.glyph_name(g), "level": lvl})

    return {
        "schemaVersion": 1,
        "name": pp.get("name") or "Maxroll Build",
        "class": klass,
        "source": pp.get("metadata", {}).get("maxrollId") or "maxroll",
        "gear": gear,
        "uniques": uniques,
        "aspects": sorted(set(aspects)),
        "skills": skills,
        "keyPassives": [],   # not auto-extracted from Maxroll skill tree (use override / live capture)
        "paragon": {"boards": boards, "glyphs": glyphs},
    }


def main(argv=None):
    ap = argparse.ArgumentParser(description="Import a Maxroll D4 build into a target.json")
    ap.add_argument("url", nargs="?", help="Maxroll build-guide or planner URL, or a planner id")
    ap.add_argument("--file", help="read a saved guide .html instead of fetching (offline)")
    ap.add_argument("--out", default="target.json")
    ap.add_argument("--profile", help="pick the build profile by name substring (e.g. Endgame)")
    ap.add_argument("--profile-index", type=int, dest="profile_index")
    ap.add_argument("--data", help="path to a cached data.min.json (else downloaded)")
    args = ap.parse_args(argv)

    if args.file:
        html = open(args.file, encoding="utf-8").read()
    elif args.url:
        url = args.url
        if "<" in url or ">" in url:
            raise SystemExit(
                "That URL contains '<' / '>' - it looks like the README placeholder, not a real build.\n"
                "Use your actual Maxroll build link, e.g.:\n"
                "  https://maxroll.gg/d4/build-guides/bone-spear-necromancer-guide\n"
                "Open your build on maxroll.gg and copy the address-bar URL.")
        if "://" not in url:
            url = f"https://maxroll.gg/d4/planner/{url}"
        print(f"[fetch] {url}", file=sys.stderr)
        try:
            html = fetch(url)
        except urllib.error.HTTPError as e:
            raise SystemExit(
                f"Maxroll returned HTTP {e.code} for that URL. Make sure it's a real build you can "
                f"open in a browser. Example:\n"
                f"  https://maxroll.gg/d4/build-guides/bone-spear-necromancer-guide")
        except urllib.error.URLError as e:
            raise SystemExit(f"Could not reach Maxroll ({e.reason}). Check your internet connection.")
    else:
        ap.error("provide a Maxroll URL/id or --file")

    ctx = extract_remix_object(html)
    pp = find_planner_profile(ctx) if ctx else None
    if not pp:
        raise SystemExit("Could not find planner data in that page. For a standalone planner "
                         "link, open its build-guide page instead, or save the page and use --file.")

    dm = (json.load(open(args.data, encoding="utf-8")) if args.data
          else cached_json(DATA_URL, "maxroll_data.min.json"))
    dc = cached_json(DC_AFFIX_URL, "d4companion_affixes.json")
    R = Resolver(dm, dc)

    target = build_target(pp, R, args.profile, args.profile_index)
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(target, f, indent=2, ensure_ascii=False)

    print(f"target -> {args.out}")
    print(f"  {target['name']} [{target['class']}]  "
          f"{len(target['gear'])} gear slots, {len(target['uniques'])} uniques, "
          f"{len(target['aspects'])} aspects, {len(target['skills'])} skills, "
          f"{len(target['paragon']['boards'])} boards, {len(target['paragon']['glyphs'])} glyphs")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
