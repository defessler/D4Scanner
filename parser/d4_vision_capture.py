#!/usr/bin/env python3
"""
D4Scanner — vision capture channel.

Captures the parts of a build that the TTS channel CANNOT enumerate — paragon
boards, glyph levels, active skills + ranks, key passives, and aspect names —
by sending screenshots of those in-game screens to a vision-capable Claude model
and asking for structured JSON.

Two modes:
  * --stub FIXTURE   : skip the API; just read/validate a fixture JSON and emit it.
                       Lets the whole pipeline run with no API key (used by tests).
  * (default)        : call the Anthropic API on the given --images. Requires
                       ANTHROPIC_API_KEY. Uses stdlib only (no SDK needed).

Output JSON shape (the vision half of build.schema.json):
  { "source": "vision", "aspects": [...], "skills": [...], "paragon": [...] }

Examples:
  # No key / offline — emit the bundled fixture:
  python d4_vision_capture.py --stub ../samples/sample_vision.json --out vision.json

  # Real capture (needs ANTHROPIC_API_KEY):
  python d4_vision_capture.py --images board1.png board2.png skills.png --out vision.json

What to screenshot: each equipped Paragon board (zoom so node text is legible),
a close-up of each socketed glyph (to read its LEVEL), the skill-assignment
screen (skills + ranks + key passive), and the Aspects/Codex if you want aspect
names. Multiple images per board are fine — send them all.
"""

import argparse
import base64
import json
import os
import sys

MODEL_DEFAULT = "claude-opus-4-8"
API_URL = "https://api.anthropic.com/v1/messages"

# JSON Schema the model must fill (the vision half of a build).
TOOL = {
    "name": "emit_build_parts",
    "description": "Return the paragon boards, glyphs, skills, key passives, and aspect names visible in the screenshots.",
    "input_schema": {
        "type": "object",
        "properties": {
            "aspects": {
                "type": "array", "items": {"type": "string"},
                "description": "Legendary aspect names visible (from item tooltips or the Codex)."
            },
            "skills": {
                "type": "array",
                "items": {
                    "type": "object",
                    "properties": {
                        "name": {"type": "string"},
                        "rank": {"type": "integer", "description": "skill rank, e.g. 5 for 5/5; 1 if unknown"},
                        "isKeyPassive": {"type": "boolean"},
                        "slotted": {"type": "boolean", "description": "true if on the action bar"}
                    },
                    "required": ["name"]
                }
            },
            "paragon": {
                "type": "array",
                "items": {
                    "type": "object",
                    "properties": {
                        "board": {"type": "string"},
                        "glyph": {"type": ["string", "null"], "description": "socketed glyph name"},
                        "glyphLevel": {"type": ["integer", "null"], "description": "the glyph's level (only visible by hovering the glyph)"},
                        "notables": {"type": "array", "items": {"type": "string"}}
                    },
                    "required": ["board"]
                }
            }
        },
        "required": ["skills", "paragon"]
    }
}

SYSTEM_PROMPT = (
    "You read Diablo IV UI screenshots and extract build data precisely. "
    "Only report what is visible. For each paragon board give its name, the socketed glyph, "
    "and the glyph's numeric LEVEL if shown. For skills give the name, rank (the n in n/n), "
    "whether it is a key passive, and whether it is slotted on the action bar. "
    "Do not invent values; if a number is not legible, omit it. Call emit_build_parts exactly once."
)


def _media_type(path):
    ext = os.path.splitext(path)[1].lower()
    return {
        ".png": "image/png", ".jpg": "image/jpeg", ".jpeg": "image/jpeg",
        ".gif": "image/gif", ".webp": "image/webp",
    }.get(ext, "image/png")


def call_api(images, model, api_key):
    """Call the Anthropic Messages API with images + a forced structured-output tool.
    Uses urllib (stdlib) so no SDK is required."""
    import urllib.request

    content = []
    for path in images:
        with open(path, "rb") as f:
            data = base64.standard_b64encode(f.read()).decode("ascii")
        content.append({
            "type": "image",
            "source": {"type": "base64", "media_type": _media_type(path), "data": data},
        })
    content.append({
        "type": "text",
        "text": ("Extract the paragon boards (+ glyph + glyph level), skills (+ ranks, key passives), "
                 "and any aspect names from these screenshots. Call emit_build_parts once."),
    })

    body = {
        "model": model,
        "max_tokens": 4096,
        # Cache the (static) system prompt + tool schema across repeated captures.
        "system": [{"type": "text", "text": SYSTEM_PROMPT, "cache_control": {"type": "ephemeral"}}],
        "tools": [TOOL],
        "tool_choice": {"type": "tool", "name": "emit_build_parts"},
        "messages": [{"role": "user", "content": content}],
    }
    req = urllib.request.Request(
        API_URL,
        data=json.dumps(body).encode("utf-8"),
        headers={
            "x-api-key": api_key,
            "anthropic-version": "2023-06-01",
            "content-type": "application/json",
        },
        method="POST",
    )
    with urllib.request.urlopen(req, timeout=120) as resp:
        payload = json.loads(resp.read().decode("utf-8"))

    for block in payload.get("content", []):
        if block.get("type") == "tool_use" and block.get("name") == "emit_build_parts":
            return block["input"]
    raise RuntimeError("model did not return the emit_build_parts tool call")


def main(argv=None):
    ap = argparse.ArgumentParser(description="D4Scanner vision capture (paragon/skills/aspects)")
    ap.add_argument("--images", nargs="*", default=[], help="screenshot files to parse")
    ap.add_argument("--stub", help="skip the API; read this fixture JSON and emit it")
    ap.add_argument("--out", default="vision.json")
    ap.add_argument("--model", default=MODEL_DEFAULT)
    args = ap.parse_args(argv)

    if args.stub:
        with open(args.stub, "r", encoding="utf-8") as f:
            parts = json.load(f)
        print(f"[stub] using fixture {args.stub}", file=sys.stderr)
    else:
        if not args.images:
            print("error: provide --images <files...> or --stub <fixture.json>", file=sys.stderr)
            return 2
        key = os.environ.get("ANTHROPIC_API_KEY")
        if not key:
            print("error: ANTHROPIC_API_KEY not set (or use --stub to run without the API)", file=sys.stderr)
            return 2
        parts = call_api(args.images, args.model, key)

    out = {
        "source": "vision",
        "aspects": parts.get("aspects", []),
        "skills": parts.get("skills", []),
        "paragon": parts.get("paragon", []),
    }
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(out, f, indent=2, ensure_ascii=False)
    print(f"vision -> {args.out}  "
          f"({len(out['skills'])} skills, {len(out['paragon'])} boards, {len(out['aspects'])} aspects)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
