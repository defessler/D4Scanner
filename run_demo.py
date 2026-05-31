#!/usr/bin/env python3
"""
D4Scanner end-to-end demo. Runs the whole pipeline on the bundled sample data:

  gear (TTS parse) + vision (stub) -> merge -> build.json -> diff vs target

Then prints a terminal report. No game, no DLL, no API key needed.

    python run_demo.py
"""
import os
import subprocess
import sys

ROOT = os.path.dirname(os.path.abspath(__file__))
P = os.path.join(ROOT, "parser")
T = os.path.join(ROOT, "tracker")
S = os.path.join(ROOT, "samples")


def run(cmd, **kw):
    print("\n$ " + " ".join(os.path.basename(c) if c.endswith(('.py', '.js')) else c for c in cmd))
    r = subprocess.run(cmd, **kw)
    if r.returncode != 0:
        sys.exit(r.returncode)


def main():
    py = sys.executable
    run([py, os.path.join(P, "d4_gear_capture.py"),
         "--log", os.path.join(S, "sample_tts.log"), "--once",
         "--out", os.path.join(S, "sample_gear.json")])
    run([py, os.path.join(P, "d4_vision_capture.py"),
         "--stub", os.path.join(S, "sample_vision.json"),
         "--out", os.path.join(S, "sample_vision_out.json")])
    run([py, os.path.join(P, "d4_build_merge.py"),
         "--gear", os.path.join(S, "sample_gear.json"),
         "--vision", os.path.join(S, "sample_vision_out.json"),
         "--out", os.path.join(S, "sample_build.json"),
         "--name", "Zappy", "--class", "Sorcerer", "--level", "100"])

    node = "node"
    try:
        run([node, os.path.join(T, "report.js"),
             os.path.join(S, "sample_target.json"),
             os.path.join(S, "sample_build.json")])
    except FileNotFoundError:
        print("\n(node not found — skipping terminal report; the tracker HTML still works)")

    print("=" * 64)
    print("Done. Now open the tracker in a browser:")
    print("   " + os.path.join(T, "d4-tracker.html"))
    print("and click 'Load bundled demo' (or drop samples/sample_target.json")
    print("and samples/sample_build.json onto the two drop zones).")


if __name__ == "__main__":
    main()
