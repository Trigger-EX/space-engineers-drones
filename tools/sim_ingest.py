#!/usr/bin/env python3
"""Ingest SE_results.zip made on the user's PC: python3 -I tools/sim_ingest.py SE_results.zip [--name pc1]
Unpacks to out/sim/results/<name>/<experiment>, checks the 'verify' experiment against pc/reference/verify-linux.runs.jsonl
(are the PC's numbers bit-identical to the reference engine?), and writes docs/pc/<name>/<experiment>.md via sim_report.py."""
import sys, os, json, zipfile, subprocess, argparse
R = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ap = argparse.ArgumentParser(); ap.add_argument("zip"); ap.add_argument("--name", default="pc1"); a = ap.parse_args()
dest = os.path.join(R, "out", "sim", "results", a.name); os.makedirs(dest, exist_ok=True)
with zipfile.ZipFile(a.zip) as z:
    for m in z.namelist():                       # untrusted archive: refuse path escapes
        if m.startswith("/") or ".." in m.split("/"): raise SystemExit("unsafe path in zip: " + m)
    z.extractall(dest)
base = os.path.join(dest, "results") if os.path.isdir(os.path.join(dest, "results")) else dest
print(open(os.path.join(base, "machine.txt")).read().strip() if os.path.exists(os.path.join(base, "machine.txt")) else "no machine.txt")
def load(p):
    d = {}
    for l in open(p):
        r = json.loads(l); d[(r["scenario"], r["seed_index"])] = r
    return d
ver = os.path.join(base, "verify", "runs.jsonl"); ref = os.path.join(R, "pc", "reference", "verify-linux.runs.jsonl")
if os.path.exists(ver) and os.path.exists(ref):
    a1, b1 = load(ver), load(ref); keys = set(a1) & set(b1)
    same = sum(1 for k in keys if a1[k]["outcome"] == b1[k]["outcome"] and a1[k]["duration_s"] == b1[k]["duration_s"]
               and [s["shots"] for s in a1[k]["ships"]] == [s["shots"] for s in b1[k]["ships"]])
    print(f"verify: {same}/{len(keys)} runs identical to the reference engine (outcome, duration, shots)."
          + ("" if same == len(keys) else " Differences are expected from floating-point details: compare win rates instead (chaotic divergence)."))
outd = os.path.join(R, "docs", "pc", a.name); os.makedirs(outd, exist_ok=True)
for e in sorted(os.listdir(base)):
    p = os.path.join(base, e)
    if os.path.isdir(p) and os.path.exists(os.path.join(p, "runs.jsonl")):
        st = open(os.path.join(p, "status.txt")).read().strip() if os.path.exists(os.path.join(p, "status.txt")) else ""
        subprocess.run([sys.executable, "-I", os.path.join(R, "tools", "sim_report.py"), p, "--title", f"{a.name}: {e}", "--out", os.path.join(outd, e + ".md")], check=False)
        print(e, "|", st)
    elif os.path.isdir(p): print(e, "| (no runs.jsonl)")
