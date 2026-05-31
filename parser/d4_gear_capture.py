#!/usr/bin/env python3
"""
D4Scanner — gear capture channel (TTS).

Tails the log file written by the saapi64.dll shim (the text Diablo IV's screen
reader tries to "speak") and turns each hovered item's tooltip into structured
JSON. This is the GEAR channel only: equipped items + affixes + item power +
tempering count + masterworking rank + aspects + unique/mythic flags.

It does NOT capture paragon boards, glyph levels, skill ranks, or key passives —
D4's screen reader can't enumerate those. Capture those with the vision-LLM
channel (separate) or planner import.

Usage:
    # Test the parser on the bundled sample (no game / DLL needed):
    python d4_gear_capture.py --log ../samples/sample_tts.log --once --verbose

    # Live capture while playing (default = follow the real log):
    python d4_gear_capture.py --follow

Default log path:  %LOCALAPPDATA%\\d4scanner\\d4_tts.log   (override with --log)
Default output:    ./live_build.json                       (override with --out)

NOTE on greater affixes: TTS does not label them. We flag an affix as
"isGreater" heuristically when it has NO [min - max] range. This is BEST-EFFORT
and noisy (an item's inherent armor/DPS-less line can look the same). Verify
visually if a GA flag matters. Tempering: TTS gives the COUNT ("Tempers: n/n"),
not which affixes are tempered. Masterwork rank IS voiced and we parse it.
"""

import argparse
import datetime as _dt
import html
import json
import os
import re
import sys
import time

END_MARKERS = ("mouse button", "action button")

# Longest / most-specific first so "Two-Handed Sword" wins over "Sword".
TYPE_SLOT = [
    ("Chest Armor", "chest"),
    ("Two-Handed Sword", "weapon"), ("Two-Handed Mace", "weapon"), ("Two-Handed Axe", "weapon"),
    ("Helm", "helm"), ("Gloves", "gloves"), ("Pants", "pants"), ("Boots", "boots"),
    ("Amulet", "amulet"), ("Ring", "ring"),
    ("Sword", "weapon"), ("Mace", "weapon"), ("Axe", "weapon"), ("Dagger", "weapon"),
    ("Bow", "weapon"), ("Crossbow", "weapon"), ("Wand", "weapon"), ("Staff", "weapon"),
    ("Polearm", "weapon"), ("Scythe", "weapon"), ("Glaive", "weapon"),
    ("Quarterstaff", "weapon"), ("Spear", "weapon"),
    ("Focus", "offhand"), ("Shield", "offhand"), ("Totem", "offhand"),
]
RARITIES = ["Mythic Unique", "Mythic", "Unique", "Legendary", "Rare", "Magic", "Common"]

RE_ITEM_POWER = re.compile(r'([\d,]+)\s+Item Power', re.I)
RE_DPS        = re.compile(r'([\d,]+(?:\.\d+)?)\s+Damage Per Second', re.I)
RE_MASTERWORK = re.compile(r'Masterwork[:\s]+(\d+)\s*/\s*(\d+)', re.I)
RE_TEMPER     = re.compile(r'Tempers?[:\s]+(\d+)\s*/\s*(\d+)', re.I)
RE_REQLEVEL   = re.compile(r'Requires Level\s+(\d+)', re.I)
RE_BRACKET    = re.compile(r'\[\s*([\d,.]+)\s*%?\s*(?:-\s*([\d,.]+)\s*%?\s*)?\]')
RE_AFFIX      = re.compile(r'^\s*([+x]?)\s*([\d,.]+)\s*(%?)\s+(.+?)\s*$')


def now_iso():
    return _dt.datetime.now().isoformat(timespec="seconds")


def clean(s):
    # D4 emits HTML entities (&apos; &quot; &lt; &gt; &amp; ...); decode them all.
    s = html.unescape(s)
    s = s.replace("’", "'").replace("‘", "'").replace("“", '"').replace("”", '"')
    s = re.sub(r"\s+", " ", s).strip()
    return s


def looks_like_item(it):
    """A captured block is a real item only if it has an item-power line, or a
    rarity/type line plus at least one affix. Rejects menu/map/UI noise — D4
    voices many ALL-CAPS headers ('GRAPHICS', region names) that are NOT items."""
    if it is None:
        return False
    if it.get("itemPower") is not None:
        return True
    return bool(it.get("rarity") and it.get("affixes"))


def to_num(x):
    if x is None:
        return None
    x = x.replace(",", "")
    try:
        f = float(x)
        return int(f) if f.is_integer() else f
    except ValueError:
        return None


_NAME_MARKER = re.compile(r"^\s*(EQUIPPED|\[FAVORITED ITEM\]\.?|\[.*?\]\.?)\s*", re.I)


def strip_name_markers(s):
    """D4 prefixes item names with 'EQUIPPED' / '[FAVORITED ITEM].' etc."""
    prev = None
    while prev != s:
        prev = s
        s = _NAME_MARKER.sub("", s).strip(" .")
    return s


def name_candidate(s):
    """Return the cleaned ALL-CAPS item name if this line starts an item, else None."""
    s = strip_name_markers(s)
    letters = [c for c in s if c.isalpha()]
    if len(letters) < 2 or len(s) > 64:
        return None
    if not all(c.isupper() for c in letters):
        return None
    return s


def display_name(raw):
    """'UNDYING ADVENTURER'S BOOTS' -> 'Undying Adventurer's Boots'."""
    return re.sub(r"'(\w)", lambda m: "'" + m.group(1).lower(), raw.title())


def detect_rarity_type(ln, item):
    """A line is the rarity/type line only if it names an item TYPE (robust gate)."""
    type_hit = None
    for t, slot in TYPE_SLOT:
        if re.search(r"\b" + re.escape(t) + r"\b", ln, re.I):
            type_hit = (t, slot)
            break
    if not type_hit:
        return False
    item["itemType"], item["slot"] = type_hit
    for r in RARITIES:
        if re.search(r"\b" + re.escape(r) + r"\b", ln, re.I):
            item["rarity"] = r
            if r.lower().startswith("mythic"):
                item["isMythic"] = True
                item["isUnique"] = True
            elif r.lower() == "unique":
                item["isUnique"] = True
            break
    if re.search(r"\bAncestral\b", ln, re.I):
        item["isAncestral"] = True
    return True


def parse_affix(ln):
    rng = RE_BRACKET.search(ln)
    vmin = vmax = None
    core = ln
    if rng:
        vmin = to_num(rng.group(1))
        vmax = to_num(rng.group(2)) if rng.group(2) else None
        core = ln[: rng.start()].strip()
    m = RE_AFFIX.match(core)
    if not m:
        return None
    sign, num, pct, text = m.group(1), m.group(2), m.group(3), m.group(4).strip()
    value = to_num(num)
    if value is None or not text or text.lower() == "item power":
        return None
    return {
        "text": text,
        "value": value,
        "min": vmin,
        "max": vmax,
        "isPercent": bool(pct),
        "isMultiplier": sign == "x",
        "isGreater": rng is None,  # BEST-EFFORT heuristic — verify visually
    }


def parse_block(name, body):
    item = {
        "name": display_name(name), "rawName": name,
        "rarity": None, "itemType": None, "slot": None,
        "isUnique": False, "isMythic": False, "isAncestral": False,
        "itemPower": None, "dps": None,
        "masterworkRank": None, "masterworkMax": None,
        "temperUsed": None, "temperMax": None,
        "requiresLevel": None, "aspect": None, "equipped": False,
        "affixes": [], "powerText": [],
        "rawLines": [name] + body,
    }
    for ln in body:
        m = RE_ITEM_POWER.search(ln)
        if m and item["itemPower"] is None and "per second" not in ln.lower():
            item["itemPower"] = to_num(m.group(1)); continue
        m = RE_DPS.search(ln)
        if m and item["dps"] is None:
            item["dps"] = to_num(m.group(1)); continue
        m = RE_MASTERWORK.search(ln)
        if m:
            item["masterworkRank"] = int(m.group(1)); item["masterworkMax"] = int(m.group(2)); continue
        m = RE_TEMPER.search(ln)
        if m:
            item["temperUsed"] = int(m.group(1)); item["temperMax"] = int(m.group(2)); continue
        m = RE_REQLEVEL.search(ln)
        if m:
            item["requiresLevel"] = int(m.group(1)); continue
        if item["rarity"] is None and detect_rarity_type(ln, item):
            continue
        # legendary aspect is voiced as "Imprinted: <effect>"
        mi = re.match(r"Imprinted:\s*(.+)", ln, re.I)
        if mi:
            item["aspect"] = mi.group(1).strip()
            item["powerText"].append(ln); continue
        af = parse_affix(ln)
        if af:
            item["affixes"].append(af); continue
        if any(c.islower() for c in ln) and len(ln) > 8:
            item["powerText"].append(ln)  # aspect / unique power text
    return item


class Segmenter:
    """Feed raw lines; yields a parsed item dict when a tooltip block completes."""

    def __init__(self):
        self.name = None
        self.body = []
        self._equip = False        # an 'EQUIPPED' line was just seen
        self._block_equip = False  # ...and it belongs to the current block

    def _start(self, nc):
        self.name, self.body = nc, []
        self._block_equip = self._equip
        self._equip = False

    def feed(self, raw):
        ln = clean(raw)
        if not ln:
            return None
        if ln.upper() == "EQUIPPED":   # precedes an equipped item's name
            self._equip = True
            return None
        low = ln.lower()
        nc = name_candidate(ln)
        if self.name is None:
            if nc:
                self._start(nc)
            return None
        if any(mk in low for mk in END_MARKERS):
            item = parse_block(self.name, self.body)
            if item is not None:
                item["equipped"] = bool(self._block_equip)
            self.name, self.body, self._block_equip = None, [], False
            return item if looks_like_item(item) else None  # drop menu/map noise
        if nc:  # new item started before previous ended -> restart
            self._start(nc)
            return None
        self.body.append(ln)
        if len(self.body) > 60:  # runaway guard
            self.name, self.body = None, []
        return None


def write_build(items_by_key, out_path):
    build = {"source": "tts", "capturedAt": now_iso(), "items": list(items_by_key.values())}
    tmp = out_path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(build, f, indent=2, ensure_ascii=False)
    os.replace(tmp, out_path)


def summarize(item):
    aff = ", ".join(a["text"] for a in item["affixes"][:6])
    mw = (f"{item['masterworkRank']}/{item['masterworkMax']}"
          if item["masterworkRank"] is not None else "-")
    tm = (f"{item['temperUsed']}/{item['temperMax']}"
          if item["temperUsed"] is not None else "-")
    return (f"[{item['slot'] or '?'}] {item['name']} "
            f"({item['rarity'] or '?'} {item['itemType'] or ''}) "
            f"iPwr {item['itemPower']}  MW {mw}  Temper {tm}  :: {aff}")


def run(lines_iter, out_path, verbose, equipped_only=False):
    seg = Segmenter()
    items = {}
    count = 0
    for raw in lines_iter:
        item = seg.feed(raw)
        if item is None:
            continue
        if equipped_only and not item.get("equipped"):
            continue  # skip inventory/stash items you hovered; keep only worn gear
        # key by slot + name so re-hovering the same item updates it, but two
        # different items in the same slot (e.g. both rings) are kept separately.
        key = (item["slot"] or "?") + ":" + item["rawName"]
        items[key] = item
        count += 1
        print(("[E] " if item.get("equipped") else "    ") + summarize(item))
        if verbose:
            print(json.dumps(item, indent=2, ensure_ascii=False))
        write_build(items, out_path)
    return count, items


def follow(path, poll=0.25):
    """Generator yielding lines as they are appended; tolerant of truncation."""
    pos = 0
    buf = ""
    print(f"watching {path} (Ctrl+C to stop)…")
    while True:
        try:
            size = os.path.getsize(path)
        except OSError:
            time.sleep(poll); continue
        if size < pos:  # log was truncated/rotated
            pos, buf = 0, ""
        if size > pos:
            with open(path, "rb") as f:
                f.seek(pos)
                chunk = f.read()
                pos = f.tell()
            buf += chunk.decode("utf-8", errors="replace")
            *full, buf = buf.split("\n")
            for ln in full:
                yield ln
        time.sleep(poll)


def default_log():
    base = os.environ.get("LOCALAPPDATA", os.getcwd())
    return os.path.join(base, "d4scanner", "d4_tts.log")


def main(argv=None):
    ap = argparse.ArgumentParser(description="D4Scanner TTS gear capture")
    ap.add_argument("--log", default=default_log(), help="path to the TTS log file")
    ap.add_argument("--out", default=os.path.join(os.getcwd(), "live_build.json"),
                    help="output JSON path")
    ap.add_argument("--once", action="store_true",
                    help="parse the existing log once and exit (default is --follow)")
    ap.add_argument("--follow", action="store_true", help="tail the log live (default)")
    ap.add_argument("--verbose", action="store_true", help="print full JSON per item")
    ap.add_argument("--equipped-only", action="store_true", dest="equipped_only",
                    help="keep only EQUIPPED items (skip inventory/stash you hovered) — use this for build capture")
    args = ap.parse_args(argv)

    if args.once:
        if not os.path.exists(args.log):
            print(f"log not found: {args.log}", file=sys.stderr); return 2
        with open(args.log, "r", encoding="utf-8", errors="replace") as f:
            n, _ = run(f, args.out, args.verbose, args.equipped_only)
        print(f"\nparsed {n} item(s) -> {args.out}")
        return 0

    try:
        run(follow(args.log), args.out, args.verbose, args.equipped_only)
    except KeyboardInterrupt:
        print("\nstopped.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
