#!/usr/bin/env python3
"""
D4Scanner — compare a Maxroll build against your live in-game capture, in one step.

  TARGET:  a Maxroll URL (imported live) or an existing target.json
  LIVE:    your captured gear.json (TTS) and/or vision.json (paragon/skills)

Examples:
  # import a Maxroll build and compare against your captured gear+vision:
  python d4_compare.py --maxroll "https://maxroll.gg/d4/build-guides/bone-spear-necromancer-guide" \
                       --gear gear.json --vision vision.json

  # reuse an existing target.json:
  python d4_compare.py --target target.json --gear gear.json

Writes target.json + build.json to --out-dir (default: cwd), prints a report, and
points you at the tracker UI for the visual diff.
"""
import argparse
import os
import subprocess
import sys

ROOT = os.path.dirname(os.path.abspath(__file__))
P = os.path.join(ROOT, "parser")
T = os.path.join(ROOT, "tracker")


def make_report(target_path, build_path, out_html):
    """Bake the data + diff engine into ONE self-contained HTML so you never drop
    a JSON file — just open it (and refresh after re-running to update)."""
    tracker = open(os.path.join(T, "d4-tracker.html"), encoding="utf-8").read()
    diffjs = open(os.path.join(T, "diff.js"), encoding="utf-8").read()
    target = open(target_path, encoding="utf-8").read()
    build = open(build_path, encoding="utf-8").read() if os.path.exists(build_path) else "{}"

    def safe(js):  # avoid a literal </script> inside embedded JSON closing the tag
        return js.replace("</", "<\\/")

    # inline diff.js (the report must not depend on the external file)
    tracker = tracker.replace('<script src="diff.js"></script>', "<script>\n" + diffjs + "\n</script>")
    # inject baked data right before the engine script (which auto-loads window.__D4_*)
    inject = ("<script>window.__D4_BAKED__=true;window.__D4_TARGET__=" + safe(target) +
              ";window.__D4_LIVE__=" + safe(build) + ";</script>\n")
    tracker = tracker.replace("<script>\n/* ---------------- bundled demo",
                              inject + "<script>\n/* ---------------- bundled demo", 1)
    with open(out_html, "w", encoding="utf-8") as f:
        f.write(tracker)
    return out_html


def run(cmd):
    print("\n$ " + " ".join(os.path.basename(c) if str(c).endswith((".py", ".js")) else str(c) for c in cmd))
    try:
        subprocess.run(cmd, check=True)
    except subprocess.CalledProcessError as e:
        # the child already printed a clear message; don't add a noisy traceback
        sys.exit(e.returncode)


def main(argv=None):
    ap = argparse.ArgumentParser(description="Compare a Maxroll build vs your in-game capture")
    src = ap.add_mutually_exclusive_group(required=True)
    src.add_argument("--maxroll", help="Maxroll build-guide/planner URL (or id) to import as the target")
    src.add_argument("--target", help="existing target.json")
    ap.add_argument("--profile", help="Maxroll build profile to use (substring, e.g. Endgame)")
    ap.add_argument("--gear", help="captured gear.json (TTS channel)")
    ap.add_argument("--vision", help="captured vision.json (paragon/skills); or a fixture for --stub")
    ap.add_argument("--out-dir", default=os.getcwd())
    args = ap.parse_args(argv)

    if not args.gear and not args.vision:
        ap.error("provide at least one of --gear / --vision (your live capture)")

    py = sys.executable
    os.makedirs(args.out_dir, exist_ok=True)
    target = args.target
    if args.maxroll:
        target = os.path.join(args.out_dir, "target.json")
        cmd = [py, os.path.join(P, "d4_maxroll_import.py"), args.maxroll, "--out", target]
        if args.profile:
            cmd += ["--profile", args.profile]
        run(cmd)

    build = os.path.join(args.out_dir, "build.json")
    cmd = [py, os.path.join(P, "d4_build_merge.py"), "--out", build]
    if args.gear:
        cmd += ["--gear", args.gear]
    if args.vision:
        cmd += ["--vision", args.vision]
    run(cmd)

    try:
        run(["node", os.path.join(T, "report.js"), target, build])
    except FileNotFoundError:
        print("\n(node not found — skipping the terminal report)")

    report = os.path.join(args.out_dir, "d4-report.html")
    make_report(target, build, report)

    print("\n" + "=" * 64)
    print("Open the self-contained report (no file dropping — just refresh after re-running):")
    print("   " + report)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
