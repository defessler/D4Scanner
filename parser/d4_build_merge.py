#!/usr/bin/env python3
"""
D4Scanner — merge capture channels into one unified live-build JSON.

Combines the gear channel (TTS parser output, has `items`/`gear`) with the
vision channel (skills / paragon / aspects) into a single build.json that
matches schema/build.schema.json and that the tracker diffs against a target.

Usage:
    python d4_build_merge.py --gear gear.json --vision vision.json --out build.json
    python d4_build_merge.py --gear gear.json                 # gear only
    python d4_build_merge.py --vision vision.json             # vision only
"""

import argparse
import datetime as _dt
import json
import os
import sys


def load(path):
    if not path:
        return {}
    if not os.path.exists(path):
        print(f"warning: {path} not found, skipping", file=sys.stderr)
        return {}
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def main(argv=None):
    ap = argparse.ArgumentParser(description="Merge D4Scanner capture channels")
    ap.add_argument("--gear", help="gear channel JSON (TTS parser output)")
    ap.add_argument("--vision", help="vision channel JSON (skills/paragon/aspects)")
    ap.add_argument("--out", default="build.json")
    ap.add_argument("--name", help="character name")
    ap.add_argument("--klass", "--class", dest="klass", help="character class")
    ap.add_argument("--level", type=int, help="character level")
    args = ap.parse_args(argv)

    gear_doc = load(args.gear)
    vision_doc = load(args.vision)

    gear = gear_doc.get("gear") or gear_doc.get("items") or []
    sources = {}
    if gear:
        sources["gear"] = gear_doc.get("source", "tts")

    build = {
        "schemaVersion": 1,
        "capturedAt": _dt.datetime.now().isoformat(timespec="seconds"),
        "character": {"name": args.name, "class": args.klass, "level": args.level},
        "gear": gear,
        "aspects": vision_doc.get("aspects", []),
        "skills": vision_doc.get("skills", []),
        "paragon": vision_doc.get("paragon", []),
        "sources": sources,
    }
    for key in ("aspects", "skills", "paragon"):
        if build[key]:
            sources[key] = vision_doc.get("source", "vision")

    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(build, f, indent=2, ensure_ascii=False)

    print(f"merged -> {args.out}  "
          f"({len(build['gear'])} gear, {len(build['skills'])} skills, "
          f"{len(build['paragon'])} boards, {len(build['aspects'])} aspects)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
