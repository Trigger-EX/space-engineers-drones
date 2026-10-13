#!/usr/bin/env python3
"""Builds releases/Seeker_Test_v<VERSION>.zip (option-4 in-game test pack) from out/Blueprints (run tools/build_all.py first).

  python3 -I tools/make_seeker_pack.py
Contents: Blueprints/{Longbow L Seeker, Basilisk Seeker}(+ Lite), Scripts/{SeekerProbe, LongbowSniperAI, BasiliskAI}.cs (minified,
paste-ready) + Scripts/Source/ (commented), README.md = packages/README_SeekerTest.md.
"""
import os, sys, zipfile
HERE = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.dirname(HERE)
sys.path.insert(0, HERE)
from minify_cs import minify
import build_all

SHIPS = ["Longbow L Seeker", "Longbow L Seeker Lite", "Basilisk Seeker", "Basilisk Seeker Lite"]
SCRIPTS = ["SeekerProbe.cs", "LongbowSniperAI.cs", "BasiliskAI.cs"]


def main():
    files = {}
    for s in SHIPS:
        d = os.path.join(ROOT, "out", "Blueprints", s)
        for f in ("bp.sbc", "thumb.png"):
            files[f"Blueprints/{s}/{f}"] = open(os.path.join(d, f), "rb").read()
    for s in SCRIPTS:
        src = open(os.path.join(ROOT, "scripts", s), encoding="utf-8").read()
        m = minify(src)
        assert len(m) <= 100000, (s, len(m))
        files[f"Scripts/{s}"] = m.encode("utf-8")
        files[f"Scripts/Source/{s}"] = src.encode("utf-8")
    files["README.md"] = open(os.path.join(ROOT, "packages", "README_SeekerTest.md"), "rb").read()
    out = os.path.join(ROOT, "releases", f"Seeker_Test_v{build_all.VERSION}.zip")
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for n in sorted(files):
            zi = zipfile.ZipInfo(n, (2026, 1, 1, 0, 0, 0)); zi.compress_type = zipfile.ZIP_DEFLATED; zi.external_attr = 0o644 << 16
            z.writestr(zi, files[n])
    print(out, os.path.getsize(out), "bytes,", len(files), "files")


if __name__ == "__main__":
    main()
