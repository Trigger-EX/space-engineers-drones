#!/usr/bin/env python3
"""Builds releases/SE_SimPack_<rev>.zip: precompiled simulator + blueprints + data + plans + runners (see pc/README_PC.md).
Run after: bash sim/build.sh && python3 -I tools/build_all.py"""
import os, sys, zipfile, subprocess, glob
R = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
rev = subprocess.run(["git", "-C", R, "rev-parse", "--short", "HEAD"], capture_output=True, text=True).stdout.strip() or "dev"
out = os.path.join(R, "releases", f"SE_SimPack_{rev}.zip")
os.makedirs(os.path.dirname(out), exist_ok=True)
top = "SE_SimPack/"
n = 0
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    def add(src, dst):
        global n; z.write(src, top + dst); n += 1
    for f in ["SimHost.exe", "SeApi.dll", "SimShim.dll"]: add(f"{R}/out/sim/{f}", f"out/sim/{f}")
    for f in glob.glob(f"{R}/out/sim/scripts/*.dll") + [f"{R}/out/sim/scripts/manifest.tsv"]: add(f, "out/sim/scripts/" + os.path.basename(f))
    for d in sorted(os.listdir(f"{R}/out/Blueprints")):
        p = f"{R}/out/Blueprints/{d}/bp.sbc"
        if os.path.isfile(p): add(p, f"out/Blueprints/{d}/bp.sbc")
    add(f"{R}/defs_stats.json", "defs_stats.json")
    for f in glob.glob(f"{R}/scripts/*.cs"): add(f, "scripts/" + os.path.basename(f))
    for f in glob.glob(f"{R}/pc/*") + glob.glob(f"{R}/pc/plans/*"):
        if os.path.isfile(f):
            rel = os.path.relpath(f, f"{R}/pc")
            add(f, rel)
    for d, _, fs in os.walk(f"{R}/pc/enemies"):          # enemy blueprints for plans/enemy.plan (<Name>/bp.sbc)
        for f in sorted(fs):
            if f == "bp.sbc": add(os.path.join(d, f), os.path.relpath(os.path.join(d, f), f"{R}/pc"))
    z.writestr(top + "VERSION.txt", f"git {rev}\n")
print(out, n, "files", os.path.getsize(out) // 1024, "KB")
